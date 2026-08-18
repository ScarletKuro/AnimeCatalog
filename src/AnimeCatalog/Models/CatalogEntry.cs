namespace AnimeCatalog.Models;

public sealed class CatalogEntry
{
    public long Id { get; set; }
    public long AnimeEntryId { get; set; }
    public CatalogStatus Status { get; set; }
    public decimal? Score { get; set; }
    public int EpisodesWatched { get; set; }
    public string? Notes { get; set; }
    public DateOnly? StartedAt { get; set; }
    public DateOnly? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
