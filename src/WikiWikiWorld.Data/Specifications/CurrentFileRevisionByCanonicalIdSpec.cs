using Ardalis.Specification;
using WikiWikiWorld.Data.Models;



namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve the current file revision by its canonical ID.
/// </summary>
public sealed class CurrentFileRevisionByCanonicalIdSpec : Specification<FileRevision>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CurrentFileRevisionByCanonicalIdSpec"/> class.
    /// </summary>
    /// <param name="CanonicalFileId">The canonical file ID.</param>
    public CurrentFileRevisionByCanonicalIdSpec(Guid CanonicalFileId)
    {
        Query.Where(x => x.CanonicalFileId == CanonicalFileId && x.IsCurrent == true);
    }
}
