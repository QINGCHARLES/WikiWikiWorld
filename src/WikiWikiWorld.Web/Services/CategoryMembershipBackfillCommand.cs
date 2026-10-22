using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Runs explicit database maintenance without starting the website or creating a missing database.
/// </summary>
public static class CategoryMembershipBackfillCommand
{
	/// <summary>
	/// Reports category changes by default, or commits them when the operator supplies --apply.
	/// </summary>
	/// <param name="Arguments">An existing database path, optionally followed by --apply.</param>
	/// <returns>Zero on success, two for usage errors, one on failure, or 130 on cancellation.</returns>
	public static async Task<int> RunAsync(string[] Arguments)
	{
		if (Arguments.Length is < 1 or > 2 || (Arguments.Length == 2 && Arguments[1] != "--apply"))
		{
			Console.Error.WriteLine("Usage: backfill-categories <database-path> [--apply]");
			return 2;
		}
		using CancellationTokenSource Cancellation = new();
		ConsoleCancelEventHandler CancelHandler = (_, Event) => { Event.Cancel = true; Cancellation.Cancel(); };
		Console.CancelKeyPress += CancelHandler;
		try
		{
			bool Apply = Arguments.Length == 2;
			string DatabasePath = Path.GetFullPath(Arguments[0]);
			Console.WriteLine($"{(Apply ? "Applying" : "Dry run")}: {DatabasePath}");
			SqliteConnectionStringBuilder ConnectionString = new()
			{
				DataSource = DatabasePath, Mode = Apply ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly,
				ForeignKeys = true, DefaultTimeout = 30
			};
			await using SqliteConnection Connection = new(ConnectionString.ToString());
			await Connection.OpenAsync(Cancellation.Token);
			await using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>()
				.UseSqlite(Connection).Options);
			// Site configuration affects external-link rendering only, not category extraction.
			MarkdownPipelineFactory Pipeline = new(Options.Create(new SiteConfiguration()));
			CategoryMembershipBackfill Backfill = new(Context, Pipeline);
			await Backfill.RunAsync(Apply, Console.Out, Cancellation.Token);
			return 0;
		}
		catch (OperationCanceledException)
		{
			Console.Error.WriteLine("Cancelled. Completed batches remain committed; rerun to finish.");
			return 130;
		}
		catch (Exception Error)
		{
			Console.Error.WriteLine($"Backfill failed: {Error.Message}");
			return 1;
		}
		finally
		{
			Console.CancelKeyPress -= CancelHandler;
		}
	}
}
