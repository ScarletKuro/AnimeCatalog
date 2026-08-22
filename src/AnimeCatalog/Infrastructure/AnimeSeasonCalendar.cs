namespace AnimeCatalog.Infrastructure;

/// <summary>
/// The year and season axis the archive browses along.
/// </summary>
/// <remarks>
/// The season strings are AniList's own <c>MediaSeason</c> values and travel to the API verbatim.
/// They are never modelled as a C# enum: JsonDefaults.Web registers a camelCase
/// JsonStringEnumConverter, so an enum would serialise into the GraphQL variables as "winter" and
/// AniList would reject it.
/// </remarks>
public static class AnimeSeasonCalendar
{
    /// <summary>
    /// AniList's catalogue reaches back to here.
    /// </summary>
    /// <remarks>
    /// This constant is the whole fix for AniChart's "restricted to 2009" limit - seasonYear 1940
    /// returns results, so the floor was always a UI choice rather than a property of the data. A
    /// lower floor would only add empty years.
    /// </remarks>
    public const int MinimumYear = 1940;

    /// <summary>
    /// How far past the current year the picker reaches. AniList carries announcements roughly two
    /// years out, and an empty season rendering the empty card is cheaper than a missing option.
    /// </summary>
    public const int YearsAhead = 2;

    public const string Winter = "WINTER";
    public const string Spring = "SPRING";
    public const string Summer = "SUMMER";
    public const string Fall = "FALL";

    /// <summary>
    /// Stands for "no season filter at all" - every title AniList assigns to the year.
    /// </summary>
    /// <remarks>
    /// Not a MediaSeason value and never sent to AniList. It travels as far as
    /// <see cref="Models.AniList.AniListBrowseRequest.Season"/>, where it is translated into a null
    /// that makes the GraphQL argument disappear entirely.
    /// </remarks>
    public const string WholeYear = "ALL";

    /// <summary>The four real seasons, in broadcast order. Deliberately excludes <see cref="WholeYear"/>.</summary>
    public static readonly string[] Seasons = [Winter, Spring, Summer, Fall];

    /// <summary>What the archive picker offers: the four seasons, then the whole year.</summary>
    public static readonly string[] PickerOptions = [Winter, Spring, Summer, Fall, WholeYear];

    public static int MaximumYear(TimeProvider timeProvider) =>
        timeProvider.GetLocalNow().Year + YearsAhead;

    /// <summary>Every selectable year, newest first - the order the picker lists them in.</summary>
    public static IEnumerable<int> Years(TimeProvider timeProvider)
    {
        for (var year = MaximumYear(timeProvider); year >= MinimumYear; year--)
        {
            yield return year;
        }
    }

    /// <summary>
    /// The season "now" falls in.
    /// </summary>
    /// <remarks>
    /// AniList buckets a title by its start month, and the boundary convention could not be verified
    /// against the live API - it was returning HTTP 403 throughout. The two candidates are
    /// Winter = Jan-Mar (used here) and Winter = Dec-Feb, under which a December premiere carries
    /// seasonYear + 1 and this method would owe December an extra year bump.
    /// <para>
    /// Confirm with <c>Page(perPage: 5) { media(season: WINTER, seasonYear: 2026, sort: [START_DATE])
    /// { startDate { year month } } }</c>: December 2025 start dates mean the Dec-Feb convention
    /// holds and <see cref="Current"/> needs the bump; January 2026 ones mean this is already right.
    /// Only the "jump to the current season" default depends on it - an explicitly chosen season is
    /// correct either way - and <see cref="AnimeSeasonCalendarTests"/> pins every month so flipping
    /// the boundary stays a one-line change.
    /// </para>
    /// </remarks>
    public static (int Year, string Season) Current(TimeProvider timeProvider)
    {
        var now = timeProvider.GetLocalNow();
        return (now.Year, SeasonForMonth(now.Month));
    }

    public static string SeasonForMonth(int month) => month switch
    {
        1 or 2 or 3 => Winter,
        4 or 5 or 6 => Spring,
        7 or 8 or 9 => Summer,
        _ => Fall
    };

    /// <summary>Normalises a route segment or query value, or null when it names no season.</summary>
    public static string? Normalise(string? season)
    {
        if (string.IsNullOrWhiteSpace(season))
        {
            return null;
        }

        var candidate = season.Trim().ToUpperInvariant();
        return PickerOptions.Contains(candidate) ? candidate : null;
    }

    /// <summary>Lower-case form used in the archive route, as in "spring".</summary>
    public static string ToRouteSlug(string season) => season.ToLowerInvariant();

    public static (int Year, string Season) Next(int year, string season)
    {
        // Stepping a whole-year view moves the year and stays whole-year. Without this the IndexOf
        // below returns -1 and it would quietly become Winter.
        if (IsWholeYear(season))
        {
            return (year + 1, WholeYear);
        }

        var index = Array.IndexOf(Seasons, Normalise(season) ?? Winter);

        // Past FALL the season index wraps and the year moves with it.
        return index == Seasons.Length - 1
            ? (year + 1, Seasons[0])
            : (year, Seasons[index + 1]);
    }

    public static (int Year, string Season) Previous(int year, string season)
    {
        if (IsWholeYear(season))
        {
            return (year - 1, WholeYear);
        }

        var index = Array.IndexOf(Seasons, Normalise(season) ?? Winter);

        return index == 0
            ? (year - 1, Seasons[^1])
            : (year, Seasons[index - 1]);
    }

    /// <summary>
    /// Whether a season is finished, which is what lets the browse cache hold it far longer: a past
    /// season's line-up does not change, and re-reading it costs five paced requests.
    /// </summary>
    public static bool IsHistorical(int year, string season, TimeProvider timeProvider)
    {
        var (currentYear, currentSeason) = Current(timeProvider);

        if (year != currentYear)
        {
            return year < currentYear;
        }

        // The current year as a whole is still in progress whatever season it is, so it never counts
        // as settled - otherwise a whole-year browse would be cached for twelve hours mid-year.
        if (IsWholeYear(season))
        {
            return false;
        }

        return Array.IndexOf(Seasons, Normalise(season) ?? Winter)
            < Array.IndexOf(Seasons, currentSeason);
    }

    public static bool IsWholeYear(string? season) =>
        string.Equals(Normalise(season), WholeYear, StringComparison.Ordinal);
}
