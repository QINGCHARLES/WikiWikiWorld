using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Data.Extensions;

public static class SpecificationExtensions
{
    public static IQueryable<T> WithSpecification<T>(this IQueryable<T> query, ISpecification<T> specification) where T : class
    {
        return SpecificationEvaluator.Default.GetQuery(query, specification);
    }
}
