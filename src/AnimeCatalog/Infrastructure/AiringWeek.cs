using System.Globalization;

namespace AnimeCatalog.Infrastructure;

/// <summary>
/// Seven consecutive local days, and the AniList query window that covers them.
/// </summary>
/// <remarks>
/// <para>
/// The week starts on Monday, as a constant. Deliberately NOT
/// <c>CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek</c>: in WebAssembly that follows the
/// browser locale, which is Sunday on en-US, so the same schedule would render shifted by a day for
/// two visitors looking at it side by side. Monday also matches how Japanese broadcast weeks are
/// published.
/// </para>
/// <para>
/// Clock and zone arrive as parameters rather than being read from <c>DateTimeOffset.Now</c>, the
/// same way FranchiseService takes its clock, so every case below is unit-testable.
/// </para>
/// </remarks>
public sealed record AiringWeek(DateOnly StartDate)
{
    public const DayOfWeek WeekStartsOn = DayOfWeek.Monday;

    public const int DaysInWeek = 7;

    /// <summary>
    /// Slack added to each end of the AniList query window.
    /// </summary>
    /// <remarks>
    /// A local week is not 168 hours - a DST transition makes it 167 or 169 - and the instant of
    /// local midnight is not even well defined on the days that matter: on a spring-forward day the
    /// wall clock never shows 00:00, and on an autumn one it shows it twice.
    /// <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> throws on the former and
    /// silently picks the standard offset for the latter, either of which moves the boundary by an
    /// hour and drops or duplicates an episode.
    /// <para>
    /// So the boundary instant is never computed precisely. The window is widened instead, and
    /// <see cref="LocalAiringDate"/> - converting an instant to a local date, which is always
    /// unambiguous - is the single authority on which day an episode belongs to. Anything landing
    /// outside the seven days is discarded. Widening is free: AniList does the filtering.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan WindowPadding = TimeSpan.FromHours(2);

    /// <summary>The week containing "now" in the visitor's own zone.</summary>
    public static AiringWeek Current(TimeProvider timeProvider) =>
        Containing(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));

    /// <summary>Snaps any local date back to the Monday of its week.</summary>
    public static AiringWeek Containing(DateOnly localDate) =>
        new(localDate.AddDays(-(((int)localDate.DayOfWeek - (int)WeekStartsOn + DaysInWeek) % DaysInWeek)));

    public DateOnly EndDate => StartDate.AddDays(DaysInWeek - 1);

    public AiringWeek Previous() => new(StartDate.AddDays(-DaysInWeek));

    public AiringWeek Next() => new(StartDate.AddDays(DaysInWeek));

    public IEnumerable<DateOnly> Days() =>
        Enumerable.Range(0, DaysInWeek).Select(StartDate.AddDays);

    /// <summary>Stable key for the week, always the local Monday, as in "2026-08-17".</summary>
    public string Key => StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Start of the padded query window. See <see cref="WindowPadding"/> for why this is deliberately
    /// approximate.
    /// </summary>
    public DateTimeOffset QueryStart(TimeZoneInfo zone) =>
        ApproximateStartOfDay(StartDate, zone) - WindowPadding;

    /// <summary>End of the padded query window, exclusive of the day after the week.</summary>
    public DateTimeOffset QueryEnd(TimeZoneInfo zone) =>
        ApproximateStartOfDay(StartDate.AddDays(DaysInWeek), zone) + WindowPadding;

    /// <summary>
    /// The authoritative day bucket for an episode. This, never the query bounds, decides which
    /// column a row lands in.
    /// </summary>
    public static DateOnly LocalAiringDate(long airingAtUnixSeconds, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(airingAtUnixSeconds), zone).DateTime);

    /// <summary>Whether an episode falls inside the seven days rather than in the padding.</summary>
    public bool Contains(long airingAtUnixSeconds, TimeZoneInfo zone)
    {
        var date = LocalAiringDate(airingAtUnixSeconds, zone);
        return date >= StartDate && date <= EndDate;
    }

    public bool Includes(DateOnly localDate) => localDate >= StartDate && localDate <= EndDate;

    /// <summary>
    /// A best-effort instant for local midnight, only ever used after <see cref="WindowPadding"/> has
    /// been applied. <c>GetUtcOffset</c> is used rather than <c>ConvertTimeToUtc</c> precisely because
    /// it never throws on a skipped or ambiguous local time - being an hour out here is absorbed by
    /// the padding, whereas an exception would take the page down on two days a year.
    /// </summary>
    private static DateTimeOffset ApproximateStartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }
}
