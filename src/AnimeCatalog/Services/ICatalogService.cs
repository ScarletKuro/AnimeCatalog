using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Services;

public interface ICatalogService
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<FranchiseSummaryViewModel>> GetCatalogAsync(CatalogFilters? filters = null, CancellationToken cancellationToken = default);

    Task<HomeSummaryViewModel> GetHomeSummaryAsync(CancellationToken cancellationToken = default);

    Task<FranchiseDetailsViewModel?> GetFranchiseAsync(string slug, CancellationToken cancellationToken = default);

    Task<AnimeDetailsViewModel?> GetAnimeDetailsAsync(long id, CancellationToken cancellationToken = default);

    Task<AdminDashboardViewModel> GetAdminDashboardAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Models.Franchise>> GetFranchisesAsync(CancellationToken cancellationToken = default);

    Task<AnimeEditorModel?> GetEditorModelAsync(long id, CancellationToken cancellationToken = default);

    Task<RepositorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
