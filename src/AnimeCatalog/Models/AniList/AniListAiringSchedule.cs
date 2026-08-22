using System.Text.Json.Serialization;

namespace AnimeCatalog.Models.AniList;

/// <summary>
/// One scheduled episode broadcast, as returned by <c>Page.airingSchedules</c>.
/// </summary>
/// <remarks>
/// This is the only source that can answer "what airs this week": <c>nextAiringEpisode</c> carries
/// just the next episode per title, so two episodes of one show in the same week would collapse into
/// one and a past week could not be shown at all.
/// </remarks>
public sealed class AniListAiringSchedule
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Unix seconds, UTC. A long to match <see cref="AniListNextAiringEpisode.AiringAt"/>, even
    /// though the <c>airingAt_greater</c>/<c>airingAt_lesser</c> filter arguments that select it are
    /// GraphQL Int - the asymmetry is real and is range-checked on the way out, not here.
    /// </summary>
    [JsonPropertyName("airingAt")]
    public long AiringAt { get; set; }

    [JsonPropertyName("timeUntilAiring")]
    public int TimeUntilAiring { get; set; }

    [JsonPropertyName("episode")]
    public int Episode { get; set; }

    [JsonPropertyName("mediaId")]
    public int MediaId { get; set; }

    /// <summary>
    /// Nullable on purpose: AniList can hold a schedule row whose media has since been deleted, and
    /// a calendar has to skip those rather than assert its way into a null reference.
    /// </summary>
    [JsonPropertyName("media")]
    public AniListMedia? Media { get; set; }

    public DateTimeOffset AiringAtUtc => DateTimeOffset.FromUnixTimeSeconds(AiringAt);
}
