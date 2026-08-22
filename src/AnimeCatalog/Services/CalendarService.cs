using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Services;

/// <summary>
/// Turns raw AniList pages into the calendar's view models.
/// </summary>
/// <remarks>
/// Pure and stateless with no DI dependencies, the same shape as FranchiseService: the clock and the
/// time zone arrive as parameters so every case here is unit-testable without touching
/// TimeZoneInfo.Local.
/// </remarks>
public sealed class CalendarService
{
    public AiringWeekViewModel BuildAiringWeek(
        AiringWeek week,
        TimeZoneInfo zone,
        DateTimeOffset now,
        AiringScheduleLoad load,
        CatalogOverlay overlay,
        AiringWeekFilters filters)
    {
        var episodes = new List<ScheduleEpisodeViewModel>();
        var seen = new HashSet<(int MediaId, int Episode, long AiringAt)>();

        foreach (var schedule in load.Schedules)
        {
            // AniList can hold a schedule row whose media was deleted. Skipped rather than asserted:
            // one stale row must not take the week down.
            if (schedule.Media is null)
            {
                continue;
            }

            // The padded query window deliberately reaches past the week - see AiringWeek - so
            // anything outside the seven local days is dropped here.
            if (!week.Contains(schedule.AiringAt, zone))
            {
                continue;
            }

            // Keyed on the airing time as well as the episode: a genuine rebroadcast is the same
            // series and episode at a different time, and it belongs on both days. Only an exact
            // duplicate is dropped.
            if (!seen.Add((schedule.MediaId, schedule.Episode, schedule.AiringAt)))
            {
                continue;
            }

            episodes.Add(Project(schedule, schedule.Media, zone, now, overlay));
        }

        var visible = episodes.Where(episode => Matches(episode, filters)).ToList();

        var days = week.Days()
            .Select(date => new AiringDayViewModel
            {
                Date = date,
                IsToday = date == DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime),
                Episodes = visible
                    .Where(episode => DateOnly.FromDateTime(episode.AirsAtLocal.DateTime) == date)
                    .OrderBy(episode => episode.AirsAtLocal)
                    .ThenBy(episode => episode.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();

        return new AiringWeekViewModel
        {
            Week = week,
            Days = days,
            LoadedEpisodeCount = episodes.Count,
            IsComplete = load.IsComplete,
            WasTruncated = load.WasTruncated,
            CompleteThrough = load.CompleteThrough,
            DegradedMessage = load.DegradedMessage,
            AvailableFormats = episodes
                .Select(episode => episode.Format)
                .Where(format => !string.IsNullOrWhiteSpace(format))
                .Select(format => format!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(format => format, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public SeasonArchiveViewModel BuildArchive(
        int year,
        string season,
        IReadOnlyList<AniListMedia> media,
        CatalogOverlay overlay,
        CatalogHighlightFilter catalogFilter)
    {
        var entries = media
            // AniList occasionally returns the same id twice across page boundaries when the
            // underlying sort key ties, so the grid de-duplicates rather than rendering a double.
            .GroupBy(item => item.Id)
            .Select(group => Project(group.First(), overlay))
            .ToList();

        var visible = catalogFilter == CatalogHighlightFilter.OnlyMine
            ? entries.Where(entry => entry.IsCataloged).ToList()
            : entries;

        return new SeasonArchiveViewModel
        {
            Year = year,
            Season = season,
            Entries = visible,
            Groups = GroupForDisplay(visible, season),
            LoadedCount = entries.Count,
            CatalogedCount = entries.Count(entry => entry.IsCataloged)
        };
    }

    /// <summary>
    /// Bands a whole-year browse by season, or hands a single-season browse back as one unlabelled
    /// group.
    /// </summary>
    /// <remarks>
    /// Grouping is done here rather than by issuing four separate season queries: one unfiltered
    /// year query is a single walk, it also catches titles AniList gave a seasonYear but no season,
    /// and every entry already carries its own season because CalendarFields asks for it. The order
    /// within each band is whatever AniList's sort produced, so the chosen sort still holds inside a
    /// season.
    /// </remarks>
    private static IReadOnlyList<SeasonArchiveGroup> GroupForDisplay(
        IReadOnlyList<SeasonArchiveEntryViewModel> entries,
        string season)
    {
        if (!AnimeSeasonCalendar.IsWholeYear(season))
        {
            return [new SeasonArchiveGroup(null, entries)];
        }

        return entries
            .GroupBy(entry => AnimeSeasonCalendar.Normalise(entry.Season))
            // Broadcast order, with anything AniList left unseasoned last rather than dropped.
            .OrderBy(group => group.Key is null)
            .ThenBy(group => MediaDisplayFormatter.SeasonSortKey(group.Key))
            .Select(group => new SeasonArchiveGroup(group.Key ?? "UNKNOWN", group.ToList()))
            .ToList();
    }

    private static ScheduleEpisodeViewModel Project(
        AniListAiringSchedule schedule,
        AniListMedia media,
        TimeZoneInfo zone,
        DateTimeOffset now,
        CatalogOverlay overlay)
    {
        var airsAtLocal = TimeZoneInfo.ConvertTime(schedule.AiringAtUtc, zone);

        return new ScheduleEpisodeViewModel
        {
            AniListId = media.Id,
            Title = PrimaryTitle(media),
            CoverUrl = media.CoverImage?.BestUrl,
            AirsAtLocal = airsAtLocal,
            Episode = schedule.Episode > 0 ? schedule.Episode : null,
            TotalEpisodes = media.Episodes,
            Format = media.Format,
            Duration = media.Duration,
            CountryOfOrigin = media.CountryOfOrigin,
            IsAdult = media.IsAdult ?? false,
            SiteUrl = media.SiteUrl,
            Catalog = overlay.IsDecorating ? overlay.Find(media.Id) : null
        };
    }

    private static SeasonArchiveEntryViewModel Project(AniListMedia media, CatalogOverlay overlay) => new()
    {
        AniListId = media.Id,
        Title = PrimaryTitle(media),
        CoverUrl = media.CoverImage?.BestUrl,
        Format = media.Format,
        Season = media.Season,
        SeasonYear = media.SeasonYear,
        Episodes = media.Episodes,
        CommunityScore = media.AverageScore,
        Genres = media.Genres,
        SiteUrl = media.SiteUrl,
        Status = media.Status,
        Catalog = overlay.IsDecorating ? overlay.Find(media.Id) : null
    };

    private static bool Matches(ScheduleEpisodeViewModel episode, AiringWeekFilters filters)
    {
        if (filters.HideAdult && episode.IsAdult)
        {
            return false;
        }

        if (filters.HideShorts && string.Equals(episode.Format, "TV_SHORT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filters.Format)
            && !string.Equals(episode.Format, filters.Format, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filters.Country)
            && !string.Equals(episode.CountryOfOrigin, filters.Country, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (filters.Catalog == CatalogHighlightFilter.OnlyMine && !episode.IsCataloged)
        {
            return false;
        }

        // Dim-others deliberately does NOT filter: the row stays, played down, so the visitor can
        // still see what else is on. Same affordance as WatchNext's "dim below the bar".
        if (!string.IsNullOrWhiteSpace(filters.Query)
            && !episode.Title.Contains(filters.Query.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string PrimaryTitle(AniListMedia media) =>
        new[] { media.Title.English, media.Title.Romaji, media.Title.Native }
            .FirstOrDefault(title => !string.IsNullOrWhiteSpace(title))
        ?? $"AniList #{media.Id}";
}
