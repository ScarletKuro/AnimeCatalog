using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;

namespace AnimeCatalog.Tests;

public sealed class CatalogProgressDatesTests
{
    private static readonly DateOnly Today = new(2026, 8, 31);
    private static readonly DateOnly Earlier = new(2024, 3, 4);

    [Fact]
    public void CompletedWithoutADate_IsStampedWithToday()
    {
        var (_, completedAt) = CatalogProgressDates.Reconcile(CatalogStatus.Completed, null, null, Today);

        Assert.Equal(Today, completedAt);
    }

    // The point of the editable field: a date typed by hand, or restored from a backup, is an answer
    // the rule has no business improving on.
    [Fact]
    public void CompletedWithADate_KeepsIt()
    {
        var (_, completedAt) = CatalogProgressDates.Reconcile(CatalogStatus.Completed, null, Earlier, Today);

        Assert.Equal(Earlier, completedAt);
    }

    // A show added to the catalog as already-finished was not started today, and the rule will not
    // guess. Started stays whatever it was -- usually nothing.
    [Fact]
    public void Completed_NeverInventsAStartDate()
    {
        var (startedAt, _) = CatalogProgressDates.Reconcile(CatalogStatus.Completed, null, null, Today);

        Assert.Null(startedAt);
    }

    [Fact]
    public void Completed_LeavesAnExistingStartDateAlone()
    {
        var (startedAt, _) = CatalogProgressDates.Reconcile(CatalogStatus.Completed, Earlier, null, Today);

        Assert.Equal(Earlier, startedAt);
    }

    [Fact]
    public void WatchingWithoutAStartDate_IsStampedWithToday()
    {
        var (startedAt, completedAt) = CatalogProgressDates.Reconcile(CatalogStatus.Watching, null, null, Today);

        Assert.Equal(Today, startedAt);
        Assert.Null(completedAt);
    }

    // Coming back to a show after a pause is not starting it again.
    [Fact]
    public void WatchingWithAStartDate_KeepsIt()
    {
        var (startedAt, _) = CatalogProgressDates.Reconcile(CatalogStatus.Watching, Earlier, null, Today);

        Assert.Equal(Earlier, startedAt);
    }

    // The rule the home page depends on: the recently-completed list, the year counters and the
    // catalog's completion sort all read the date and never the status, so a date left behind by a
    // status change would show a half-watched entry among the finished ones.
    [Theory]
    [InlineData(CatalogStatus.Planned)]
    [InlineData(CatalogStatus.Watching)]
    [InlineData(CatalogStatus.OnHold)]
    [InlineData(CatalogStatus.Dropped)]
    public void AnyStatusButCompleted_HasNoCompletionDate(CatalogStatus status)
    {
        var (_, completedAt) = CatalogProgressDates.Reconcile(status, Earlier, Today, Today);

        Assert.Null(completedAt);
    }

    // Nothing has happened yet, so neither date has anything to describe.
    [Fact]
    public void Planned_ClearsBothDates()
    {
        var (startedAt, completedAt) = CatalogProgressDates.Reconcile(CatalogStatus.Planned, Earlier, Today, Today);

        Assert.Null(startedAt);
        Assert.Null(completedAt);
    }

    [Theory]
    [InlineData(CatalogStatus.OnHold)]
    [InlineData(CatalogStatus.Dropped)]
    public void PausedAndAbandoned_KeepTheStartDateWithoutStampingOne(CatalogStatus status)
    {
        Assert.Equal(Earlier, CatalogProgressDates.Reconcile(status, Earlier, null, Today).StartedAt);
        Assert.Null(CatalogProgressDates.Reconcile(status, null, null, Today).StartedAt);
    }

    // A function of the row rather than of what changed, so running it twice -- as both editors and
    // the service now do on one save -- cannot keep moving the dates.
    [Theory]
    [InlineData(CatalogStatus.Planned)]
    [InlineData(CatalogStatus.Watching)]
    [InlineData(CatalogStatus.Completed)]
    [InlineData(CatalogStatus.OnHold)]
    [InlineData(CatalogStatus.Dropped)]
    public void ApplyingTheRuleTwice_ChangesNothing(CatalogStatus status)
    {
        var once = CatalogProgressDates.Reconcile(status, Earlier, Earlier, Today);
        var twice = CatalogProgressDates.Reconcile(status, once.StartedAt, once.CompletedAt, Today.AddDays(1));

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Today_IsTheClocksUtcDate()
    {
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 31, 23, 30, 0, TimeSpan.Zero),
            FixedTimeProvider.EuropeanStyleZone);

        Assert.Equal(new DateOnly(2026, 8, 31), CatalogProgressDates.Today(clock));
    }
}
