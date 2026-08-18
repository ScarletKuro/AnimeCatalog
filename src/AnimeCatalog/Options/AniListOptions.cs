namespace AnimeCatalog.Options;

public sealed class AniListOptions
{
    public const string SectionName = "AniList";

    public string GraphQlUrl { get; set; } = "https://graphql.anilist.co";
}
