using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

public sealed class CatalogFilters
{
    public string Query { get; set; } = string.Empty;
    public CatalogStatus? Status { get; set; }
    public CatalogSortOption Sort { get; set; } = CatalogSortOption.Title;
}
