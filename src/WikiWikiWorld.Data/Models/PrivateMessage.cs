namespace WikiWikiWorld.Data.Models;

/// <summary>A posted message with no editing workflow; author membership is enforced by a composite foreign key.</summary>
public sealed class PrivateMessage
{
	/// <summary>Gets or sets the generated identifier used for stable ordering and message anchors.</summary>
	public int Id { get; set; }
	/// <summary>Gets or sets the private conversation containing this message.</summary>
	public Guid ConversationId { get; set; }
	/// <summary>Gets or sets the participant who posted the message.</summary>
	public Guid AuthorUserId { get; set; }
	/// <summary>Gets or sets the Markdown body rendered only after membership authorization.</summary>
	public required string Text { get; set; }
	/// <summary>Gets or sets the UTC posting instant.</summary>
	public DateTimeOffset DateCreated { get; set; }
	/// <summary>Gets or sets the parent used to enforce site and culture query scope.</summary>
	public PrivateConversation Conversation { get; set; } = null!;
}
