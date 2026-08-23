using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

/// <summary>One episode broadcast, ready to render as a schedule row.</summary>
public sealed record ScheduleEpisodeViewModel
{
    public required int AniListId { get; init; }

    public required string Title { get; init; }

    public string? CoverUrl { get; init; }

    public required DateTimeOffset AirsAtLocal { get; init; }

    public int? Episode { get; init; }

    public int? TotalEpisodes { get; init; }

    /// <summary>
    /// Episodes of this series broadcast so far, from AniList's own nextAiringEpisode rather than
    /// from <see cref="Episode"/>. Null when AniList says neither how many have aired nor how many
    /// there are, in which case the catalog note is left off rather than guessed at.
    /// </summary>
    public int? EpisodesAired { get; init; }

    public string? Format { get; init; }

    public int? Duration { get; init; }

    public string? CountryOfOrigin { get; init; }

    public bool IsAdult { get; init; }

    public string? SiteUrl { get; init; }

    /// <summary>Null when the title is not in the catalog, which is also what drives highlighting.</summary>
    public CatalogOverlayItem? Catalog { get; init; }

    public bool IsCataloged => Catalog is not null;

    /// <summary>
    /// A cataloged title routes to its own page; anything else opens AniList in a new tab. That
    /// asymmetry is most of the value of highlighting.
    /// </summary>
    public string? Href => Catalog is not null ? $"anime/{Catalog.AnimeEntryId}" : SiteUrl;

    public bool IsExternalHref => Catalog is null;

    public string? Subtitle => MediaDisplayFormatter.FormatLabel(Format) is { Length: > 0 } format
        && MediaDisplayFormatter.DurationLabel(Duration) is { } duration
            ? $"{format} · {duration}"
            : MediaDisplayFormatter.FormatLabel(Format);

    /// <summary>
    /// "3 episodes behind" or "Caught up", for any title in the catalog whatever its status - a
    /// planned or dropped series still gets one, which is how the progress on it stays visible.
    /// </summary>
    /// <remarks>
    /// Keyed on <see cref="EpisodesAired"/>, not on <see cref="Episode"/>: the same series must read
    /// the same on every week of the calendar, because how far behind you are does not depend on
    /// which week you happen to be looking at.
    /// </remarks>
    public string? CatalogNote => Catalog is null || EpisodesAired is null
        ? null
        : AiringTimeFormatter.BehindLabel(EpisodesAired.Value, Catalog.EpisodesWatched);

    public bool IsBehind => Catalog is not null
        && EpisodesAired is not null
        && AiringTimeFormatter.IsBehind(EpisodesAired.Value, Catalog.EpisodesWatched);

    public CatalogStatus? CatalogStatus => Catalog?.Status;
}

/// <summary>One column of the weekly grid.</summary>
public sealed record AiringDayViewModel
{
    public required DateOnly Date { get; init; }

    public required IReadOnlyList<ScheduleEpisodeViewModel> Episodes { get; init; }

    public bool IsToday { get; init; }

    public int Count => Episodes.Count;
}

/// <summary>The whole weekly schedule, filtered and bucketed.</summary>
public sealed record AiringWeekViewModel
{
    public required AiringWeek Week { get; init; }

    /// <summary>Always seven entries, whether or not any episodes landed in them.</summary>
    public required IReadOnlyList<AiringDayViewModel> Days { get; init; }

    /// <summary>How many episodes survived the filters - the only honest progress number.</summary>
    public int VisibleEpisodeCount => Days.Sum(day => day.Count);

    /// <summary>How many arrived before filtering, so "no matches" can be told from "nothing aired".</summary>
    public int LoadedEpisodeCount { get; init; }

    public bool IsComplete { get; init; }

    public bool WasTruncated { get; init; }

    public DateTimeOffset? CompleteThrough { get; init; }

    public string? DegradedMessage { get; init; }

    /// <summary>Formats available in what actually loaded, for the format filter's options.</summary>
    public IReadOnlyList<string> AvailableFormats { get; init; } = [];
}
