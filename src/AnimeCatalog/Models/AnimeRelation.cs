namespace AnimeCatalog.Models;

public sealed class AnimeRelation
{
    public long Id { get; set; }
    public long SourceAnimeId { get; set; }
    public int TargetAniListId { get; set; }
    public string RelationType { get; set; } = string.Empty;
}
