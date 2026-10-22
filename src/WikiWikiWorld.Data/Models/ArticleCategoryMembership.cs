namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Records a revision's membership in a category independently of category display names or page existence.
/// </summary>
/// <remarks>
/// Memberships remain attached to historical revisions. Read through a filtered article-revision query
/// to enforce site, culture, and deletion scope, and select current revisions for current listings.
/// </remarks>
public sealed class ArticleCategoryMembership
{
	/// <summary>
	/// Gets the revision whose source declares this membership.
	/// </summary>
	public int ArticleRevisionId { get; init; }

	/// <summary>
	/// Gets the immutable category identity without the category: namespace prefix.
	/// This value is supplied as a slug, never derived from a display name by this entity.
	/// </summary>
	public required string CategorySlug { get; init; }
}
