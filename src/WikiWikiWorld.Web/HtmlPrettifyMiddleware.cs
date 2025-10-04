using System.Text;
using AngleSharp;
using AngleSharp.Html.Parser;
using AngleSharp.Html;

namespace WikiWikiWorld.Web;

public class HtmlPrettifyMiddleware(RequestDelegate Next)
{
	public async Task Invoke(HttpContext Context)
	{
		// Capture original response stream
		Stream OriginalBodyStream = Context.Response.Body;
		using MemoryStream NewBodyStream = new();
		Context.Response.Body = NewBodyStream;

		await Next(Context); // Execute request pipeline

		// Ensure we have the full response
		NewBodyStream.Seek(0, SeekOrigin.Begin);
		using StreamReader StreamReader = new(NewBodyStream);
		string RawHtml = await StreamReader.ReadToEndAsync();

		// Check if it's an HTML response AFTER processing
		if (Context.Response.Headers.TryGetValue("Content-Type", out Microsoft.Extensions.Primitives.StringValues ContentType) &&
			ContentType.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
		{
			string FormattedHtml = await PrettifyHtmlAsync(RawHtml);

			byte[] ResponseBytes = Encoding.UTF8.GetBytes(FormattedHtml);
			Context.Response.Body = OriginalBodyStream;
			Context.Response.ContentLength = ResponseBytes.Length;
			await Context.Response.Body.WriteAsync(ResponseBytes.AsMemory(), Context.RequestAborted);
		}
		else
		{
			// If not HTML, return the original content
			NewBodyStream.Seek(0, SeekOrigin.Begin);
			await NewBodyStream.CopyToAsync(OriginalBodyStream);
		}
	}

	private static async Task<string> PrettifyHtmlAsync(string Html)
	{
		HtmlParser Parser = new();
		AngleSharp.Dom.IDocument Document = await Parser.ParseDocumentAsync(Html);
		return Document.ToHtml(new PrettyMarkupFormatter
		{
			Indentation = "\t",
			NewLine = "\n"
		});
	}
}
