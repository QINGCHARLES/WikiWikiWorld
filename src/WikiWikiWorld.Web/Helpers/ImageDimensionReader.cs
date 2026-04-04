namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Reads image dimensions from file headers without loading the full image into memory.
/// Supports JPEG, PNG, GIF, and WebP formats.
/// </summary>
public static class ImageDimensionReader
{
	/// <summary>
	/// Reads the pixel dimensions of an image from its file header.
	/// </summary>
	/// <param name="FilePath">The absolute path to the image file.</param>
	/// <returns>The width and height in pixels, or (0, 0) if the format is unsupported or unreadable.</returns>
	public static (int WidthPx, int HeightPx) ReadDimensions(string FilePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(FilePath);

		if (!File.Exists(FilePath))
		{
			return (0, 0);
		}

		try
		{
			using FileStream Stream = new(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			using BinaryReader Reader = new(Stream);

			if (Stream.Length < 8)
			{
				return (0, 0);
			}

			byte[] Header = Reader.ReadBytes(8);

			// PNG: 89 50 4E 47 0D 0A 1A 0A
			if (Header[0] == 0x89 && Header[1] == 0x50 && Header[2] == 0x4E && Header[3] == 0x47)
			{
				return ReadPngDimensions(Reader);
			}

			// JPEG: FF D8 FF
			if (Header[0] == 0xFF && Header[1] == 0xD8 && Header[2] == 0xFF)
			{
				Stream.Position = 0;
				return ReadJpegDimensions(Reader, Stream);
			}

			// GIF: 47 49 46 38
			if (Header[0] == 0x47 && Header[1] == 0x49 && Header[2] == 0x46 && Header[3] == 0x38)
			{
				return ReadGifDimensions(Reader, Stream);
			}

			// WebP: 52 49 46 46 (RIFF) ... 57 45 42 50 (WEBP)
			if (Header[0] == 0x52 && Header[1] == 0x49 && Header[2] == 0x46 && Header[3] == 0x46)
			{
				return ReadWebPDimensions(Reader, Stream);
			}

			return (0, 0);
		}
		catch
		{
			return (0, 0);
		}
	}

	/// <summary>
	/// Reads dimensions from a PNG IHDR chunk (bytes 16–23).
	/// </summary>
	/// <param name="Reader">The binary reader positioned after the 8-byte PNG signature.</param>
	/// <returns>The width and height in pixels.</returns>
	private static (int WidthPx, int HeightPx) ReadPngDimensions(BinaryReader Reader)
	{
		// Skip IHDR chunk length (4 bytes) and chunk type "IHDR" (4 bytes)
		Reader.ReadBytes(8);

		// Width and height are big-endian 4-byte integers
		int WidthPx = ReadBigEndianInt32(Reader);
		int HeightPx = ReadBigEndianInt32(Reader);
		return (WidthPx, HeightPx);
	}

	/// <summary>
	/// Reads dimensions from a JPEG file by scanning for a Start of Frame (SOF) marker.
	/// </summary>
	/// <param name="Reader">The binary reader.</param>
	/// <param name="Stream">The underlying file stream for position tracking.</param>
	/// <returns>The width and height in pixels.</returns>
	private static (int WidthPx, int HeightPx) ReadJpegDimensions(BinaryReader Reader, FileStream Stream)
	{
		// Skip SOI marker (FF D8)
		Stream.Position = 2;

		while (Stream.Position < Stream.Length - 1)
		{
			byte Marker1 = Reader.ReadByte();
			if (Marker1 != 0xFF)
			{
				continue;
			}

			byte Marker2 = Reader.ReadByte();

			// Skip padding FF bytes
			while (Marker2 == 0xFF && Stream.Position < Stream.Length)
			{
				Marker2 = Reader.ReadByte();
			}

			// SOF markers: C0–C3, C5–C7, C9–CB, CD–CF
			bool IsSofMarker = Marker2 switch
			{
				>= 0xC0 and <= 0xC3 => true,
				>= 0xC5 and <= 0xC7 => true,
				>= 0xC9 and <= 0xCB => true,
				>= 0xCD and <= 0xCF => true,
				_ => false
			};

			if (IsSofMarker)
			{
				// Skip segment length (2 bytes) and precision (1 byte)
				Reader.ReadBytes(3);

				// Height and width are big-endian 2-byte integers
				int HeightPx = ReadBigEndianInt16(Reader);
				int WidthPx = ReadBigEndianInt16(Reader);
				return (WidthPx, HeightPx);
			}

			// Not a SOF marker — skip this segment
			if (Marker2 == 0x00 || Marker2 == 0x01 || (Marker2 >= 0xD0 && Marker2 <= 0xD9))
			{
				// Standalone markers (no payload)
				continue;
			}

			if (Stream.Position + 2 > Stream.Length)
			{
				break;
			}

			int SegmentLength = ReadBigEndianInt16(Reader);
			if (SegmentLength < 2)
			{
				break;
			}

			Stream.Position += SegmentLength - 2;
		}

		return (0, 0);
	}

	/// <summary>
	/// Reads dimensions from a GIF file header (bytes 6–9, little-endian).
	/// </summary>
	/// <param name="Reader">The binary reader.</param>
	/// <param name="Stream">The underlying stream.</param>
	/// <returns>The width and height in pixels.</returns>
	private static (int WidthPx, int HeightPx) ReadGifDimensions(BinaryReader Reader, FileStream Stream)
	{
		// Position past the 8-byte header already read; rewind to byte 6
		Stream.Position = 6;
		int WidthPx = Reader.ReadUInt16(); // little-endian
		int HeightPx = Reader.ReadUInt16();
		return (WidthPx, HeightPx);
	}

	/// <summary>
	/// Reads dimensions from a WebP file (VP8, VP8L, or VP8X sub-format).
	/// </summary>
	/// <param name="Reader">The binary reader.</param>
	/// <param name="Stream">The underlying stream.</param>
	/// <returns>The width and height in pixels.</returns>
	private static (int WidthPx, int HeightPx) ReadWebPDimensions(BinaryReader Reader, FileStream Stream)
	{
		// Bytes 8–11 should be "WEBP"
		Stream.Position = 8;
		byte[] WebpSig = Reader.ReadBytes(4);
		if (WebpSig[0] != 0x57 || WebpSig[1] != 0x45 || WebpSig[2] != 0x42 || WebpSig[3] != 0x50)
		{
			return (0, 0);
		}

		// Read sub-chunk FourCC at byte 12
		byte[] ChunkId = Reader.ReadBytes(4);
		string FourCc = Encoding.ASCII.GetString(ChunkId);

		// Skip chunk size (4 bytes)
		Reader.ReadBytes(4);

		switch (FourCc)
		{
			case "VP8 ":
			{
				// Lossy: skip 3-byte frame tag, then read 2-byte LE width and height (masked to 14 bits)
				Reader.ReadBytes(3);
				// 3 bytes: sync code 9D 01 2A
				byte[] Sync = Reader.ReadBytes(3);
				if (Sync[0] != 0x9D || Sync[1] != 0x01 || Sync[2] != 0x2A)
				{
					return (0, 0);
				}

				int WidthPx = Reader.ReadUInt16() & 0x3FFF;
				int HeightPx = Reader.ReadUInt16() & 0x3FFF;
				return (WidthPx, HeightPx);
			}
			case "VP8L":
			{
				// Lossless: skip 1-byte signature (0x2F), then read 4 bytes for packed width/height
				byte Signature = Reader.ReadByte();
				if (Signature != 0x2F)
				{
					return (0, 0);
				}

				uint Bits = Reader.ReadUInt32();
				int WidthPx = (int)(Bits & 0x3FFF) + 1;
				int HeightPx = (int)((Bits >> 14) & 0x3FFF) + 1;
				return (WidthPx, HeightPx);
			}
			case "VP8X":
			{
				// Extended: skip 4-byte flags, then read 3-byte LE width-1 and 3-byte LE height-1
				Reader.ReadBytes(4);
				byte[] WidthBytes = Reader.ReadBytes(3);
				byte[] HeightBytes = Reader.ReadBytes(3);

				int WidthPx = (WidthBytes[0] | (WidthBytes[1] << 8) | (WidthBytes[2] << 16)) + 1;
				int HeightPx = (HeightBytes[0] | (HeightBytes[1] << 8) | (HeightBytes[2] << 16)) + 1;
				return (WidthPx, HeightPx);
			}
			default:
				return (0, 0);
		}
	}

	/// <summary>
	/// Reads a big-endian 32-bit integer from the reader.
	/// </summary>
	/// <param name="Reader">The binary reader.</param>
	/// <returns>The 32-bit integer value.</returns>
	private static int ReadBigEndianInt32(BinaryReader Reader)
	{
		byte[] Bytes = Reader.ReadBytes(4);
		return (Bytes[0] << 24) | (Bytes[1] << 16) | (Bytes[2] << 8) | Bytes[3];
	}

	/// <summary>
	/// Reads a big-endian 16-bit integer from the reader.
	/// </summary>
	/// <param name="Reader">The binary reader.</param>
	/// <returns>The 16-bit integer value.</returns>
	private static int ReadBigEndianInt16(BinaryReader Reader)
	{
		byte[] Bytes = Reader.ReadBytes(2);
		return (Bytes[0] << 8) | Bytes[1];
	}
}
