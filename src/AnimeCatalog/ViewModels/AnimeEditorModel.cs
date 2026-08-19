using System.ComponentModel.DataAnnotations;
using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

public sealed class AnimeEditorModel : IValidatableObject
{
    public long? AnimeEntryId { get; set; }
    public int AniListId { get; set; }
    public long? CatalogEntryId { get; set; }
    public long? FranchiseId { get; set; }
    public string TitleRomaji { get; set; } = string.Empty;
    public string? TitleEnglish { get; set; }
    public string? TitleNative { get; set; }
    public string? CoverUrl { get; set; }
    public string? Format { get; set; }
    public string? Season { get; set; }
    public int? SeasonYear { get; set; }
    public int? Episodes { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? SeasonNumber { get; set; }
    public int? PartNumber { get; set; }
    public int DisplayOrder { get; set; }
    public CatalogStatus Status { get; set; } = CatalogStatus.Planned;
    public decimal? Score { get; set; }
    public int EpisodesWatched { get; set; }
    public string? Notes { get; set; }
    public DateOnly? StartedAt { get; set; }
    public DateOnly? CompletedAt { get; set; }
    public FranchiseAssignmentMode FranchiseAssignmentMode { get; set; } = FranchiseAssignmentMode.None;
    public string NewFranchiseTitle { get; set; } = string.Empty;
    public string? NewFranchiseDescription { get; set; }
    public string? NewFranchiseCoverUrl { get; set; }
    public string? SuggestedFranchiseTitle { get; set; }
    public string? SuggestedNewFranchiseTitle { get; set; }
    public List<RelatedAnimeSuggestion> RelatedSuggestions { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Score is < 0 or > 10)
        {
            yield return new ValidationResult("Score must be between 0 and 10.", [nameof(Score)]);
        }

        if (EpisodesWatched < 0)
        {
            yield return new ValidationResult("Episodes watched cannot be negative.", [nameof(EpisodesWatched)]);
        }

        if (Episodes is not null && EpisodesWatched > Episodes.Value)
        {
            yield return new ValidationResult("Episodes watched cannot exceed the total episode count.", [nameof(EpisodesWatched)]);
        }

        // Watching every episode is what Completed means, so the two cannot disagree. Picking the
        // last episode promotes the status, so the UI cannot produce this -- it catches rows saved
        // before that rule, and makes one of the two give way on the way out.
        if (Episodes is not null && Status != CatalogStatus.Completed && EpisodesWatched == Episodes.Value)
        {
            yield return new ValidationResult(
                "Every episode is watched, so the status should be Completed.",
                [nameof(EpisodesWatched)]);
        }

        if (FranchiseAssignmentMode == FranchiseAssignmentMode.CreateNew &&
            string.IsNullOrWhiteSpace(NewFranchiseTitle))
        {
            yield return new ValidationResult("New franchise title is required when creating a franchise.", [nameof(NewFranchiseTitle)]);
        }
    }
}
