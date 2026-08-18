using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

public sealed class FranchiseDetailsViewModel
{
    public required Franchise Franchise { get; init; }
    public required FranchiseSummaryViewModel Summary { get; init; }

    /// <summary>Supabase-only aggregates, available on first paint.</summary>
    public required FranchiseStatsViewModel Stats { get; init; }

    /// <summary>Entries bucketed by release year, ascending, unknown years last.</summary>
    public IReadOnlyList<FranchiseTimelineGroup> Timeline { get; init; } = [];

    /// <summary>Relation targets of these entries that are not part of this franchise.</summary>
    public IReadOnlyList<RelationLinkViewModel> RelatedOutsideFranchise { get; init; } = [];
}
