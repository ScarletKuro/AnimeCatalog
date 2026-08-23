using System.Globalization;

namespace AnimeCatalog.Infrastructure;

/// <summary>
/// Turns AniList's unix airing timestamps into the strings the UI shows.
/// </summary>
/// <remarks>
/// <para>
/// Every format here passes <see cref="CultureInfo.InvariantCulture"/>, including the *visible*
/// time. That is a deliberate divergence from StatMeter's note that visible text stays
/// culture-sensitive: Blazor WebAssembly takes CurrentCulture from the browser, so an en-US visitor
/// would get "6:30 PM" in a schedule column sized for "18:30", and every test assertion would
/// depend on the locale of the machine running it. Hard-coded English is already this app's
/// localisation policy.
/// </para>
/// <para>
/// Week arithmetic deliberately does not live here - see <c>AiringWeek</c>. This file is display
/// strings only.
/// </para>
/// </remarks>
public static class AiringTimeFormatter
{
    /// <summary>Converts AniList's unix seconds to the browser's local zone.</summary>
    public static DateTimeOffset ToLocal(long airingAtUnixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(airingAtUnixSeconds).ToLocalTime();

    /// <summary>
    /// The "next episode" line, as in "Episode 18 on 2026-08-21 18:30".
    /// </summary>
    /// <remarks>
    /// The home page and the details page each built this inline, character for character. Keeping
    /// one copy is the point; a test pins the output so the extraction stayed byte-identical.
    /// </remarks>
    public static string NextEpisodeLine(int episode, long airingAtUnixSeconds)
    {
        var airsAt = ToLocal(airingAtUnixSeconds);
        return string.Create(CultureInfo.InvariantCulture, $"Episode {episode} on {airsAt:yyyy-MM-dd HH:mm}");
    }

    /// <summary>24-hour clock label for a schedule row, as in "18:30".</summary>
    public static string ClockLabel(DateTimeOffset localTime) =>
        localTime.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// The sr-only sentence carrying what a schedule column conveys by position alone, as in
    /// "Airs Monday 24 August at 18:30."
    /// </summary>
    public static string SpokenAiringLabel(DateTimeOffset localTime) =>
        localTime.ToString("'Airs' dddd d MMMM 'at' HH:mm'.'", CultureInfo.InvariantCulture);

    /// <summary>Day heading for a schedule column, as in "Mon".</summary>
    public static string DayNameLabel(DateOnly date) =>
        date.ToString("dddd", CultureInfo.InvariantCulture);

    /// <summary>Date under a day heading, as in "24 Aug".</summary>
    public static string DayDateLabel(DateOnly date) =>
        date.ToString("d MMM", CultureInfo.InvariantCulture);

    /// <summary>Machine-readable date for a <c>&lt;time datetime&gt;</c> attribute.</summary>
    public static string IsoDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The week's span, as in "18-24 August 2026", collapsing the repeated month or year where it
    /// would otherwise read twice.
    /// </summary>
    public static string WeekRangeLabel(DateOnly weekStart)
    {
        var weekEnd = weekStart.AddDays(6);

        if (weekStart.Year != weekEnd.Year)
        {
            return $"{weekStart.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} - {weekEnd.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        }

        if (weekStart.Month != weekEnd.Month)
        {
            return $"{weekStart.ToString("d MMM", CultureInfo.InvariantCulture)} - {weekEnd.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        }

        return $"{weekStart.Day.ToString(CultureInfo.InvariantCulture)}-{weekEnd.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// How far behind the owner is on a series with <paramref name="episodesAired"/> episodes out.
    /// This is the fact neither AniChart nor AnimeSchedule can show.
    /// </summary>
    /// <remarks>
    /// Takes the count of episodes actually broadcast, not the episode number of the row it is being
    /// rendered against, and the difference is the whole point. Deriving the count from the row read
    /// the schedule as if every row were airing now, so paging the calendar forward invented a
    /// backlog: next week's episode 9 became "1 episode behind" while episode 8 had not aired either.
    /// Being behind is a fact about the present, so it must not change with the week on screen.
    /// </remarks>
    public static string BehindLabel(int episodesAired, int episodesWatched)
    {
        var behind = episodesAired - episodesWatched;

        return behind switch
        {
            <= 0 => "Caught up",
            1 => "1 episode behind",
            _ => string.Create(CultureInfo.InvariantCulture, $"{behind} episodes behind")
        };
    }

    /// <summary>True when <paramref name="episodesAired"/> leaves the owner with episodes to watch.</summary>
    public static bool IsBehind(int episodesAired, int episodesWatched) =>
        episodesAired - episodesWatched > 0;
}
