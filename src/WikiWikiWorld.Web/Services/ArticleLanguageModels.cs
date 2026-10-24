namespace WikiWikiWorld.Web.Services;

/// <summary>
/// One article in a language group: an article's current revision in some culture of the current site.
/// </summary>
/// <param name="CanonicalArticleId">The article's canonical identifier, which is unique to one culture.</param>
/// <param name="Culture">The culture code the article is written in.</param>
/// <param name="Title">The article's current title.</param>
/// <param name="UrlSlug">The article's current slug, without the <c>file:</c> namespace prefix.</param>
/// <param name="Type">The article's type.</param>
public sealed record ArticleLanguageEntry(Guid CanonicalArticleId, string Culture, string Title, string UrlSlug, ArticleType Type);

/// <summary>
/// A language that both groups in a proposed merge already hold, so only one of its two articles can stay.
/// </summary>
/// <param name="Culture">The clashing culture code.</param>
/// <param name="CurrentGroupArticle">The article in the current page's group.</param>
/// <param name="OtherGroupArticle">The article in the group being merged in.</param>
/// <param name="ForcedKeepArticleId">
/// The article that must stay because it is one of the two articles being linked, or <see langword="null"/>
/// when the editor has to choose.
/// </param>
public sealed record ArticleLanguageClash(
	string Culture,
	ArticleLanguageEntry CurrentGroupArticle,
	ArticleLanguageEntry OtherGroupArticle,
	Guid? ForcedKeepArticleId);

/// <summary>
/// The kinds of outcome a language-link write can have.
/// </summary>
public enum ArticleLanguageOutcome
{
	/// <summary>The link was created, or two groups were merged.</summary>
	Linked,

	/// <summary>The two articles were already in the same group; nothing changed.</summary>
	AlreadyLinked,

	/// <summary>A language's article was replaced with another one.</summary>
	Changed,

	/// <summary>An article was taken out of the group.</summary>
	Removed,

	/// <summary>Linking would merge two groups; nothing changed until the editor confirms and resolves any clashes.</summary>
	NeedsMergeConfirmation,

	/// <summary>The request broke a rule; nothing changed. <see cref="ArticleLanguageResult.Message"/> says why.</summary>
	Refused
}

/// <summary>
/// The result of a language-link write.
/// </summary>
public sealed record ArticleLanguageResult
{
	/// <summary>
	/// Gets what happened.
	/// </summary>
	public required ArticleLanguageOutcome Outcome { get; init; }

	/// <summary>
	/// Gets a reader-facing message describing the outcome or the refusal reason.
	/// </summary>
	public string Message { get; init; } = string.Empty;

	/// <summary>
	/// Gets whether a merge confirmation joins two existing groups, rather than adding a lone article that displaces
	/// an article of the same language.
	/// </summary>
	public bool JoinsTwoGroups { get; init; }

	/// <summary>
	/// Gets every article the merged group would hold besides the current page, for a merge confirmation.
	/// Articles that would be dropped by a clash are included; <see cref="Clashes"/> identifies them.
	/// </summary>
	public IReadOnlyList<ArticleLanguageEntry> MergeMembers { get; init; } = [];

	/// <summary>
	/// Gets the languages both groups hold, for a merge confirmation.
	/// </summary>
	public IReadOnlyList<ArticleLanguageClash> Clashes { get; init; } = [];

	/// <summary>
	/// Creates a refusal with the given reason.
	/// </summary>
	/// <param name="Message">Why the request was refused.</param>
	/// <returns>A <see cref="ArticleLanguageOutcome.Refused"/> result.</returns>
	public static ArticleLanguageResult Refused(string Message) => new() { Outcome = ArticleLanguageOutcome.Refused, Message = Message };
}
