namespace AnimeCatalog.ViewModels;

public sealed class AdminDashboardViewModel
{
    public int FranchiseCount { get; init; }
    public int AnimeEntryCount { get; init; }
    public int RelationsCount { get; init; }
    public int CompletedCount { get; init; }
    public int WatchingCount { get; init; }
    public bool PublicCatalogEnabled { get; set; }
}
