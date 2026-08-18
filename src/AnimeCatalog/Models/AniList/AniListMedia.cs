using System.Text.Json.Serialization;

namespace AnimeCatalog.Models.AniList;

public sealed class AniListMedia
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public AniListTitle Title { get; set; } = new();

    [JsonPropertyName("coverImage")]
    public AniListCoverImage? CoverImage { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("seasonYear")]
    public int? SeasonYear { get; set; }

    [JsonPropertyName("episodes")]
    public int? Episodes { get; set; }

    [JsonPropertyName("startDate")]
    public AniListFuzzyDate? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public AniListFuzzyDate? EndDate { get; set; }

    [JsonPropertyName("relations")]
    public AniListRelationConnection? Relations { get; set; }

    // Everything below is requested only by the enrichment queries. The search and details
    // queries leave these null, which is why they are all nullable or default to empty.

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("bannerImage")]
    public string? BannerImage { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    // ANIME or MANGA. Relation targets are not always anime — a SOURCE relation usually points at
    // the manga — and anilist.co/anime/{mangaId} is a 404, so outbound links must use this.
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = [];

    [JsonPropertyName("synonyms")]
    public List<string> Synonyms { get; set; } = [];

    [JsonPropertyName("averageScore")]
    public int? AverageScore { get; set; }

    [JsonPropertyName("meanScore")]
    public int? MeanScore { get; set; }

    [JsonPropertyName("popularity")]
    public int? Popularity { get; set; }

    [JsonPropertyName("favourites")]
    public int? Favourites { get; set; }

    [JsonPropertyName("countryOfOrigin")]
    public string? CountryOfOrigin { get; set; }

    [JsonPropertyName("isAdult")]
    public bool? IsAdult { get; set; }

    [JsonPropertyName("siteUrl")]
    public string? SiteUrl { get; set; }

    [JsonPropertyName("studios")]
    public AniListStudioConnection? Studios { get; set; }

    [JsonPropertyName("tags")]
    public List<AniListMediaTag> Tags { get; set; } = [];

    [JsonPropertyName("rankings")]
    public List<AniListRanking> Rankings { get; set; } = [];

    [JsonPropertyName("nextAiringEpisode")]
    public AniListNextAiringEpisode? NextAiringEpisode { get; set; }
}

public sealed class AniListTitle
{
    [JsonPropertyName("romaji")]
    public string? Romaji { get; set; }

    [JsonPropertyName("english")]
    public string? English { get; set; }

    [JsonPropertyName("native")]
    public string? Native { get; set; }
}

public sealed class AniListCoverImage
{
    [JsonPropertyName("large")]
    public string? Large { get; set; }

    [JsonPropertyName("extraLarge")]
    public string? ExtraLarge { get; set; }

    // AniList's dominant colour for the cover art, used as a per-entry accent.
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    // AniList serves the *medium* asset for `large`, so prefer extraLarge wherever the image
    // is rendered bigger than a thumbnail. AnimeSearchResult deliberately does the reverse.
    public string? BestUrl => ExtraLarge ?? Large;
}

public sealed class AniListFuzzyDate
{
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("month")]
    public int? Month { get; set; }

    [JsonPropertyName("day")]
    public int? Day { get; set; }

    public DateOnly? ToDateOnly()
    {
        if (Year is null || Month is null || Day is null)
        {
            return null;
        }

        try
        {
            return new DateOnly(Year.Value, Month.Value, Day.Value);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class AniListRelationConnection
{
    [JsonPropertyName("edges")]
    public List<AniListRelationEdge> Edges { get; set; } = [];
}

public sealed class AniListRelationEdge
{
    [JsonPropertyName("relationType")]
    public string? RelationType { get; set; }

    [JsonPropertyName("node")]
    public AniListMedia? Node { get; set; }
}

public sealed class AniListStudioConnection
{
    [JsonPropertyName("edges")]
    public List<AniListStudioEdge> Edges { get; set; } = [];
}

public sealed class AniListStudioEdge
{
    [JsonPropertyName("isMain")]
    public bool IsMain { get; set; }

    [JsonPropertyName("node")]
    public AniListStudio? Node { get; set; }
}

public sealed class AniListStudio
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("siteUrl")]
    public string? SiteUrl { get; set; }
}

public sealed class AniListMediaTag
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("rank")]
    public int? Rank { get; set; }

    [JsonPropertyName("isMediaSpoiler")]
    public bool IsMediaSpoiler { get; set; }

    [JsonPropertyName("isGeneralSpoiler")]
    public bool IsGeneralSpoiler { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    public bool IsSpoiler => IsMediaSpoiler || IsGeneralSpoiler;
}

public sealed class AniListRanking
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("allTime")]
    public bool? AllTime { get; set; }

    [JsonPropertyName("context")]
    public string? Context { get; set; }
}

public sealed class AniListNextAiringEpisode
{
    [JsonPropertyName("episode")]
    public int Episode { get; set; }

    [JsonPropertyName("airingAt")]
    public long AiringAt { get; set; }

    [JsonPropertyName("timeUntilAiring")]
    public int TimeUntilAiring { get; set; }
}
