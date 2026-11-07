namespace WikiWikiWorld.Data.Models;

/// <summary>A private conversation whose recipients are fixed at creation and independent of article revisions.</summary>
public sealed class PrivateConversation
{
	/// <summary>Gets or sets the stable identifier used by private conversation routes.</summary>
	public Guid Id { get; set; }
	/// <summary>Gets or sets the site whose members' inboxes contain this conversation.</summary>
	public int SiteId { get; set; }
	/// <summary>Gets or sets the content culture in which this conversation was started.</summary>
	public required string Culture { get; set; }
	/// <summary>Gets or sets the plain-text subject visible only to participants.</summary>
	public required string Subject { get; set; }
	/// <summary>Gets or sets the initiating account, which is also a participant.</summary>
	public Guid CreatedByUserId { get; set; }
	/// <summary>Gets or sets the UTC creation instant.</summary>
	public DateTimeOffset DateCreated { get; set; }
	/// <summary>Gets the membership rows that authorize reads and replies.</summary>
	public List<PrivateConversationParticipant> Participants { get; } = [];
	/// <summary>Gets the chronological messages belonging exclusively to this conversation.</summary>
	public List<PrivateMessage> Messages { get; } = [];
}
