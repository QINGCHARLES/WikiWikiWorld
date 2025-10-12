﻿#nullable enable

using System.Text;
using AngleSharp;
using AngleSharp.Html.Parser;

namespace WikiWikiWorld.Web;

public sealed class HtmlPrettifyMiddleware(RequestDelegate Next)
{
	public async Task Invoke(HttpContext Context)
	{
		Stream OriginalBodyStream = Context.Response.Body;
		using MemoryStream CaptureStream = new();
		Context.Response.Body = CaptureStream;

		try
		{
			await Next(Context);

			if (!IsHtmlResponse(Context.Response))
			{
				CaptureStream.Seek(0, SeekOrigin.Begin);
				await CaptureStream.CopyToAsync(OriginalBodyStream, Context.RequestAborted);
				return;
			}

			CaptureStream.Seek(0, SeekOrigin.Begin);

			Encoding SelectedEncoding = GetEncoding(Context.Response.ContentType) ?? Encoding.UTF8;
			using StreamReader Reader = new(CaptureStream, SelectedEncoding, true, 1024, leaveOpen: true);
			string RawHtml = await Reader.ReadToEndAsync(Context.RequestAborted);

			string FormattedHtml = await PrettifyHtmlAsync(RawHtml);

			// Let the server decide framing (chunked/compressed). Avoid mismatched Content-Length.
			Context.Response.Headers.ContentLength = null;
			Context.Response.Body = OriginalBodyStream;

			byte[] ResponseBytes = SelectedEncoding.GetBytes(FormattedHtml);
			await Context.Response.Body.WriteAsync(ResponseBytes, Context.RequestAborted);
		}
		finally
		{
			// Ensure Body is always restored even on exceptions / non-HTML paths
			Context.Response.Body = OriginalBodyStream;
		}
	}

	private static bool IsHtmlResponse(HttpResponse Response)
	{
		string? ContentType = Response.ContentType;
		return !string.IsNullOrWhiteSpace(ContentType)
			&& ContentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);
	}

	private static Encoding? GetEncoding(string? ContentType)
	{
		if (string.IsNullOrWhiteSpace(ContentType))
		{
			return null;
		}

		int Index = ContentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
		if (Index < 0)
		{
			return null;
		}

		string Charset = ContentType[(Index + "charset=".Length)..].Trim().Trim(';').Trim();

		try
		{
			return Encoding.GetEncoding(Charset);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	private static async Task<string> PrettifyHtmlAsync(string Html)
	{
		HtmlParser Parser = new();
		AngleSharp.Dom.IDocument Document = await Parser.ParseDocumentAsync(Html);

		InlineAwarePrettyFormatter Formatter = new()
		{
			Indentation = "\t",
			NewLine = "\n"
		};

		// No NormalizeDocument, no regex hacks needed
		return Document.ToHtml(Formatter);
	}
}

public static class HtmlPrettifyMiddlewareExtensions
{
	public static IApplicationBuilder UseHtmlPrettify(this IApplicationBuilder ApplicationBuilder) =>
		ApplicationBuilder.UseMiddleware<HtmlPrettifyMiddleware>();
}
