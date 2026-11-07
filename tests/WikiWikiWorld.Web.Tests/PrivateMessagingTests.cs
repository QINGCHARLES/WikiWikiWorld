using System.Security.Claims;
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
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Pages;
using WikiWikiWorld.Web.Pages.Profile;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Exercises private-message authorization, atomic writes and paging against real SQLite and Razor handlers.</summary>
public sealed class PrivateMessagingTests : IDisposable
{
	private readonly SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
	private readonly WikiWikiWorldDbContext Context;
	private readonly UserManager<User> Users;
	private readonly PrivateMessagingService Messaging;
	private readonly User Sender = new() { Id = Guid.NewGuid(), UserName = "Sender", NormalizedUserName = "SENDER" };
	private readonly User First = new() { Id = Guid.NewGuid(), UserName = "First", NormalizedUserName = "FIRST" };
	private readonly User Second = new() { Id = Guid.NewGuid(), UserName = "Second", NormalizedUserName = "SECOND" };
	private readonly User Outsider = new() { Id = Guid.NewGuid(), UserName = "Outsider", NormalizedUserName = "OUTSIDER" };
	private readonly IOptions<SiteConfiguration> Configuration = Options.Create(new SiteConfiguration
	{
		Sites = [new() { SiteId = 1, Domains = ["example.test"], DefaultCulture = "en" }]
	});

