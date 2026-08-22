using AnimeCatalog.Infrastructure;

namespace AnimeCatalog.Tests;

public sealed class AnimeSeasonCalendarTests
{
    // All twelve months pinned, so flipping the Winter boundary from Jan-Mar to Dec-Feb - the
    // convention that could not be verified while AniList was returning 403 - stays a one-line
    // change with a failing test pointing straight at it.
    [Theory]
    [InlineData(1, "WINTER")]
    [InlineData(2, "WINTER")]
    [InlineData(3, "WINTER")]
    [InlineData(4, "SPRING")]
    [InlineData(5, "SPRING")]
    [InlineData(6, "SPRING")]
    [InlineData(7, "SUMMER")]
    [InlineData(8, "SUMMER")]
    [InlineData(9, "SUMMER")]
    [InlineData(10, "FALL")]
    [InlineData(11, "FALL")]
    [InlineData(12, "FALL")]
    public void SeasonForMonth_CoversTheWholeYear(int month, string expected)
    {
        Assert.Equal(expected, AnimeSeasonCalendar.SeasonForMonth(month));
    }

    [Fact]
    public void Current_ReadsTheProvidersLocalDate()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal((2026, "SUMMER"), AnimeSeasonCalendar.Current(timeProvider));
    }

    // December is the month the two boundary conventions disagree about. Documented here so the
    // behaviour is explicit rather than incidental.
    [Fact]
    public void Current_TreatsDecemberAsFallOfTheSameYear()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 12, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal((2026, "FALL"), AnimeSeasonCalendar.Current(timeProvider));
    }

    // The requirement, stated as a test: the archive reaches back to 1940, not 2009.
    [Fact]
    public void Years_SpanNineteenFortyToTwoYearsAhead()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero));

        var years = AnimeSeasonCalendar.Years(timeProvider).ToList();

        Assert.Equal(2028, years[0]);
        Assert.Equal(1940, years[^1]);
        Assert.Contains(2009, years);
        Assert.Contains(1963, years);

        // Newest first: the order the picker lists them in.
        Assert.Equal(years.OrderByDescending(year => year).ToList(), years);
        Assert.Equal(2028 - 1940 + 1, years.Count);
    }

    [Theory]
    [InlineData("all", "ALL")]
    [InlineData("ALL", "ALL")]
    [InlineData("winter", "WINTER")]
    [InlineData("SPRING", "SPRING")]
    [InlineData("  summer  ", "SUMMER")]
    [InlineData("Fall", "FALL")]
    [InlineData("autumn", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalise_AcceptsRouteSlugsAndRejectsAnythingElse(string? input, string? expected)
    {
        Assert.Equal(expected, AnimeSeasonCalendar.Normalise(input));
    }

    [Fact]
    public void Next_WrapsFallIntoTheFollowingWinter()
    {
        Assert.Equal((2026, "SPRING"), AnimeSeasonCalendar.Next(2026, "WINTER"));
        Assert.Equal((2026, "FALL"), AnimeSeasonCalendar.Next(2026, "SUMMER"));
        Assert.Equal((2027, "WINTER"), AnimeSeasonCalendar.Next(2026, "FALL"));
    }

    [Fact]
    public void Previous_WrapsWinterIntoThePrecedingFall()
    {
        Assert.Equal((2025, "FALL"), AnimeSeasonCalendar.Previous(2026, "WINTER"));
        Assert.Equal((2026, "SPRING"), AnimeSeasonCalendar.Previous(2026, "SUMMER"));
    }

    [Fact]
    public void SteppingForwardThenBack_ReturnsToTheStart()
    {
        var (year, season) = AnimeSeasonCalendar.Next(2026, "FALL");

        Assert.Equal((2026, "FALL"), AnimeSeasonCalendar.Previous(year, season));
    }

    [Theory]
    [InlineData(2025, "FALL", true)]
    [InlineData(2026, "WINTER", true)]
    [InlineData(2026, "SPRING", true)]
    [InlineData(2026, "SUMMER", false)]  // the current season is not historical
    [InlineData(2026, "FALL", false)]
    [InlineData(2027, "WINTER", false)]
    public void IsHistorical_MarksOnlyFinishedSeasons(int year, string season, bool expected)
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(expected, AnimeSeasonCalendar.IsHistorical(year, season, timeProvider));
    }

    [Fact]
    public void WholeYearIsRecognisedButIsNotOneOfTheSeasons()
    {
        Assert.True(AnimeSeasonCalendar.IsWholeYear("all"));
        Assert.True(AnimeSeasonCalendar.IsWholeYear(AnimeSeasonCalendar.WholeYear));
        Assert.False(AnimeSeasonCalendar.IsWholeYear("winter"));
        Assert.False(AnimeSeasonCalendar.IsWholeYear(null));

        // Seasons feeds AniList queries and must stay the four real MediaSeason values.
        Assert.DoesNotContain(AnimeSeasonCalendar.WholeYear, AnimeSeasonCalendar.Seasons);
        Assert.Contains(AnimeSeasonCalendar.WholeYear, AnimeSeasonCalendar.PickerOptions);
        Assert.Equal(5, AnimeSeasonCalendar.PickerOptions.Length);
    }

    // Without an explicit guard IndexOf returns -1 for the sentinel and the step would land on Winter.
    [Fact]
    public void SteppingAWholeYearMovesTheYearAndStaysWholeYear()
    {
        Assert.Equal((2012, "ALL"), AnimeSeasonCalendar.Next(2011, AnimeSeasonCalendar.WholeYear));
        Assert.Equal((2010, "ALL"), AnimeSeasonCalendar.Previous(2011, AnimeSeasonCalendar.WholeYear));
    }

    // A whole-year browse of the current year is never settled, so it must not take the long cache TTL.
    [Fact]
    public void TheCurrentYearAsAWholeIsNotHistorical()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero));

        Assert.False(AnimeSeasonCalendar.IsHistorical(2026, AnimeSeasonCalendar.WholeYear, timeProvider));
        Assert.True(AnimeSeasonCalendar.IsHistorical(2025, AnimeSeasonCalendar.WholeYear, timeProvider));
    }

    [Fact]
    public void ToRouteSlug_IsLowerCase()
    {
        Assert.Equal("spring", AnimeSeasonCalendar.ToRouteSlug("SPRING"));
    }
}
