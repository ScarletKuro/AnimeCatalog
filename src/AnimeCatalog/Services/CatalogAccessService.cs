using AnimeCatalog.Models.Supabase;

namespace AnimeCatalog.Services;

public sealed class CatalogAccessService : ICatalogAccessService
{
    private const int SettingsRowId = 1;

    private readonly ISupabaseRestService _supabaseRestService;
    private readonly IAdminAuthorizationService _authorizationService;

    public CatalogAccessService(
        ISupabaseRestService supabaseRestService,
        IAdminAuthorizationService authorizationService)
    {
        _supabaseRestService = supabaseRestService;
        _authorizationService = authorizationService;
    }

    public async Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
    {
        var result = await _supabaseRestService.RpcAsync<bool>("can_read_catalog", cancellationToken: cancellationToken);
        return result;
    }

    public async Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAdminOrThrowAsync(cancellationToken);

        var row = await _supabaseRestService.SelectSingleAsync<AppSettingsRow>(
            "app_settings",
            new Dictionary<string, string>
            {
                ["id"] = $"eq.{SettingsRowId}"
            },
            cancellationToken: cancellationToken);

        return row?.PublicCatalogEnabled
            ?? throw new InvalidOperationException("The app_settings row is missing. Apply the public catalog toggle patch in Supabase.");
    }

    public async Task SetPublicCatalogEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await EnsureAdminOrThrowAsync(cancellationToken);

        var row = await _supabaseRestService.UpdateSingleAsync<AppSettingsRow>(
            "app_settings",
            new Dictionary<string, string>
            {
                ["id"] = $"eq.{SettingsRowId}"
            },
            new
            {
                public_catalog_enabled = enabled
            },
            cancellationToken);

        if (row is null)
        {
            throw new InvalidOperationException("Updating app_settings returned no data.");
        }
    }

    private async Task EnsureAdminOrThrowAsync(CancellationToken cancellationToken)
    {
        if (!await _authorizationService.EnsureAdminAsync(cancellationToken))
        {
            throw new UnauthorizedAccessException("Admin access is required.");
        }
    }
}
