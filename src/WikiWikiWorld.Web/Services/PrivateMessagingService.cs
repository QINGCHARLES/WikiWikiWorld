using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace WikiWikiWorld.Web.Services;

/// <summary>Creates and reads private conversations using explicit scope and participant checks on every operation.</summary>
/// <param name="Context">The request-scoped SQLite unit of work.</param>
/// <param name="UserManager">Resolves recipient names using Identity's configured normalization.</param>
public sealed class PrivateMessagingService(WikiWikiWorldDbContext Context, UserManager<User> UserManager)
{
	/// <summary>Maximum number of other accounts selected for a conversation at creation.</summary>
	public const int MaxRecipients = 20;
	/// <summary>Maximum subject length accepted by both the form and service.</summary>
	public const int MaxSubjectLength = 200;
	/// <summary>Maximum body length accepted by both the form and service.</summary>
	public const int MaxMessageLength = 10000;
	/// <summary>Maximum number of rows returned by a folder or thread page.</summary>
	public const int PageSize = 50;

	/// <summary>Atomically starts a conversation, includes the sender in its fixed membership, and posts its first message.</summary>
	/// <param name="SiteId">Resolved site, not a form-selected tenant.</param>
	/// <param name="Culture">Resolved content culture.</param>
	/// <param name="SenderId">Authenticated sending account.</param>
	/// <param name="Recipients">Comma-separated usernames; duplicates and an optional leading @ are normalized.</param>
	/// <param name="Subject">Plain-text conversation subject.</param>
	/// <param name="Text">Initial Markdown body.</param>
	/// <param name="CancellationToken">Cancels recipient lookup and the transaction.</param>
	/// <returns>The new conversation identifier after commit.</returns>
	/// <exception cref="ArgumentException">An account or submitted field is invalid; nothing is saved.</exception>
	public async Task<Guid> CreateAsync(int SiteId, string Culture, Guid SenderId, string Recipients, string Subject, string Text, CancellationToken CancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SiteId);
		ArgumentException.ThrowIfNullOrWhiteSpace(Culture);
		ValidateText(Subject, MaxSubjectLength, "Subject");
		ValidateText(Text, MaxMessageLength, "Message");
		ArgumentException.ThrowIfNullOrWhiteSpace(Recipients);
		if (Recipients.Length > 2000) throw new ArgumentException("Recipient list is too long.");
		if (!await Context.Users.AnyAsync(u => u.Id == SenderId, CancellationToken)) throw new ArgumentException("Sender account is unavailable.");

		string[] Names = Recipients.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (Names.Length > MaxRecipients) throw new ArgumentException($"Choose at most {MaxRecipients} recipients.");
		HashSet<Guid> ParticipantIds = [SenderId];
		foreach (string Name in Names)
		{
			CancellationToken.ThrowIfCancellationRequested();
			User? Recipient = await UserManager.FindByNameAsync(Name.TrimStart('@'));
			if (Recipient is null) throw new ArgumentException($"Recipient '{Name}' was not found.");
			ParticipantIds.Add(Recipient.Id);
		}
		if (ParticipantIds.Count < 2) throw new ArgumentException("Choose at least one other recipient.");

