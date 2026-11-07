namespace WikiWikiWorld.Web.Services;

/// <summary>Identifies one participant for a participant-only conversation header.</summary>
/// <param name="UserId">The stable membership account.</param>
/// <param name="UserName">The current name, or a placeholder for an unavailable account.</param>
public sealed record class PrivateParticipant(Guid UserId, string UserName);

/// <summary>Contains the authorized message body and attribution without exposing a tracked entity.</summary>
/// <param name="Id">The chronological message anchor.</param>
/// <param name="AuthorName">Current author name, or an unavailable-account placeholder.</param>
/// <param name="Text">Markdown body for the minimal talk renderer.</param>
/// <param name="DateCreated">UTC posting instant.</param>
public sealed record class PrivateMessageItem(int Id, string AuthorName, string Text, DateTimeOffset DateCreated);

/// <summary>Contains only the subject and activity metadata needed by a member's folder.</summary>
/// <param name="Id">Private conversation route identity.</param>
/// <param name="Subject">Plain-text subject.</param>
/// <param name="CreatorName">Account that started the conversation.</param>
/// <param name="LastMessageDate">UTC time of its most recent message.</param>
public sealed record class PrivateConversationItem(Guid Id, string Subject, string CreatorName, DateTimeOffset LastMessageDate);

/// <summary>A bounded member-only inbox or started-conversation folder.</summary>
/// <param name="Items">At most one page of authorized conversations.</param>
/// <param name="PageNumber">Clamped one-based page.</param>
/// <param name="PageCount">Number of pages, at least one for an empty folder.</param>
public sealed record class PrivateConversationFolder(IReadOnlyList<PrivateConversationItem> Items, int PageNumber, int PageCount);

/// <summary>A member-authorized chronological conversation page; membership is checked before bodies are loaded.</summary>
/// <param name="Id">Conversation route identity.</param>
/// <param name="Subject">Plain-text subject.</param>
/// <param name="Participants">Fixed sender/recipient membership.</param>
/// <param name="Messages">At most one page of messages.</param>
/// <param name="PageNumber">Clamped one-based message page.</param>
/// <param name="PageCount">Number of message pages, at least one.</param>
public sealed record class PrivateConversationThread(Guid Id, string Subject, IReadOnlyList<PrivateParticipant> Participants,
	IReadOnlyList<PrivateMessageItem> Messages, int PageNumber, int PageCount);
