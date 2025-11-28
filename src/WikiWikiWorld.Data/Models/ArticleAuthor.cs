namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Represents an author of an article.
/// </summary>
/// <param name="UserName">The username of the author.</param>
/// <param name="ProfilePicGuid">The GUID of the author's profile picture, if any.</param>
public record ArticleAuthor(string UserName, string? ProfilePicGuid);
