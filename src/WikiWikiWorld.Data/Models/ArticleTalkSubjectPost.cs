namespace WikiWikiWorld.Data.Models;

public sealed record ArticleTalkSubjectPost
{
    public Guid Id { get; init; }
    public Guid ArticleTalkSubjectId { get; init; }
    public Guid? ParentTalkSubjectPostId { get; init; }
    public required string Text { get; init; }
    public bool HasBeenEdited { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset DateCreated { get; init; }
    public DateTimeOffset? DateModified { get; init; }
    public DateTimeOffset? DateDeleted { get; init; }
}
