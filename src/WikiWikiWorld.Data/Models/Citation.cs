namespace WikiWikiWorld.Data.Models;

public sealed record Citation
{
	public required int Number { get; init; }
	public required string Id { get; init; }
	public required Dictionary<string, List<string>> Properties { get; init; }
	public List<string> ReferencedBy { get; init; } = [];
}
