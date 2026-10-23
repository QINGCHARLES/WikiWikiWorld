namespace WikiWikiWorld.Web.Services;

/// <summary>
/// The finish of a badge medallion, from the easiest badges to the hardest.
/// </summary>
public enum ProfileBadgeTier
{
	/// <summary>
	/// A first milestone.
	/// </summary>
	Bronze,

	/// <summary>
	/// A sustained contribution.
	/// </summary>
	Silver,

	/// <summary>
	/// A major contribution.
	/// </summary>
	Gold,

	/// <summary>
	/// The rarest milestone, printed in ink black.
	/// </summary>
	Ink
}

/// <summary>
/// A badge with the user's progress towards it. Badges are computed from contribution totals on every request rather
/// than stored, so they appear retroactively and need no schema.
/// </summary>
public sealed record class ProfileBadge
{
	/// <summary>
	/// Gets the stable identifier, used as a CSS hook and for tests.
	/// </summary>
	public required string Key { get; init; }

	/// <summary>
	/// Gets the display name.
	/// </summary>
	public required string Name { get; init; }

	/// <summary>
	/// Gets the sentence describing how the badge is earned.
	/// </summary>
	public required string Description { get; init; }

	/// <summary>
	/// Gets the short typographic mark printed on the medallion.
	/// </summary>
	public required string Glyph { get; init; }

	/// <summary>
	/// Gets the medallion finish.
	/// </summary>
	public required ProfileBadgeTier Tier { get; init; }

	/// <summary>
	/// Gets the user's current value for the badge's measure, such as their edit count.
	/// </summary>
	public required int Progress { get; init; }

	/// <summary>
	/// Gets the value of the measure at which the badge is earned.
	/// </summary>
	public required int Target { get; init; }

	/// <summary>
	/// Gets a value indicating whether the user has reached the target.
	/// </summary>
	public bool IsEarned => Progress >= Target;

	/// <summary>
	/// Gets progress towards the target as a whole percentage from 0 to 100.
	/// </summary>
	public int ProgressPercent => Target <= 0
		? 100
		: (int)Math.Clamp(Math.Floor(Progress * 100.0 / Target), 0, 100);
}

/// <summary>
/// Defines the profile badges and editorial ranks, and evaluates them against a user's contribution totals.
/// </summary>
public static class ProfileBadgeCatalog
{
	/// <summary>
	/// Edit counts at which each rank begins, highest first.
	/// </summary>
	private static readonly (int MinimumEdits, string Rank)[] Ranks =
	[
		(2_500, "Masthead"),
		(500, "Archivist"),
		(50, "Regular"),
		(1, "Contributor"),
		(0, "Reader")
	];

	/// <summary>
	/// Evaluates every badge for a user. Earned badges come first; within each group the catalog order is kept, which
	/// runs from easiest to hardest per measure.
	/// </summary>
	/// <param name="Stats">The user's contribution totals.</param>
	/// <param name="MembershipDays">Whole days since the account was created.</param>
	/// <param name="LongestStreakDays">The longest run of consecutive editing days in the last year.</param>
	/// <param name="CategoryCount">The number of distinct categories across the articles the user edited.</param>
	/// <returns>All badges with progress, earned first.</returns>
	public static IReadOnlyList<ProfileBadge> Evaluate(ProfileStats Stats, int MembershipDays, int LongestStreakDays, int CategoryCount)
	{
		ArgumentNullException.ThrowIfNull(Stats);

		ProfileBadge[] Badges =
		[
			Create("debut", "Debut", "Published a first edit.", "I", ProfileBadgeTier.Bronze, Stats.EditCount, 1),
			Create("regular", "Regular", "Made 50 edits.", "L", ProfileBadgeTier.Silver, Stats.EditCount, 50),
			Create("archivist", "Archivist", "Made 500 edits.", "D", ProfileBadgeTier.Gold, Stats.EditCount, 500),
			Create("masthead", "Masthead", "Made 2,500 edits.", "M", ProfileBadgeTier.Ink, Stats.EditCount, 2_500),
			Create("byline", "Byline", "Started a first article.", "¶", ProfileBadgeTier.Bronze, Stats.ArticlesStartedCount, 1),
			Create("editor-at-large", "Editor at Large", "Started 25 articles.", "§", ProfileBadgeTier.Gold, Stats.ArticlesStartedCount, 25),
			Create("columnist", "Columnist", "Wrote 10 talk posts.", "“", ProfileBadgeTier.Silver, Stats.TalkPostCount, 10),
			Create("polymath", "Polymath", "Edited articles in 10 categories.", "&", ProfileBadgeTier.Silver, CategoryCount, 10),
			Create("on-deadline", "On Deadline", "Edited 7 days in a row.", "7", ProfileBadgeTier.Silver, LongestStreakDays, 7),
			Create("anniversary", "Anniversary", "A member for a year.", "1", ProfileBadgeTier.Bronze, MembershipDays, 365),
			Create("veteran", "Veteran", "A member for five years.", "V", ProfileBadgeTier.Gold, MembershipDays, 1_826)
		];

		return [.. Badges.Where(b => b.IsEarned), .. Badges.Where(b => !b.IsEarned)];
	}

	/// <summary>
	/// Returns the editorial rank for an edit count.
	/// </summary>
	/// <param name="EditCount">The user's total number of revisions.</param>
	/// <returns><c>Reader</c> for no edits, then <c>Contributor</c>, <c>Regular</c>, <c>Archivist</c>, and <c>Masthead</c>.</returns>
	public static string GetRank(int EditCount)
	{
		foreach ((int MinimumEdits, string Rank) in Ranks)
		{
			if (EditCount >= MinimumEdits)
			{
				return Rank;
			}
		}

		return Ranks[^1].Rank;
	}

	/// <summary>
	/// Creates a badge with the user's progress, clamping negative measures to zero.
	/// </summary>
	/// <param name="Key">The stable identifier.</param>
	/// <param name="Name">The display name.</param>
	/// <param name="Description">How the badge is earned.</param>
	/// <param name="Glyph">The medallion mark.</param>
	/// <param name="Tier">The medallion finish.</param>
	/// <param name="Progress">The user's current value of the measure.</param>
	/// <param name="Target">The value at which the badge is earned.</param>
	/// <returns>The evaluated badge.</returns>
	private static ProfileBadge Create(string Key, string Name, string Description, string Glyph, ProfileBadgeTier Tier, int Progress, int Target) => new()
	{
		Key = Key,
		Name = Name,
		Description = Description,
		Glyph = Glyph,
		Tier = Tier,
		Progress = Math.Max(0, Progress),
		Target = Target
	};
}