		Guid ConversationId = Guid.NewGuid();
		await ExecuteTransactionAsync(async Token =>
		{
			DateTimeOffset Now = DateTimeOffset.UtcNow;
			PrivateConversation Conversation = new()
			{
				Id = ConversationId, SiteId = SiteId, Culture = Culture, Subject = Subject.Trim(), CreatedByUserId = SenderId, DateCreated = Now
			};
			foreach (Guid UserId in ParticipantIds)
				Conversation.Participants.Add(new() { UserId = UserId });
			Conversation.Messages.Add(new() { AuthorUserId = SenderId, Text = Text.Trim(), DateCreated = Now });
			Context.PrivateConversations.Add(Conversation);
			await Context.SaveChangesAsync(Token);
			return true;
		}, CancellationToken);
		return ConversationId;
	}

	/// <summary>Lists only the viewer's conversations, with inbox and started folders scoped and paged in SQL.</summary>
	/// <param name="SiteId">Resolved site.</param>
	/// <param name="Culture">Resolved culture.</param>
	/// <param name="ViewerId">Authenticated member.</param>
	/// <param name="Sent">Whether to list conversations started by the viewer rather than received ones.</param>
	/// <param name="PageNumber">Requested one-based page.</param>
	/// <param name="CancellationToken">Cancels database operations.</param>
	/// <returns>A bounded folder with no message bodies.</returns>
	public async Task<PrivateConversationFolder> GetFolderAsync(int SiteId, string Culture, Guid ViewerId, bool Sent, int PageNumber, CancellationToken CancellationToken)
	{
		IQueryable<PrivateConversation> Query = AccessibleConversations(SiteId, Culture, ViewerId)
			.Where(c => Sent ? c.CreatedByUserId == ViewerId : c.CreatedByUserId != ViewerId);
		int Total = await Query.CountAsync(CancellationToken);
		int Pages = Total == 0 ? 1 : (Total - 1) / PageSize + 1;
		PageNumber = Math.Clamp(PageNumber, 1, Pages);
		var Rows = await Query.OrderByDescending(c => c.Messages.Max(m => (int?)m.Id)).ThenBy(c => c.Id)
			.Skip((PageNumber - 1) * PageSize).Take(PageSize)
			.Select(c => new { c.Id, c.Subject, c.CreatedByUserId, LastMessageDate = c.Messages.OrderByDescending(m => m.Id).Select(m => m.DateCreated).First() })
			.ToListAsync(CancellationToken);
		Dictionary<Guid, string> Names = await GetNamesAsync(Rows.Select(c => c.CreatedByUserId), CancellationToken);
		return new([.. Rows.Select(c => new PrivateConversationItem(c.Id, c.Subject, Names.GetValueOrDefault(c.CreatedByUserId, "Unavailable account"), c.LastMessageDate))], PageNumber, Pages);
	}

	/// <summary>Checks membership before loading any private body; the default page shows the latest messages.</summary>
	/// <param name="SiteId">Resolved site.</param>
	/// <param name="Culture">Resolved culture.</param>
	/// <param name="ViewerId">Authenticated reader.</param>
	/// <param name="ConversationId">Untrusted route selector.</param>
	/// <param name="PageNumber">Requested page, or null for the latest page.</param>
	/// <param name="CancellationToken">Cancels database reads.</param>
	/// <returns>The authorized page, or null for an unavailable, foreign-scope or nonmember conversation.</returns>
	public async Task<PrivateConversationThread?> GetThreadAsync(int SiteId, string Culture, Guid ViewerId, Guid ConversationId, int? PageNumber, CancellationToken CancellationToken)
	{
		var Conversation = await AccessibleConversations(SiteId, Culture, ViewerId).Where(c => c.Id == ConversationId)
			.Select(c => new { c.Id, c.Subject }).SingleOrDefaultAsync(CancellationToken);
		if (Conversation is null) return null;
		List<Guid> Members = await Context.PrivateConversationParticipants.Where(p => p.ConversationId == ConversationId).Select(p => p.UserId).ToListAsync(CancellationToken);
		Dictionary<Guid, string> Names = await GetNamesAsync(Members, CancellationToken);
		IQueryable<PrivateMessage> Query = Context.PrivateMessages.AsNoTracking().Where(m => m.ConversationId == ConversationId);
		int Total = await Query.CountAsync(CancellationToken);
		int Pages = Total == 0 ? 1 : (Total - 1) / PageSize + 1;
		int Page = Math.Clamp(PageNumber ?? Pages, 1, Pages);
		var Rows = await Query.OrderBy(m => m.Id).Skip((Page - 1) * PageSize).Take(PageSize)
			.Select(m => new { m.Id, m.AuthorUserId, m.Text, m.DateCreated }).ToListAsync(CancellationToken);
		return new(Conversation.Id, Conversation.Subject,
			[.. Members.Select(Id => new PrivateParticipant(Id, Names.GetValueOrDefault(Id, "Unavailable account"))).OrderBy(p => p.UserName, StringComparer.OrdinalIgnoreCase)],
			[.. Rows.Select(m => new PrivateMessageItem(m.Id, Names.GetValueOrDefault(m.AuthorUserId, "Unavailable account"), m.Text, m.DateCreated))], Page, Pages);
	}

	/// <summary>Posts only for a current participant in the resolved scope, with the membership checked inside the write transaction.</summary>
	/// <param name="SiteId">Resolved site.</param>
	/// <param name="Culture">Resolved culture.</param>
	/// <param name="ViewerId">Authenticated author.</param>
	/// <param name="ConversationId">Untrusted route selector.</param>
	/// <param name="Text">Reply Markdown.</param>
	/// <param name="CancellationToken">Cancels the transaction.</param>
	/// <returns>The new message anchor, or null when the viewer cannot access the conversation.</returns>
	/// <exception cref="ArgumentException">The reply is empty or exceeds the body limit.</exception>
	public Task<int?> ReplyAsync(int SiteId, string Culture, Guid ViewerId, Guid ConversationId, string Text, CancellationToken CancellationToken)
	{
		ValidateText(Text, MaxMessageLength, "Message");
		return ExecuteTransactionAsync<int?>(async Token =>
		{
			if (!await AccessibleConversations(SiteId, Culture, ViewerId).AnyAsync(c => c.Id == ConversationId, Token)) return null;
			if (!await Context.Users.AnyAsync(u => u.Id == ViewerId, Token)) return null;
			PrivateMessage Message = new() { ConversationId = ConversationId, AuthorUserId = ViewerId, Text = Text.Trim(), DateCreated = DateTimeOffset.UtcNow };
			Context.PrivateMessages.Add(Message);
			await Context.SaveChangesAsync(Token);
			return Message.Id;
		}, CancellationToken);
	}

	/// <summary>Starts every resource lookup at explicit scope and fixed participant membership, even for unfiltered contexts.</summary>
	/// <param name="SiteId">Resolved site.</param>
	/// <param name="Culture">Resolved culture.</param>
	/// <param name="ViewerId">Authenticated account.</param>
	/// <returns>A composable member-only conversation query.</returns>
	private IQueryable<PrivateConversation> AccessibleConversations(int SiteId, string Culture, Guid ViewerId) =>
		Context.PrivateConversations.AsNoTracking().Where(c => c.SiteId == SiteId && c.Culture == Culture && c.Participants.Any(p => p.UserId == ViewerId));

	/// <summary>Resolves public account names only after the owning conversation has been authorized.</summary>
	/// <param name="UserIds">Distinct participants or folder creators to display.</param>
	/// <param name="CancellationToken">Cancels account lookup.</param>
	/// <returns>Names of accounts still available through the normal user filters.</returns>
	private Task<Dictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> UserIds, CancellationToken CancellationToken)
	{
		Guid[] Ids = [.. UserIds.Distinct()];
		return Context.Users.AsNoTracking().Where(u => Ids.Contains(u.Id) && u.UserName != null).ToDictionaryAsync(u => u.Id, u => u.UserName!, CancellationToken);
	}

	/// <summary>Rejects invalid text at the service boundary, including callers that bypass form validation.</summary>
	/// <param name="Text">Submitted field.</param>
	/// <param name="Limit">Maximum character count.</param>
	/// <param name="Label">User-facing field name.</param>
	/// <exception cref="ArgumentException">The field is blank or too long.</exception>
	private static void ValidateText(string Text, int Limit, string Label)
	{
		if (string.IsNullOrWhiteSpace(Text) || Text.Length > Limit) throw new ArgumentException($"{Label} must contain 1–{Limit} characters.");
	}

	/// <summary>Commits one private-message operation; detaches rolled-back private entities before SQLite lock retries.</summary>
	/// <typeparam name="TResult">Committed operation result.</typeparam>
	/// <param name="Operation">Work sharing this context inside its transaction.</param>
	/// <param name="CancellationToken">Cancels transaction acquisition or retries.</param>
	/// <returns>The committed result, propagating cancellation and database failures.</returns>
	private Task<TResult> ExecuteTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> Operation, CancellationToken CancellationToken) =>
		Context.Database.CreateExecutionStrategy().ExecuteAsync(async Token =>
		{
			await using IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync(Token);
			try
			{
				TResult Result = await Operation(Token);
				await Transaction.CommitAsync(Token);
				return Result;
			}
			catch
			{
				foreach (EntityEntry Entry in Context.ChangeTracker.Entries().ToArray())
					if (Entry.Entity is PrivateConversation or PrivateConversationParticipant or PrivateMessage) Entry.State = EntityState.Detached;
				throw;
			}
		}, CancellationToken);
}