	/// <summary>Seeds four synthetic accounts in an isolated EF-created database.</summary>
	public PrivateMessagingTests()
	{
		Connection.Open();
		Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		Context.Database.EnsureCreated();
		Context.Users.AddRange(Sender, First, Second, Outsider);
		Context.SaveChanges();
		Users = new(new UserStore<User, Role, WikiWikiWorldDbContext, Guid>(Context), Options.Create(new IdentityOptions()),
			new PasswordHasher<User>(), [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!, NullLogger<UserManager<User>>.Instance);
		Messaging = new(Context, Users);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Users.Dispose();
		Context.Dispose();
		Connection.Dispose();
	}

	/// <summary>Case-normalized, repeated recipients yield fixed membership and no public content or activity.</summary>
	/// <returns>Completion after membership, inbox and public-profile isolation assertions.</returns>
	[Fact]
	public async Task MultiRecipientCreationIsPrivateAndDeduplicated()
	{
		Guid Id = await StartAsync("@first, SECOND, First, Sender");
		Assert.Equal(3, await Context.PrivateConversationParticipants.CountAsync());
		Assert.Single(await Context.PrivateMessages.ToListAsync());
		Assert.Empty(Context.ArticleRevisions);
		Assert.Empty(Context.ArticleTalkSubjects);
		Assert.Empty(Context.ArticleTalkSubjectPosts);
		foreach (User Member in new[] { Sender, First, Second })
		{
			PrivateConversationThread Thread = Assert.IsType<PrivateConversationThread>(await Messaging.GetThreadAsync(1, "en", Member.Id, Id, null, default));
			Assert.Equal("Private subject", Thread.Subject);
			Assert.Equal("Secret body", Assert.Single(Thread.Messages).Text);
			Assert.Equal(3, Thread.Participants.Count);
		}
		Assert.Single((await Messaging.GetFolderAsync(1, "en", Sender.Id, true, 1, default)).Items);
		Assert.Empty((await Messaging.GetFolderAsync(1, "en", Sender.Id, false, 1, default)).Items);
		Assert.Single((await Messaging.GetFolderAsync(1, "en", First.Id, false, 1, default)).Items);
		ProfileService Profiles = new(Context, Users, new MarkdownPipelineFactory(Configuration));
		ProfileHeader Header = (await Profiles.GetHeaderAsync(Sender.UserName!, new(new ClaimsIdentity()), 1, default))!;
		Assert.Equal(0, Header.Stats.TalkPostCount);
		Assert.Null(Header.LastActive);
	}

	/// <summary>Members can reply; outsiders, unknown IDs and foreign scopes cannot read or write, even with unfiltered contexts.</summary>
	/// <returns>Completion after refusals and preserved-message assertions.</returns>
	[Fact]
	public async Task EveryReadAndReplyRequiresMembershipAndScope()
	{
		Guid Id = await StartAsync("First,Second");
		Assert.NotNull(await Messaging.ReplyAsync(1, "en", Second.Id, Id, "Member reply", default));
		foreach ((int Site, string Culture, Guid Viewer, Guid Conversation) in new[]
		{
			(1, "en", Outsider.Id, Id), (2, "en", First.Id, Id), (1, "fr", First.Id, Id),
			(1, "en", First.Id, Guid.NewGuid()), (1, "en", Guid.NewGuid(), Id)
		})
		{
			Assert.Null(await Messaging.GetThreadAsync(Site, Culture, Viewer, Conversation, null, default));
			Assert.Null(await Messaging.ReplyAsync(Site, Culture, Viewer, Conversation, "Forbidden reply", default));
		}
		Assert.Empty((await Messaging.GetFolderAsync(1, "en", Outsider.Id, false, 1, default)).Items);
		Assert.Empty((await Messaging.GetFolderAsync(2, "en", First.Id, false, 1, default)).Items);
		Assert.Empty((await Messaging.GetFolderAsync(1, "fr", First.Id, false, 1, default)).Items);
		Assert.Equal(2, await Context.PrivateMessages.CountAsync());
	}

	/// <summary>Invalid recipients or fields fail the whole creation without creating articles, membership or messages.</summary>
	/// <param name="Recipients">Submitted recipient list.</param>
	/// <param name="Subject">Submitted title.</param>
	/// <param name="Text">Submitted first body.</param>
	/// <returns>Completion after the rejected write is shown to leave no private or public rows.</returns>
	[Theory]
	[InlineData("First,Missing", "Title", "Body")]
	[InlineData("Sender", "Title", "Body")]
	[InlineData(", ,", "Title", "Body")]
	[InlineData("First", " ", "Body")]
	[InlineData("First", "Title", " ")]
	public async Task InvalidCreationIsAtomic(string Recipients, string Subject, string Text)
	{
		await Assert.ThrowsAnyAsync<ArgumentException>(() => Messaging.CreateAsync(1, "en", Sender.Id, Recipients, Subject, Text, default));
		Assert.Empty(Context.PrivateConversations);
		Assert.Empty(Context.PrivateConversationParticipants);
		Assert.Empty(Context.PrivateMessages);
		Assert.Empty(Context.ArticleRevisions);
	}

	/// <summary>Length/recipient bounds and cancellation also apply to callers bypassing form validation.</summary>
	/// <returns>Completion after no invalid or canceled content has been saved.</returns>
	[Fact]
	public async Task ServiceLimitsAndCancellationPreventPartialWrites()
	{
		await Assert.ThrowsAnyAsync<ArgumentException>(() => StartAsync(string.Join(',', Enumerable.Repeat("First", 21))));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => Messaging.CreateAsync(1, "en", Sender.Id, "First", new('x', 201), "Body", default));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => Messaging.CreateAsync(1, "en", Sender.Id, "First", "Title", new('x', 10001), default));
		using CancellationTokenSource Cancellation = new();
		Cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Messaging.CreateAsync(1, "en", Sender.Id, "First", "Title", "Body", Cancellation.Token));
		Assert.Empty(Context.PrivateConversations);
	}

	/// <summary>A failed body insertion rolls back the conversation graph and detaches its failed entities.</summary>
	/// <returns>Completion after rollback and a successful save using the same context.</returns>
	[Fact]
	public async Task DatabaseFailureRollsBackConversationMembershipAndBody()
	{
		using SqliteCommand FailureTrigger = Connection.CreateCommand();
		FailureTrigger.CommandText = "CREATE TRIGGER FailPrivateBody BEFORE INSERT ON PrivateMessages BEGIN SELECT RAISE(ABORT, 'Injected body failure'); END;";
		FailureTrigger.ExecuteNonQuery();
		await Assert.ThrowsAsync<DbUpdateException>(() => StartAsync("First,Second"));
		Assert.Empty(Context.PrivateConversations);
		Assert.Empty(Context.PrivateConversationParticipants);
		Assert.Empty(Context.PrivateMessages);
		Assert.DoesNotContain(Context.ChangeTracker.Entries(), e => e.Entity is PrivateConversation or PrivateConversationParticipant or PrivateMessage);
		FailureTrigger.CommandText = "DROP TRIGGER FailPrivateBody;";
		FailureTrigger.ExecuteNonQuery();
		await StartAsync("First,Second");
		Assert.Equal(3, await Context.PrivateConversationParticipants.CountAsync());
	}

	/// <summary>Fifty-one messages produce deterministic bounded chronological pages and open on the latest page.</summary>
	/// <returns>Completion after paging and post-redirect anchor targets are verified.</returns>
	[Fact]
	public async Task ThreadPagesAreBoundedAndDefaultToLatest()
	{
		Guid Id = await StartAsync("First");
		int? LastId = null;
		for (int i = 0; i < 50; i++) LastId = await Messaging.ReplyAsync(1, "en", First.Id, Id, $"Reply {i}", default);
		PrivateConversationThread Latest = (await Messaging.GetThreadAsync(1, "en", Sender.Id, Id, null, default))!;
		Assert.Equal(2, Latest.PageNumber);
		Assert.Equal(2, Latest.PageCount);
		Assert.Equal(LastId, Assert.Single(Latest.Messages).Id);
		PrivateConversationThread FirstPage = (await Messaging.GetThreadAsync(1, "en", Sender.Id, Id, 0, default))!;
		Assert.Equal(50, FirstPage.Messages.Count);
		Assert.True(FirstPage.Messages.SequenceEqual(FirstPage.Messages.OrderBy(m => m.Id)));
	}

	/// <summary>Fifty-one conversations are bounded, with the latest reply moving its conversation to the front.</summary>
	/// <returns>Completion after SQL folder ordering and clamped pagination are verified.</returns>
	[Fact]
	public async Task FoldersPageByLatestMessage()
	{
		Guid FirstId = await StartAsync("First");
		for (int i = 0; i < 50; i++) await StartAsync("First");
		await Messaging.ReplyAsync(1, "en", First.Id, FirstId, "Most recent", default);
		PrivateConversationFolder Page = await Messaging.GetFolderAsync(1, "en", First.Id, false, 1, default);
		Assert.Equal(50, Page.Items.Count);
		Assert.Equal(FirstId, Page.Items[0].Id);
		PrivateConversationFolder Last = await Messaging.GetFolderAsync(1, "en", First.Id, false, int.MaxValue, default);
		Assert.Equal(2, Last.PageNumber);
		Assert.Single(Last.Items);
	}

	/// <summary>Direct Razor GET and POST calls enforce authentication and the same nonmember refusal, including invalid forms.</summary>
	/// <returns>Completion after handlers are shown to preserve drafts and privacy.</returns>
	[Fact]
	public async Task ConversationHandlersRefuseOutsidersAndKeepMemberDrafts()
	{
		Guid Id = await StartAsync("First");
		ConversationModel Foreign = CreateConversation(Outsider, Id);
		Assert.IsType<NotFoundResult>(await Foreign.OnGetAsync(default));
		Foreign.Input.Text = "No access";
		Assert.IsType<NotFoundResult>(await Foreign.OnPostAsync(default));
		Foreign.ModelState.AddModelError("Input.Text", "Invalid");
		Assert.IsType<NotFoundResult>(await Foreign.OnPostAsync(default));
		Assert.Null(Foreign.Thread);
		ConversationModel Anonymous = CreateConversation(null, Id);
		Assert.IsType<ChallengeResult>(await Anonymous.OnGetAsync(default));
		Assert.IsType<ChallengeResult>(await Anonymous.OnPostAsync(default));
		ConversationModel Member = CreateConversation(First, Id);
		Member.Input.Text = "Keep draft";
		Member.ModelState.AddModelError("Input.Text", "Invalid");
		Assert.IsType<PageResult>(await Member.OnPostAsync(default));
		Assert.Equal("Keep draft", Member.Input.Text);
		Assert.False(Member.AllowSearchEngineIndexingOfPage);
		Member.ModelState.Clear();
		LocalRedirectResult Redirect = Assert.IsType<LocalRedirectResult>(await Member.OnPostAsync(default));
		Assert.StartsWith($"/message/{Id:D}#message-", Redirect.Url);
		Assert.Equal(2, await Context.PrivateMessages.CountAsync());
	}

	/// <summary>Profile prefill remains editable and POST never infers a sender or recipient from the URL.</summary>
	/// <returns>Completion after multi-recipient form, unknown-account and anonymous-search scenarios.</returns>
	[Fact]
	public async Task ComposeUsesSubmittedRecipientsAndRetainsUnknownRecipientDraft()
	{
		DefaultHttpContext Http = CreateHttp(Sender);
		NewMessageModel Page = new(Messaging, Users, Context, new(Configuration, new HttpContextAccessor { HttpContext = Http })) { Recipient = "First" };
		InitializePage(Page, Http);
		Assert.IsType<PageResult>(await Page.OnGetAsync(default));
		Assert.Equal("First", Page.Input.Recipients);
		Assert.Empty(Context.ArticleRevisions);
		Page.Input = new() { Recipients = "Missing", Subject = "Draft title", Message = "Draft body" };
		Assert.IsType<PageResult>(await Page.OnPostAsync(default));
		Assert.Equal("Draft body", Page.Input.Message);
		Assert.NotNull(Page.ErrorMessage);
		Page.Input.Recipients = "Second";
		Assert.IsType<LocalRedirectResult>(await Page.OnPostAsync(default));
		Assert.False(await Context.PrivateConversationParticipants.AnyAsync(p => p.UserId == First.Id));
		Assert.True(await Context.PrivateConversationParticipants.AnyAsync(p => p.UserId == Second.Id));
		Http.User = new(new ClaimsIdentity());
		Assert.IsType<ChallengeResult>(await Page.OnGetSearchUsersAsync("Fi", default));
		Assert.IsType<ChallengeResult>(await Page.OnPostAsync(default));
	}

	/// <summary>The private folder handler checks the viewer owns the profile before rendering its contents.</summary>
	/// <returns>Completion after the owner's folder succeeds while foreign and anonymous requests are refused.</returns>
	[Fact]
	public async Task InboxHandlerRequiresTheProfileOwner()
	{
		await StartAsync("First");
		DefaultHttpContext Http = CreateHttp(Sender);
		MessagesModel Page = new(Messaging, new ProfileService(Context, Users, new MarkdownPipelineFactory(Configuration)),
			new(Configuration, new HttpContextAccessor { HttpContext = Http })) { Username = Sender.UserName!, Tab = "sent" };
		InitializePage(Page, Http);
		Assert.IsType<PageResult>(await Page.OnGetAsync(default));
		Assert.Single(Page.Folder.Items);
		Http.User = CreateHttp(Outsider).User;
		Assert.IsType<ForbidResult>(await Page.OnGetAsync(default));
		Http.User = new(new ClaimsIdentity());
		Assert.IsType<ChallengeResult>(await Page.OnGetAsync(default));
	}

	/// <summary>All private page responses disallow cache storage, including search and error responses through Razor filters.</summary>
	/// <param name="PageType">The private page model.</param>
	[Theory]
	[InlineData(typeof(NewMessageModel))]
	[InlineData(typeof(MessagesModel))]
	[InlineData(typeof(ConversationModel))]
	public void PrivatePagesDisableCaching(Type PageType)
	{
		ResponseCacheAttribute Attribute = Assert.Single(PageType.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());
		Assert.True(Attribute.NoStore);
		Assert.Equal(ResponseCacheLocation.None, Attribute.Location);
	}

	/// <summary>Starts one fixture conversation through the real service.</summary>
	/// <param name="Recipients">Fixture account names.</param>
	/// <returns>The committed conversation ID.</returns>
	private Task<Guid> StartAsync(string Recipients) => Messaging.CreateAsync(1, "en", Sender.Id, Recipients, "Private subject", "Secret body", default);

	/// <summary>Initializes the conversation handler using the real site-resolution filter.</summary>
	/// <param name="Viewer">Authenticated account, or null for anonymous.</param>
	/// <param name="Id">Conversation route selector.</param>
	/// <returns>The initialized page model.</returns>
	private ConversationModel CreateConversation(User? Viewer, Guid Id)
	{
		DefaultHttpContext Http = CreateHttp(Viewer);
		ConversationModel Page = new(Messaging, Users, new(Configuration, new HttpContextAccessor { HttpContext = Http })) { ConversationId = Id };
		InitializePage(Page, Http);
		return Page;
	}

	/// <summary>Creates a synthetic authenticated request without real credentials.</summary>
	/// <param name="Viewer">Fixture account, or null.</param>
	/// <returns>The request over the configured site.</returns>
	private static DefaultHttpContext CreateHttp(User? Viewer)
	{
		DefaultHttpContext Http = new();
		Http.Request.Host = new("en.example.test");
		if (Viewer is not null) Http.User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, Viewer.Id.ToString()), new(ClaimTypes.Name, Viewer.UserName!)], "Test"));
		return Http;
	}

	/// <summary>Runs the shared Razor scope initialization before a direct handler call.</summary>
	/// <param name="Page">Page under test.</param>
	/// <param name="Http">Synthetic request.</param>
	private static void InitializePage(BasePageModel Page, DefaultHttpContext Http)
	{
		Page.PageContext = new() { HttpContext = Http, RouteData = new(), ActionDescriptor = new() };
		Page.OnPageHandlerExecuting(new(Page.PageContext, [], new HandlerMethodDescriptor(), new Dictionary<string, object?>(), Page));
	}
}
