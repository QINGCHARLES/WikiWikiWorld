using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Data.Extensions;

/// <summary>
/// Extension methods for specifications.
/// </summary>
public static class SpecificationExtensions
{
    /// <summary>
    /// Applies a specification to a queryable.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="query">The queryable to apply the specification to.</param>
    /// <param name="specification">The specification to apply.</param>
    /// <returns>The queryable with the specification applied.</returns>
    public static IQueryable<T> WithSpecification<T>(this IQueryable<T> query, ISpecification<T> specification) where T : class
    {
        return SpecificationEvaluator.Default.GetQuery(query, specification);
    }
}
