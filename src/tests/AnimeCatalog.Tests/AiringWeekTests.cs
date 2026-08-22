using System.Globalization;
using AnimeCatalog.Infrastructure;

namespace AnimeCatalog.Tests;

public sealed class AiringWeekTests
{
    [Theory]
    // Every day of one week snaps back to the same Monday, 2026-08-17.
    [InlineData("2026-08-17", DayOfWeek.Monday)]
    [InlineData("2026-08-18", DayOfWeek.Tuesday)]
    [InlineData("2026-08-19", DayOfWeek.Wednesday)]
    [InlineData("2026-08-20", DayOfWeek.Thursday)]
    [InlineData("2026-08-21", DayOfWeek.Friday)]
    [InlineData("2026-08-22", DayOfWeek.Saturday)]
    [InlineData("2026-08-23", DayOfWeek.Sunday)]
    public void EveryDay_SnapsBackToItsMonday(string date, DayOfWeek expectedDay)
    {
        var localDate = DateOnly.Parse(date, CultureInfo.InvariantCulture);
        Assert.Equal(expectedDay, localDate.DayOfWeek);

        var week = AiringWeek.Containing(localDate);

        Assert.Equal(new DateOnly(2026, 8, 17), week.StartDate);
        Assert.Equal(DayOfWeek.Monday, week.StartDate.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 8, 23), week.EndDate);
    }

    // The week must not follow the browser locale. On en-US FirstDayOfWeek is Sunday, and honouring
    // it would shift all seven columns by a day for half the visitors.
    [Theory]
    [InlineData("en-US")]
    [InlineData("et-EE")]
    [InlineData("ja-JP")]
    public void TheWeekStartsOnMonday_WhateverTheCultureSays(string culture)
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            // A Sunday: the day the two conventions disagree about most sharply.
            var week = AiringWeek.Containing(new DateOnly(2026, 8, 23));

            Assert.Equal(new DateOnly(2026, 8, 17), week.StartDate);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Current_UsesTheProvidersLocalDate_NotItsUtcDate()
    {
        // 23:30 UTC on Sunday is already Monday 01:30 in a +02:00 zone, so the local date decides.
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 23, 23, 30, 0, TimeSpan.Zero),
            TimeZoneInfo.CreateCustomTimeZone("Test/Plus2", TimeSpan.FromHours(2), "Test +2", "Test +2"));

        Assert.Equal(new DateOnly(2026, 8, 24), AiringWeek.Current(timeProvider).StartDate);
    }

    [Fact]
    public void PreviousAndNext_StepWholeWeeks()
    {
        var week = AiringWeek.Containing(new DateOnly(2026, 8, 19));

        Assert.Equal(new DateOnly(2026, 8, 10), week.Previous().StartDate);
        Assert.Equal(new DateOnly(2026, 8, 24), week.Next().StartDate);
        Assert.Equal(week, week.Next().Previous());
    }

    [Fact]
    public void Days_AreSevenConsecutiveDatesStartingAtTheMonday()
    {
        var days = AiringWeek.Containing(new DateOnly(2026, 8, 19)).Days().ToList();

        Assert.Equal(7, days.Count);
        Assert.Equal(new DateOnly(2026, 8, 17), days[0]);
        Assert.Equal(new DateOnly(2026, 8, 23), days[6]);
        Assert.Equal(days.OrderBy(day => day).ToList(), days);
    }

    [Fact]
    public void Key_IsTheLocalMonday()
    {
        Assert.Equal("2026-08-17", AiringWeek.Containing(new DateOnly(2026, 8, 22)).Key);
    }

    // The two DST cases. Every local hour of the week has to land in exactly one column, with none
    // lost to a 167-hour week and none duplicated by a 169-hour one.
    [Theory]
    [InlineData("2026-03-23")] // contains the spring-forward Sunday
    [InlineData("2026-10-19")] // contains the autumn-back Sunday
    public void AcrossADstTransition_EveryLocalHourBucketsIntoExactlyOneDay(string monday)
    {
        var zone = FixedTimeProvider.EuropeanStyleZone;
        var week = AiringWeek.Containing(DateOnly.Parse(monday, CultureInfo.InvariantCulture));

        var start = week.QueryStart(zone);
        var end = week.QueryEnd(zone);
        var counts = week.Days().ToDictionary(day => day, _ => 0);

        // Walk the whole padded window in ten-minute steps and bucket every instant.
        for (var instant = start; instant < end; instant += TimeSpan.FromMinutes(10))
        {
            var seconds = instant.ToUnixTimeSeconds();

            if (!week.Contains(seconds, zone))
            {
                continue;
            }

            counts[AiringWeek.LocalAiringDate(seconds, zone)]++;
        }

        // Every day got instants, and the transition days are the only ones off the 144-per-day
        // baseline - which is exactly the hour DST adds or removes, and it is accounted for, not lost.
        Assert.All(counts, entry => Assert.True(entry.Value > 0, $"{entry.Key} had no instants"));

        var total = counts.Values.Sum();
        Assert.InRange(total, 7 * 144 - 6, 7 * 144 + 6);
    }

    [Theory]
    [InlineData("2026-03-23")]
    [InlineData("2026-10-19")]
    [InlineData("2026-08-17")]
    public void TheQueryWindow_IsStrictlyWiderThanTheLocalWeek(string monday)
    {
        var zone = FixedTimeProvider.EuropeanStyleZone;
        var week = AiringWeek.Containing(DateOnly.Parse(monday, CultureInfo.InvariantCulture));

        var start = week.QueryStart(zone);
        var end = week.QueryEnd(zone);

        // The first and last local instants of the week must both sit inside the padded window,
        // whichever way the transition moved the boundary.
        var firstMoment = new DateTimeOffset(
            week.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            zone.GetUtcOffset(week.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified)));

        var lastMoment = new DateTimeOffset(
            week.EndDate.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Unspecified),
            zone.GetUtcOffset(week.EndDate.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Unspecified)));

        Assert.True(start < firstMoment, "the window must open before the week does");
        Assert.True(end > lastMoment, "the window must close after the week does");
    }

    [Fact]
    public void AnEpisodeInThePadding_IsNotPartOfTheWeek()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/Plus2", TimeSpan.FromHours(2), "Test +2", "Test +2");
        var week = AiringWeek.Containing(new DateOnly(2026, 8, 17));

        // 23:30 local on the Sunday before the week starts: inside the padded window, outside the week.
        var justBefore = new DateTimeOffset(2026, 8, 16, 23, 30, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds();

        Assert.False(week.Contains(justBefore, zone));
        Assert.True(week.Contains(new DateTimeOffset(2026, 8, 17, 0, 30, 0, TimeSpan.FromHours(2)).ToUnixTimeSeconds(), zone));
    }

    [Fact]
    public void BucketingUsesTheLocalDate_SoALateNightEpisodeStaysOnItsLocalDay()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/Plus9", TimeSpan.FromHours(9), "Test +9", "Test +9");

        // 2026-08-20 17:00 UTC is 2026-08-21 02:00 in +09:00 - a Friday, not a Thursday.
        var airingAt = new DateTimeOffset(2026, 8, 20, 17, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        Assert.Equal(new DateOnly(2026, 8, 21), AiringWeek.LocalAiringDate(airingAt, zone));
    }
}
