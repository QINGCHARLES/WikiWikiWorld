using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Controllers.Api;
using WikiWikiWorld.Web.Pages.Article;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Exercises upload recovery against committed SQLite rows and actual staged byte contents.</summary>
public sealed class FileUploadRecoveryTests : IDisposable
{
	private const int SiteId = 1;
	private readonly Guid UserId = Guid.NewGuid();
	private readonly SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
	private readonly string StorageRoot = Path.Combine(Path.GetTempPath(), $"WikiWikiWorld.UploadRecoveryTests-{Guid.NewGuid():N}");

	/// <summary>Creates an isolated schema and uploader without using any application database or upload directory.</summary>
	public FileUploadRecoveryTests()
	{
		Connection.Open();
		using WikiWikiWorldDbContext Context = CreateContext();
		Context.Database.EnsureCreated();
		Context.Users.Add(new() { Id = UserId, UserName = "Uploader" });
		Context.SaveChanges();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Connection.Dispose();
		if (Directory.Exists(StorageRoot))
		{
			Directory.Delete(StorageRoot, recursive: true);
		}
	}

	/// <summary>Recovery must retain separate byte contents when a pending upload has already been superseded.</summary>
	[Fact]
	public async Task RecoveryFinalizesHistoricalAndCurrentUploads()
	{
		Guid CanonicalFileId = Guid.NewGuid();
		StagedFileUpload First = await StageAsync(CanonicalFileId, "first");
		FileRevision Historical = await CommitAsync(First, IsCurrent: false);
		StagedFileUpload Second = await StageAsync(CanonicalFileId, "second");
		FileRevision Current = await CommitAsync(Second, IsCurrent: true);
		byte[] FirstBytes = await File.ReadAllBytesAsync(First.TemporaryFilePath);
		byte[] SecondBytes = await File.ReadAllBytesAsync(Second.TemporaryFilePath);

		using WikiWikiWorldDbContext Context = CreateContext();
		FileUploadStorageService Storage = CreateStorage(Context);
		await Storage.FinalizeCommittedUploadsAndDeleteStaleTempFilesAsync(default);

		string HistoricalPath = Storage.GetFinalFilePath(SiteId, CanonicalFileId, Historical.Id, First.OriginalFileName);
		Assert.True(File.Exists(HistoricalPath), "A superseded committed upload must still be recovered.");
		Assert.Equal(FirstBytes, await File.ReadAllBytesAsync(HistoricalPath));
		Assert.Equal(SecondBytes, await File.ReadAllBytesAsync(Storage.GetFinalFilePath(SiteId, CanonicalFileId, Current.Id, Second.OriginalFileName)));
	}

	/// <summary>A newer uncommitted attempt must never supply the bytes for an older committed revision.</summary>
	[Fact]
	public async Task RecoveryDoesNotSubstituteNewestUncommittedUpload()
	{
		Guid CanonicalFileId = Guid.NewGuid();
		StagedFileUpload Committed = await StageAsync(CanonicalFileId, "committed");
		FileRevision Revision = await CommitAsync(Committed, IsCurrent: true);
		StagedFileUpload Uncommitted = await StageAsync(CanonicalFileId, "uncommitted");
		File.SetLastWriteTimeUtc(Committed.TemporaryFilePath, DateTime.UtcNow.AddMinutes(-2));
		File.SetLastWriteTimeUtc(Uncommitted.TemporaryFilePath, DateTime.UtcNow);
		byte[] Expected = await File.ReadAllBytesAsync(Committed.TemporaryFilePath);

		using WikiWikiWorldDbContext Context = CreateContext();
		FileUploadStorageService Storage = CreateStorage(Context);
		await Storage.FinalizeCommittedUploadsAndDeleteStaleTempFilesAsync(default);

		Assert.Equal(Expected, await File.ReadAllBytesAsync(Storage.GetFinalFilePath(SiteId, CanonicalFileId, Revision.Id, Revision.Filename)));
		Assert.True(File.Exists(Uncommitted.TemporaryFilePath));
	}

