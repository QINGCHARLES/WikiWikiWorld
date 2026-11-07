namespace WikiWikiWorld.Data.Models;

/// <summary>Grants an account access to one private conversation; the sender and every recipient have a row.</summary>
public sealed class PrivateConversationParticipant
{
	/// <summary>Gets or sets the conversation to which this membership grants access.</summary>
	public Guid ConversationId { get; set; }
	/// <summary>Gets or sets the account permitted to read and reply.</summary>
	public Guid UserId { get; set; }
	/// <summary>Gets or sets the parent used to enforce site and culture query scope.</summary>
	public PrivateConversation Conversation { get; set; } = null!;
}
