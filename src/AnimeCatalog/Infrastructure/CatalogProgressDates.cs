using AnimeCatalog.Models;

namespace AnimeCatalog.Infrastructure;

/// <summary>
/// The started/completed dates a catalog status implies.
/// </summary>
/// <remarks>
/// Both editors and the write boundary call this rather than each stamping dates by hand, the same
/// way they all share <c>EpisodePicker</c>'s status and count rules -- three copies of "Completed
/// means there is a completion date" would drift.
/// <para>
/// A pure function of the row rather than of what changed, so it is idempotent and also repairs a
/// row that was already inconsistent when it loaded. The invariant it keeps is that a completion
/// date and Completed status imply each other: the home page, the year counters and the catalog's
/// recently-completed sort all read the date and never the status, so a stale date would leave a
/// half-watched show sitting in a list of finished ones.
/// </para>
/// <para>
/// Deliberately not applied by <c>CatalogTransferService</c>: an import restores a backup verbatim,
/// and reconciling there would hand back something other than what was exported.
/// </para>
/// </remarks>
public static class CatalogProgressDates
{
    /// <summary>
    /// Today, in the frame the catalog counts in.
    /// </summary>
    /// <remarks>
    /// UTC rather than local, so a stamped date and the "this year / last 30 days" bucketing in
    /// <c>FranchiseService.BuildHomeSummary</c> cannot land on different days for the same moment.
    /// </remarks>
    public static DateOnly Today(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    /// <param name="today">
    /// The date to stamp with. Callers pass the clock's value rather than reading it here, so tests
    /// and the two editors agree on what "today" is within one interaction.
    /// </param>
    public static (DateOnly? StartedAt, DateOnly? CompletedAt) Reconcile(
        CatalogStatus status,
        DateOnly? startedAt,
        DateOnly? completedAt,
        DateOnly today) => status switch
        {
            // Planned is the one status that says nothing has happened yet, so it owns neither date.
            CatalogStatus.Planned => (null, null),

            // Starting to watch is the moment a start date exists; an entry that already carries one
            // keeps it, so passing back through Watching after a pause does not reset the date.
            CatalogStatus.Watching => (startedAt ?? today, null),

            // Finishing stamps the completion date but never invents a start date: a show added to
            // the catalog as already-finished was not started today, and a guess would read as fact.
            CatalogStatus.Completed => (startedAt, completedAt ?? today),

            // On hold and dropped both mean started-but-not-finished, so the start date stands and
            // the completion date cannot.
            _ => (startedAt, null)
        };
}