	/// <summary>Recovery preserves conflicting final bytes and their claimed staging instead of replacing either.</summary>
	[Fact]
	public async Task ExistingDestinationIsNotOverwrittenOrCleanedUp()
	{
		StagedFileUpload Upload = await StageAsync(Guid.NewGuid(), "staged");
		FileRevision Revision = await CommitAsync(Upload, IsCurrent: false);
		File.SetLastWriteTimeUtc(Upload.TemporaryFilePath, DateTime.UtcNow.AddHours(-2));
		using WikiWikiWorldDbContext Context = CreateContext();
		FileUploadStorageService Storage = CreateStorage(Context);
		string Destination = Storage.GetFinalFilePath(SiteId, Upload.CanonicalFileId, Revision.Id, Revision.Filename);
		Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
		await File.WriteAllTextAsync(Destination, "existing final bytes");

		FileUploadStartupMaintenanceResult Result = await Storage.FinalizeCommittedUploadsAndDeleteStaleTempFilesAsync(default);

		Assert.Equal("existing final bytes", await File.ReadAllTextAsync(Destination));
		Assert.True(File.Exists(Upload.TemporaryFilePath));
		Assert.Equal(0, Result.DeletedTempCount);
		Assert.Contains(Result.Issues, Issue => Issue.TemporaryFilePath == Upload.TemporaryFilePath);
		Assert.Throws<IOException>(() => Storage.FinalizeStagedUpload(Upload, SiteId, Revision.Id));
	}

	/// <summary>Successful finalization and startup recovery can be repeated without further mutations.</summary>
	[Fact]
	public async Task FinalizationAndRecoveryAreRepeatable()
	{
		StagedFileUpload Upload = await StageAsync(Guid.NewGuid(), "repeatable");
		FileRevision Revision = await CommitAsync(Upload, IsCurrent: true);
		byte[] Expected = await File.ReadAllBytesAsync(Upload.TemporaryFilePath);
		using WikiWikiWorldDbContext Context = CreateContext();
		FileUploadStorageService Storage = CreateStorage(Context);

		Storage.FinalizeStagedUpload(Upload, SiteId, Revision.Id);
		Storage.FinalizeStagedUpload(Upload, SiteId, Revision.Id);
		FileUploadStartupMaintenanceResult Result = await Storage.FinalizeCommittedUploadsAndDeleteStaleTempFilesAsync(default);

		Assert.Equal(Expected, await File.ReadAllBytesAsync(Storage.GetFinalFilePath(SiteId, Upload.CanonicalFileId, Revision.Id, Revision.Filename)));
		Assert.Equal(0, Result.FinalizedCount);
		Assert.Empty(Result.Issues);
	}

