using AnimeCatalog.Models.AniList;

namespace AnimeCatalog.ViewModels;

/// <summary>
/// An airing-window read, possibly still in flight.
/// </summary>
/// <remarks>
/// Every field has to be meaningful mid-walk, because the page renders each progress report rather
/// than waiting for the last one.
/// </remarks>
public sealed record AiringScheduleLoad
{
    public static readonly AiringScheduleLoad Empty = new();

    public IReadOnlyList<AniListAiringSchedule> Schedules { get; init; } = [];

    public int PagesLoaded { get; init; }

    public bool IsComplete { get; init; }

    /// <summary>True when the page cap stopped the walk before AniList ran out of results.</summary>
    public bool WasTruncated { get; init; }

    /// <summary>
    /// The last airing time actually received.
    /// </summary>
    /// <remarks>
    /// Because the query sorts by time, everything up to this instant is complete - which is the only
    /// honest progress statement available, given pageInfo.total is fiction on page 1. It also makes
    /// a truncated week legible: with a time sort, running out of pages means the *end* of the week
    /// is missing, so Sunday looks empty rather than unknown, and the notice has to name the
    /// boundary rather than vaguely say "incomplete".
    /// </remarks>
    public DateTimeOffset? CompleteThrough { get; init; }

    /// <summary>Set when AniList stopped answering part-way. Drives the amber degraded notice.</summary>
    public string? DegradedMessage { get; init; }

    public bool IsDegraded => DegradedMessage is not null || WasTruncated;
}
