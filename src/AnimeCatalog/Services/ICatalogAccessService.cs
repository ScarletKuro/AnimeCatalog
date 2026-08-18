namespace AnimeCatalog.Services;

public interface ICatalogAccessService
{
    Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default);

    Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default);

    Task SetPublicCatalogEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
