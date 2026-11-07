using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WikiWikiWorld.Web.Infrastructure;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Ensures browser codecs reach static-file middleware instead of the wiki article endpoint.</summary>
public sealed class HeaderHelperRoutingTests
{
	/// <summary>Leaves module and WASM URLs unclaimed so static middleware can serve or reject them.</summary>
	/// <param name="Path">A codec or worker URL, without a leading slash.</param>
	/// <returns>A task completing after the route transformer has been checked.</returns>
	[Theory]
	[InlineData("js/vendor/codec.wasm")]
	[InlineData("js/vendor/codec.mjs")]
	[InlineData("js/header-helper-worker.js")]
	public async Task CodecAssetsAreNotArticleRoutes(string Path)
	{
		ArticleRouteTransformer Transformer = new();
		RouteValueDictionary Result = await Transformer.TransformAsync(new DefaultHttpContext(),
			new RouteValueDictionary { [ArticleRouteTransformer.SlugRouteValueName] = Path });
		Assert.Null(Result);
	}
}
