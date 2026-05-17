using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// View-model passed to the recursive <c>_TalkSubjectPost</c> partial.
/// Carries the post node, current indent depth, the per-thread author lookup and the parent slugs
/// (needed so the reply form can route back to <see cref="TalkSubjectModel"/>).
/// </summary>
public sealed class _TalkSubjectPostContext
{
	/// <summary>
	/// Gets the post node to render.
	/// </summary>
	public required TalkSubjectModel.PostNode Node { get; init; }

	/// <summary>
	/// Gets the current indent depth (0 for top-level posts).
	/// </summary>
	public required int Depth { get; init; }

	/// <summary>
	/// Gets the author lookup shared across the thread.
	/// </summary>
	public required IReadOnlyDictionary<Guid, User> Authors { get; init; }

	/// <summary>
	/// Gets the parent article slug, used for reply form routing.
	/// </summary>
	public required string ArticleSlug { get; init; }

	/// <summary>
	/// Gets the parent subject slug, used for reply form routing.
	/// </summary>
	public required string SubjectSlug { get; init; }

	/// <summary>
	/// Creates a child context at <see cref="Depth"/> + 1, reusing the shared author lookup and slugs.
	/// </summary>
	/// <param name="Child">The child post node.</param>
	/// <returns>A new context for the child node.</returns>
	public _TalkSubjectPostContext ForChild(TalkSubjectModel.PostNode Child)
	{
		return new _TalkSubjectPostContext
		{
			Node = Child,
			Depth = Depth + 1,
			Authors = Authors,
			ArticleSlug = ArticleSlug,
			SubjectSlug = SubjectSlug,
		};
	}
}
