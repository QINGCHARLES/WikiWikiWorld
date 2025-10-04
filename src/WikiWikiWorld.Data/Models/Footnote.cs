namespace WikiWikiWorld.Data.Models;

public sealed record Footnote
{
    public required int Number { get; init; }
    public required string Text { get; init; }
}
