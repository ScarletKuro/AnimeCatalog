using System.Globalization;
using AnimeCatalog.Infrastructure;

namespace AnimeCatalog.Tests;

public sealed class AiringTimeFormatterTests
{
    // Pins the extraction as byte-identical to the line Home.razor and AnimeDetails.razor each built
    // inline, so the refactor cannot quietly reword either page.
    [Fact]
    public void NextEpisodeLine_MatchesTheStringTheTwoPagesBuiltInline()
    {
        var airingAt = new DateTimeOffset(2026, 8, 21, 18, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        var expected = BuildTheOldWay(18, airingAt);

        Assert.Equal(expected, AiringTimeFormatter.NextEpisodeLine(18, airingAt));
        Assert.StartsWith("Episode 18 on ", AiringTimeFormatter.NextEpisodeLine(18, airingAt));
    }

    // A dev box or a visitor on en-US must not turn "18:30" into "6:30 PM" - the schedule column is
    // sized for the 24-hour form, and every assertion in this suite would otherwise be locale-bound.
    [Theory]
    [InlineData("en-US")]
    [InlineData("et-EE")]
    [InlineData("ja-JP")]
    public void ClockLabel_IsInvariantAcrossCultures(string culture)
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var localTime = new DateTimeOffset(2026, 8, 21, 18, 30, 0, TimeSpan.FromHours(3));

            Assert.Equal("18:30", AiringTimeFormatter.ClockLabel(localTime));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("et-EE")]
    public void SpokenAiringLabel_NamesTheDayInEnglishWhateverTheCulture(string culture)
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var localTime = new DateTimeOffset(2026, 8, 24, 18, 30, 0, TimeSpan.FromHours(3));

            Assert.Equal("Airs Monday 24 August at 18:30.", AiringTimeFormatter.SpokenAiringLabel(localTime));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // Counts what has been broadcast against what has been watched, and nothing else. Watching more
    // than has aired - a rewatch, or a count nudged ahead - still reads as caught up rather than
    // going negative.
    [Theory]
    [InlineData(4, 4, "Caught up")]
    [InlineData(4, 5, "Caught up")]
    [InlineData(4, 9, "Caught up")]
    [InlineData(4, 3, "1 episode behind")]
    [InlineData(4, 1, "3 episodes behind")]
    [InlineData(0, 0, "Caught up")]
    [InlineData(11, 0, "11 episodes behind")]
    public void BehindLabel_CountsAiredEpisodesAgainstWatchedOnes(int aired, int watched, string expected)
    {
        Assert.Equal(expected, AiringTimeFormatter.BehindLabel(aired, watched));
    }

    [Theory]
    [InlineData(4, 4, false)]
    [InlineData(4, 3, true)]
    public void IsBehind_AgreesWithTheLabel(int aired, int watched, bool expected)
    {
        Assert.Equal(expected, AiringTimeFormatter.IsBehind(aired, watched));
    }

    [Fact]
    public void WeekRangeLabel_CollapsesARepeatedMonth()
    {
        Assert.Equal("17-23 August 2026", AiringTimeFormatter.WeekRangeLabel(new DateOnly(2026, 8, 17)));
    }

    [Fact]
    public void WeekRangeLabel_SpellsOutBothMonthsWhenTheWeekStraddlesThem()
    {
        Assert.Equal("31 Aug - 6 Sep 2026", AiringTimeFormatter.WeekRangeLabel(new DateOnly(2026, 8, 31)));
    }

    [Fact]
    public void WeekRangeLabel_SpellsOutBothYearsAtAYearBoundary()
    {
        Assert.Equal("28 Dec 2026 - 3 Jan 2027", AiringTimeFormatter.WeekRangeLabel(new DateOnly(2026, 12, 28)));
    }

    [Fact]
    public void DayLabels_AreInvariantEnglish()
    {
        var date = new DateOnly(2026, 8, 24);

        Assert.Equal("Monday", AiringTimeFormatter.DayNameLabel(date));
        Assert.Equal("24 Aug", AiringTimeFormatter.DayDateLabel(date));
        Assert.Equal("2026-08-24", AiringTimeFormatter.IsoDate(date));
    }

    /// <summary>The exact expression Home.razor:419 and AnimeDetails.razor:737 used before extraction.</summary>
    private static string BuildTheOldWay(int episode, long airingAtUnixSeconds)
    {
        var airsAt = DateTimeOffset.FromUnixTimeSeconds(airingAtUnixSeconds).ToLocalTime();
        return $"Episode {episode} on {airsAt:yyyy-MM-dd HH:mm}";
    }
}
