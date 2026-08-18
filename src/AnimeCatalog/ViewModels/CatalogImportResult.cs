namespace AnimeCatalog.ViewModels;

public sealed class CatalogImportResult
{
    public int FranchisesCreated { get; set; }
    public int FranchisesUpdated { get; set; }
    public int EntriesCreated { get; set; }
    public int EntriesUpdated { get; set; }
    public int RelationsWritten { get; set; }

    /// <summary>
    /// One line per row that could not be imported, so a partial import is never silent.
    /// </summary>
    public List<string> Skipped { get; set; } = [];

    public int TotalWritten => EntriesCreated + EntriesUpdated;
}
