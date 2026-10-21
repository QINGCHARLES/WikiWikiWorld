using Microsoft.AspNetCore.Http;
using WikiWikiWorld.Web;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Protects fragment responses from document wrappers that would replace the entire page during morphing.
/// </summary>
public sealed class HtmlPrettifyMiddlewareTests
{
	/// <summary>
	/// Ensures enhanced responses retain exactly the fragment and attributes supplied by Razor.
	/// </summary>
	/// <returns>A task completing after the middleware response is checked.</returns>
	[Fact]
	public async Task DatastarFragmentIsPreservedVerbatim()
	{
		const string Fragment = "<span id=\"article-bookmark-status\" data-message=\"\">Saved</span>";
		DefaultHttpContext Context = new();
		Context.Request.Headers["Datastar-Request"] = "true";
		using MemoryStream ResponseStream = new();
		Context.Response.Body = ResponseStream;
		HtmlPrettifyMiddleware Middleware = new(async HttpContext =>
		{
			HttpContext.Response.ContentType = "text/html; charset=utf-8";
			await HttpContext.Response.WriteAsync(Fragment);
		});

		await Middleware.Invoke(Context);

		ResponseStream.Position = 0;
		using StreamReader Reader = new(ResponseStream);
		Assert.Equal(Fragment, await Reader.ReadToEndAsync());
	}
}
