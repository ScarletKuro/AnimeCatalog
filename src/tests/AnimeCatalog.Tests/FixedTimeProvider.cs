namespace AnimeCatalog.Tests;

/// <summary>
/// A clock that also pins the local time zone.
/// </summary>
/// <remarks>
/// The zone override is the point: week bucketing is entirely a local-time question, so a provider
/// that only fixes the instant leaves every DST case at the mercy of whatever zone the test machine
/// happens to be in. AniListEnrichmentServiceTests has an older private fake that overrides
/// GetUtcNow alone; consolidating the two is not worth the churn.
/// </remarks>
internal sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo? localTimeZone = null)
    {
        _utcNow = utcNow;
        LocalTimeZone = localTimeZone ?? TimeZoneInfo.Utc;
    }

    public override TimeZoneInfo LocalTimeZone { get; }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;

    /// <summary>
    /// A synthetic zone with EU-style DST, used instead of FindSystemTimeZoneById so the DST tests
    /// do not depend on the host's time-zone database or on ICU being present in CI.
    /// </summary>
    public static TimeZoneInfo EuropeanStyleZone { get; } = CreateEuropeanStyleZone();

    private static TimeZoneInfo CreateEuropeanStyleZone()
    {
        // Last Sunday of March at 02:00 forward, last Sunday of October at 03:00 back.
        var transitionStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday);

        var transitionEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday);

        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            transitionStart,
            transitionEnd);

        return TimeZoneInfo.CreateCustomTimeZone(
            "Test/EuropeanStyle",
            TimeSpan.FromHours(2),
            "Test European Style",
            "Test Standard",
            "Test Daylight",
            [rule]);
    }
}
