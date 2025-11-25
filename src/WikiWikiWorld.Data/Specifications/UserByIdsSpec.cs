using Ardalis.Specification;
using Ardalis.Specification;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Data.Specifications;

/// <summary>
/// Specification to retrieve users by a list of IDs.
/// </summary>
public sealed class UserByIdsSpec : Specification<User>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UserByIdsSpec"/> class.
    /// </summary>
    /// <param name="Ids">The list of user IDs.</param>
    public UserByIdsSpec(IEnumerable<Guid> Ids)
    {
        Query.AsNoTracking()
             .Where(u => Ids.Contains(u.Id));
    }
}