	/// <summary>Age-based cleanup removes rolled-back attempts, while retaining recent attempts and ambiguous legacy bytes.</summary>
	[Fact]
	public async Task CleanupDistinguishesRolledBackRecentAndLegacyStaging()
	{
		StagedFileUpload RolledBack = await StageAsync(Guid.NewGuid(), "rolled back");
		StagedFileUpload Recent = await StageAsync(Guid.NewGuid(), "recent");
		using (WikiWikiWorldDbContext Context = CreateContext())
		{
			await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync();
			Context.FileRevisions.Add(CreateRevision(RolledBack, IsCurrent: true));
			await Context.SaveChangesAsync();
			await Transaction.RollbackAsync();
		}
		File.SetLastWriteTimeUtc(RolledBack.TemporaryFilePath, DateTime.UtcNow.AddHours(-2));
		string LegacyPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Recent.TemporaryFilePath))!, $"{Recent.CanonicalFileId}.svg.{Guid.NewGuid():N}.tmp");
		await File.WriteAllTextAsync(LegacyPath, "legacy bytes");
		File.SetLastWriteTimeUtc(LegacyPath, DateTime.UtcNow.AddHours(-2));
		using WikiWikiWorldDbContext Maintenance = CreateContext();

		FileUploadStartupMaintenanceResult Result = await CreateStorage(Maintenance).FinalizeCommittedUploadsAndDeleteStaleTempFilesAsync(default);

		Assert.False(File.Exists(RolledBack.TemporaryFilePath));
		Assert.True(File.Exists(Recent.TemporaryFilePath));
		Assert.Equal("legacy bytes", await File.ReadAllTextAsync(LegacyPath));
		Assert.Equal(1, Result.DeletedTempCount);
		Assert.Contains(Result.Issues, Issue => Issue.TemporaryFilePath == LegacyPath);
	}

	/// <summary>Even soft-deleted claims survive catch-path cleanup and startup age-based cleanup.</summary>
	[Fact]
	public async Task DeletedCommittedClaimIsRetained()
	{
		StagedFileUpload Upload = await StageAsync(Guid.NewGuid(), "committed");
		FileRevision Revision = await CommitAsync(Upload, IsCurrent: false);
		using WikiWikiWorldDbContext Context = CreateContext();
		await Context.FileRevisions.Where(r => r.Id == Revision.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.DateDeleted, DateTimeOffset.UtcNow));
		File.SetLastWriteTimeUtc(Upload.TemporaryFilePath, DateTime.UtcNow.AddHours(-2));
		FileUploadStorageService Storage = CreateStorage(Context);

		await Storage.CleanupUncommittedUploadAsync(Upload, default);
		FileUploadStartupMaintenanceResult Result = await Storage.FinalizeCommittedUploadsAndDeleteStaleTempFilesAsync(default);

		Assert.True(File.Exists(Upload.TemporaryFilePath));
		Assert.Equal(0, Result.FinalizedCount);
		Assert.Equal(0, Result.DeletedTempCount);
		Assert.Single(Result.Issues);
	}

	/// <summary>Explicit failure cleanup deletes only attempts with no committed claim, independent of the tracker.</summary>
	[Fact]
	public async Task FailureCleanupRetainsCommittedBytesAndDeletesUncommittedBytes()
	{
		StagedFileUpload Committed = await StageAsync(Guid.NewGuid(), "committed");
		await CommitAsync(Committed, IsCurrent: true);
		StagedFileUpload Abandoned = await StageAsync(Guid.NewGuid(), "abandoned");
		using WikiWikiWorldDbContext Context = CreateContext();
		FileUploadStorageService Storage = CreateStorage(Context);

		await Storage.CleanupUncommittedUploadAsync(Committed, default);
		await Storage.CleanupUncommittedUploadAsync(Abandoned, default);

		Assert.True(File.Exists(Committed.TemporaryFilePath));
		Assert.False(File.Exists(Abandoned.TemporaryFilePath));
	}

	/// <summary>A copied claim token in an incompatible site, canonical ID, or extension never becomes a new ownership guess.</summary>
	/// <param name="Mismatch">The staging context to corrupt after commit.</param>
	[Theory]
	[InlineData("site")]
	[InlineData("canonical")]
	[InlineData("extension")]
	[InlineData("malformed")]
	public async Task MismatchedClaimsRemainUntouched(string Mismatch)
	{
		StagedFileUpload Upload = await StageAsync(Guid.NewGuid(), "original");
		await CommitAsync(Upload, IsCurrent: true);
		string ChangedPath = Mismatch switch
		{
			"site" => Upload.TemporaryFilePath.Replace($"{Path.DirectorySeparatorChar}1{Path.DirectorySeparatorChar}images", $"{Path.DirectorySeparatorChar}2{Path.DirectorySeparatorChar}images", StringComparison.Ordinal),
			"canonical" => Upload.TemporaryFilePath.Replace(Upload.CanonicalFileId.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal),
			"extension" => Upload.TemporaryFilePath.Replace(".svg.tmp", ".png.tmp", StringComparison.Ordinal),
			_ => Path.Combine(Path.GetDirectoryName(Upload.TemporaryFilePath)!, "unrecognized.tmp")
		};
		Directory.CreateDirectory(Path.GetDirectoryName(ChangedPath)!);
		File.Move(Upload.TemporaryFilePath, ChangedPath);
		File.SetLastWriteTimeUtc(ChangedPath, DateTime.UtcNow.AddHours(-2));
		using WikiWikiWorldDbContext Context = CreateContext();

		FileUploadStartupMaintenanceResult Result = await CreateStorage(Context).FinalizeCommittedUploadsAndDeleteStaleTempFilesAsync(default);

		Assert.True(File.Exists(ChangedPath));
		Assert.Single(Result.Issues);
		Assert.Equal(0, Result.FinalizedCount);
		Assert.Equal(0, Result.DeletedTempCount);
	}

	/// <summary>A cancelled startup check leaves both committed and uncommitted bytes available for the next attempt.</summary>
	[Fact]
	public async Task CancelledMaintenanceLeavesStagingUntouched()
	{
		StagedFileUpload Upload = await StageAsync(Guid.NewGuid(), "cancelled");
		File.SetLastWriteTimeUtc(Upload.TemporaryFilePath, DateTime.UtcNow.AddHours(-2));
		using WikiWikiWorldDbContext Context = CreateContext();
		using CancellationTokenSource Cancellation = new();
		Cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateStorage(Context).FinalizeCommittedUploadsAndDeleteStaleTempFilesAsync(Cancellation.Token));
		Assert.True(File.Exists(Upload.TemporaryFilePath));
	}

	/// <summary>A copy that writes some bytes and then fails never leaves a partially staged upload behind.</summary>
	[Fact]
	public async Task FailedCopyRemovesPartialStaging()
	{
		using PartialFailureStream Input = new();
		FormFile Upload = new(Input, 0, Input.Length, "File", "picture.svg") { Headers = new HeaderDictionary(), ContentType = "image/svg+xml" };
		using WikiWikiWorldDbContext Context = CreateContext();

		await Assert.ThrowsAsync<IOException>(() => CreateStorage(Context).StageUploadedFileAsync(Upload, SiteId, Guid.NewGuid(), default));

		Assert.Empty(Directory.EnumerateFiles(StorageRoot, "*.tmp", SearchOption.AllDirectories));
	}

	/// <summary>Cancellation while copying a selected file removes its uncommitted temporary file.</summary>
	[Fact]
	public async Task CancelledCopyRemovesStaging()
	{
		using MemoryStream Input = new(new byte[10]);
		FormFile Upload = new(Input, 0, Input.Length, "File", "picture.svg") { Headers = new HeaderDictionary(), ContentType = "image/svg+xml" };
		using WikiWikiWorldDbContext Context = CreateContext();
		using CancellationTokenSource Cancellation = new();
		Cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateStorage(Context).StageUploadedFileAsync(Upload, SiteId, Guid.NewGuid(), Cancellation.Token));

		Assert.Empty(Directory.EnumerateFiles(StorageRoot, "*.tmp", SearchOption.AllDirectories));
	}

	/// <summary>All upload entry points commit exact claims and retain recoverable bytes when post-commit finalization fails.</summary>
	/// <param name="EntryPoint">The Razor or API create/replacement path to execute.</param>
	/// <param name="FailFinalization">Whether an actual conflicting directory should interrupt the final move.</param>
	[Theory]
	[InlineData("razor-create", false)]
	[InlineData("razor-replace", false)]
	[InlineData("api-create", false)]
	[InlineData("api-replace", false)]
	[InlineData("razor-create", true)]
	[InlineData("razor-replace", true)]
	[InlineData("api-create", true)]
	[InlineData("api-replace", true)]
	public async Task UploadEntryPointsCommitClaims(string EntryPoint, bool FailFinalization)
	{
		string Slug = "upload-test";
		if (EntryPoint.EndsWith("replace", StringComparison.Ordinal))
		{
			StagedFileUpload Existing = await StageAsync(Guid.NewGuid(), "old bytes");
			await CommitAsync(Existing, IsCurrent: true);
			using WikiWikiWorldDbContext Lookup = CreateContext();
			Slug = await Lookup.ArticleRevisions.Select(r => r.UrlSlug).SingleAsync();
		}
		using WikiWikiWorldDbContext Context = CreateContext();
		FileUploadStorageService Storage = CreateStorage(Context);
		if (FailFinalization)
		{
			Context.SavedChanges += (_, _) =>
			{
				FileRevision NewRevision = Context.ChangeTracker.Entries<FileRevision>().Select(e => e.Entity).Single(r => r.IsCurrent == true);
				Directory.CreateDirectory(Storage.GetFinalFilePath(SiteId, NewRevision.CanonicalFileId, NewRevision.Id, NewRevision.Filename));
			};
		}
		DefaultHttpContext Http = new();
		Http.Request.Host = new("en.example.test");
		Http.User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, UserId.ToString()), new(ClaimTypes.Name, "Uploader")], "Test"));
		SiteResolverService Resolver = new(Options.Create(new SiteConfiguration
		{
			Sites = [new() { SiteId = SiteId, Domains = ["example.test"], DefaultCulture = "en" }]
		}), new HttpContextAccessor { HttpContext = Http });
		ArticleRevisionService Revisions = new(Context, NullLogger<ArticleRevisionService>.Instance);
		byte[] Expected = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><title>new bytes</title></svg>");
		using MemoryStream Input = new(Expected);
		FormFile Upload = new(Input, 0, Expected.Length, "File", "picture.svg") { Headers = new HeaderDictionary(), ContentType = "image/svg+xml" };
		IActionResult Result;
		if (EntryPoint.StartsWith("razor", StringComparison.Ordinal))
		{
			using UserStore<User, Role, WikiWikiWorldDbContext, Guid> Store = new(Context);
			using UserManager<User> Users = new(Store, Options.Create(new IdentityOptions()), new PasswordHasher<User>(),
				[], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!, NullLogger<UserManager<User>>.Instance);
			CreateEditModel Page = new(Context, Users, Resolver, Revisions, Storage)
			{
				PageContext = new() { HttpContext = Http, RouteData = new(), ActionDescriptor = new() }, UrlSlug = Slug, Title = "Picture", ArticleText = "New picture",
				OriginalUrlSlug = EntryPoint == "razor-replace" ? Slug : string.Empty, SelectedType = ArticleType.File, UploadedFile = Upload
			};
			Page.OnPageHandlerExecuting(new(Page.PageContext, [], new HandlerMethodDescriptor(), new Dictionary<string, object?>(), Page));
			Result = await Page.OnPostAsync(default);
			if (FailFinalization)
			{
				Assert.IsType<PageResult>(Result);
				Assert.Contains("recovery", Page.ErrorMessage, StringComparison.OrdinalIgnoreCase);
			}
			else
			{
				Assert.IsType<RedirectResult>(Result);
			}
		}
		else if (EntryPoint == "api-create")
		{
			ArticleApiController Controller = new(Context, Resolver, Revisions, Storage) { ControllerContext = new() { HttpContext = Http } };
			Result = await Controller.CreateArticleRevision(Slug, "Picture", null, ArticleType.File, "Picture", "Created", null, Upload, default);
			Assert.IsType<OkObjectResult>(Result);
			if (FailFinalization)
			{
				Assert.Contains("recovery", ((OkObjectResult)Result).Value?.ToString(), StringComparison.OrdinalIgnoreCase);
			}
		}
		else
		{
			FileApiController Controller = new(Context, Resolver, Storage) { ControllerContext = new() { HttpContext = Http } };
			Result = await Controller.UploadFile(Slug, Upload, null, null, null, default);
			if (FailFinalization)
			{
				Assert.Equal(StatusCodes.Status202Accepted, Assert.IsType<ObjectResult>(Result).StatusCode);
			}
			else
			{
				Assert.IsType<OkObjectResult>(Result);
			}
		}
		FileRevision Current = await Context.FileRevisions.AsNoTracking().SingleAsync(r => r.IsCurrent == true);
		Assert.NotNull(Current.StagingUploadId);
		Assert.DoesNotContain("StagingUploadId", JsonSerializer.Serialize(Current));
		string FinalPath = Storage.GetFinalFilePath(SiteId, Current.CanonicalFileId, Current.Id, Current.Filename);
		if (FailFinalization)
		{
			Assert.Contains(Directory.EnumerateFiles(StorageRoot, "*.tmp", SearchOption.AllDirectories), Path => Path.Contains(Current.StagingUploadId.Value.ToString("N"), StringComparison.Ordinal));
			Directory.Delete(FinalPath);
		}
		// A fresh context models the process restart; it cannot use the request's tracked entities or captured upload.
		using WikiWikiWorldDbContext RestartedContext = CreateContext();
		await CreateStorage(RestartedContext).FinalizeCommittedUploadsAndDeleteStaleTempFilesAsync(default);
		Assert.Equal(Expected, await File.ReadAllBytesAsync(FinalPath));
		Assert.Empty(Directory.EnumerateFiles(StorageRoot, "*.tmp", SearchOption.AllDirectories));
	}

	/// <summary>Creates a separate unit of work sharing only this test's in-memory SQLite connection.</summary>
	/// <returns>An unrestricted maintenance context disposed by its caller.</returns>
	private WikiWikiWorldDbContext CreateContext() => new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);

	/// <summary>Points the production storage service at this test's disposable directory.</summary>
	/// <param name="Context">The current unit of work.</param>
	/// <returns>A storage service with the normal one-hour staging retention.</returns>
	private FileUploadStorageService CreateStorage(WikiWikiWorldDbContext Context) =>
		new(Context, Options.Create(new FileStorageOptions { SiteFilesPath = StorageRoot }));

	/// <summary>Stages distinct SVG images through the real upload service.</summary>
	/// <param name="CanonicalFileId">The shared file identity.</param>
	/// <param name="Label">Text distinguishing the bytes from other attempts.</param>
	/// <returns>The service's staged upload metadata.</returns>
	private async Task<StagedFileUpload> StageAsync(Guid CanonicalFileId, string Label)
	{
		byte[] Bytes = Encoding.UTF8.GetBytes($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><title>{Label}</title></svg>");
		using MemoryStream Input = new(Bytes);
		FormFile Upload = new(Input, 0, Bytes.Length, "File", "picture.svg") { Headers = new HeaderDictionary(), ContentType = "image/svg+xml" };
		using WikiWikiWorldDbContext Context = CreateContext();
		return await CreateStorage(Context).StageUploadedFileAsync(Upload, SiteId, CanonicalFileId, default);
	}

	/// <summary>Commits metadata and a site association, deliberately leaving physical finalization for recovery.</summary>
	/// <param name="Upload">The staged bytes claimed by this revision.</param>
	/// <param name="IsCurrent">Whether this revision is current or historical.</param>
	/// <returns>The persisted revision with its generated identifier.</returns>
	private async Task<FileRevision> CommitAsync(StagedFileUpload Upload, bool IsCurrent)
	{
		using WikiWikiWorldDbContext Context = CreateContext();
		FileRevision Revision = CreateRevision(Upload, IsCurrent);
		Context.FileRevisions.Add(Revision);
		if (!await Context.ArticleRevisions.AnyAsync(r => r.CanonicalFileId == Upload.CanonicalFileId))
		{
			Context.ArticleRevisions.Add(new()
			{
				CanonicalArticleId = Guid.NewGuid(), CanonicalFileId = Upload.CanonicalFileId, SiteId = SiteId, Culture = "en",
				Title = "Picture", UrlSlug = $"picture-{Upload.CanonicalFileId:N}", Type = ArticleType.File, Text = "Picture",
				RevisionReason = "Test article", CreatedByUserId = UserId, DateCreated = DateTimeOffset.UtcNow, IsCurrent = true
			});
		}
		await Context.SaveChangesAsync();
		return Revision;
	}

	/// <summary>Creates realistic metadata with the staging claim without deciding when its transaction commits.</summary>
	/// <param name="Upload">The exact bytes claimed by this revision.</param>
	/// <param name="IsCurrent">Whether this revision is active or historical.</param>
	/// <returns>A new entity ready to add in the caller's transaction.</returns>
	private FileRevision CreateRevision(StagedFileUpload Upload, bool IsCurrent) => new()
	{
		CanonicalFileId = Upload.CanonicalFileId, StagingUploadId = Upload.StagingUploadId, IsCurrent = IsCurrent, Filename = Upload.OriginalFileName,
		MimeType = Upload.ContentType, FileSizeBytes = Upload.FileSizeBytes, Type = FileType.Image2D,
		RevisionReason = "Test upload", SourceAndRevisionReasonCulture = "en", CreatedByUserId = UserId, DateCreated = DateTimeOffset.UtcNow
	};

	/// <summary>Supplies four bytes successfully before simulating a failure in the selected file's read stream.</summary>
	private sealed class PartialFailureStream() : MemoryStream(new byte[10])
	{
		/// <inheritdoc/>
		public override ValueTask<int> ReadAsync(Memory<byte> Buffer, CancellationToken CancellationToken = default)
		{
			if (Position > 0)
			{
				throw new IOException("Selected file became unreadable.");
			}
			return base.ReadAsync(Buffer[..Math.Min(4, Buffer.Length)], CancellationToken);
		}
	}
}
