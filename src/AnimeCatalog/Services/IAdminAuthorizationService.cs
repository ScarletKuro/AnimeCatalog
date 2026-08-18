namespace AnimeCatalog.Services;

public interface IAdminAuthorizationService
{
    Task<bool> EnsureAdminAsync(CancellationToken cancellationToken = default);
}
