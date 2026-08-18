using System.Globalization;

namespace AnimeCatalog.Infrastructure;

/// <summary>
/// Turns AniList's SCREAMING_SNAKE_CASE enum values and raw counts into display text.
/// </summary>
/// <remarks>
/// Every method tolerates unknown input: AniList adds enum members over time and some of these
/// values also arrive straight out of Supabase, so nothing here throws on an unexpected string.
/// </remarks>
public static class MediaDisplayFormatter
{
    public static string FormatLabel(string? format) => string.IsNullOrWhiteSpace(format)
        ? "Unknown"
        : format.Trim().ToUpperInvariant() switch
        {
            "TV" => "TV",
            "TV_SHORT" => "TV Short",
            "MOVIE" => "Movie",
            "SPECIAL" => "Special",
            "OVA" => "OVA",
            "ONA" => "ONA",
            "MUSIC" => "Music",
            "MANGA" => "Manga",
            "NOVEL" => "Novel",
            "ONE_SHOT" => "One shot",
            _ => Humanize(format)
        };

    public static string SeasonLabel(string? season) => string.IsNullOrWhiteSpace(season)
        ? "Unknown"
        : season.Trim().ToUpperInvariant() switch
        {
            "WINTER" => "Winter",
            "SPRING" => "Spring",
            "SUMMER" => "Summer",
            "FALL" => "Fall",
            _ => Humanize(season)
        };

    public static string AiringStatusLabel(string? status) => string.IsNullOrWhiteSpace(status)
        ? "Unknown"
        : status.Trim().ToUpperInvariant() switch
        {
            "FINISHED" => "Finished",
            "RELEASING" => "Releasing",
            "NOT_YET_RELEASED" => "Not yet released",
            "CANCELLED" => "Cancelled",
            "HIATUS" => "Hiatus",
            _ => Humanize(status)
        };

    public static string? SourceLabel(string? source) => string.IsNullOrWhiteSpace(source)
        ? null
        : source.Trim().ToUpperInvariant() switch
        {
            "ORIGINAL" => "Original",
            "MANGA" => "Manga",
            "LIGHT_NOVEL" => "Light novel",
            "VISUAL_NOVEL" => "Visual novel",
            "VIDEO_GAME" => "Video game",
            "NOVEL" => "Novel",
            "DOUJINSHI" => "Doujinshi",
            "ANIME" => "Anime",
            "WEB_NOVEL" => "Web novel",
            "LIVE_ACTION" => "Live action",
            "GAME" => "Game",
            "COMIC" => "Comic",
            "MULTIMEDIA_PROJECT" => "Multimedia project",
            "PICTURE_BOOK" => "Picture book",
            _ => Humanize(source)
        };

    public static string CountryLabel(string? countryCode) => string.IsNullOrWhiteSpace(countryCode)
        ? "Unknown"
        : countryCode.Trim().ToUpperInvariant() switch
        {
            "JP" => "Japan",
            "KR" => "South Korea",
            "CN" => "China",
            "TW" => "Taiwan",
            "US" => "United States",
            _ => countryCode.Trim().ToUpperInvariant()
        };

    public static string? DurationLabel(int? minutes) => minutes is null or <= 0
        ? null
        : $"{minutes} min";

    /// <summary>Formats a total episode runtime as "18h 20m", or "45m" under an hour.</summary>
    public static string? RuntimeLabel(int? totalMinutes)
    {
        if (totalMinutes is null or <= 0)
        {
            return null;
        }

        var hours = totalMinutes.Value / 60;
        var minutes = totalMinutes.Value % 60;

        if (hours == 0)
        {
            return $"{minutes}m";
        }

        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }

    /// <summary>Compacts large community counts: 999, 1.2K, 1.2M.</summary>
    public static string CountLabel(int? value)
    {
        if (value is null)
        {
            return "n/a";
        }

        var number = value.Value;

        return number switch
        {
            >= 1_000_000 => (number / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M",
            >= 1_000 => (number / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "K",
            _ => number.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Combines format and year into the one-line subtitle used on poster tiles.</summary>
    public static string? FormatAndYear(string? format, int? year)
    {
        var hasFormat = !string.IsNullOrWhiteSpace(format);

        return (hasFormat, year) switch
        {
            (true, not null) => $"{FormatLabel(format)} · {year}",
            (true, null) => FormatLabel(format),
            (false, not null) => year!.Value.ToString(CultureInfo.InvariantCulture),
            _ => null
        };
    }

    /// <summary>Renders an AniList ranking as "#3 highest rated all time".</summary>
    public static string RankingLabel(int rank, string? context, int? year, bool allTime)
    {
        var scope = allTime ? "all time" : year?.ToString(CultureInfo.InvariantCulture);
        var label = string.IsNullOrWhiteSpace(context) ? "ranked" : context.Trim();

        return string.IsNullOrWhiteSpace(scope) ? $"#{rank} {label}" : $"#{rank} {label} {scope}";
    }

    /// <summary>Orders AniList seasons so a franchise timeline reads in release order.</summary>
    public static int SeasonSortKey(string? season) => season?.Trim().ToUpperInvariant() switch
    {
        "WINTER" => 0,
        "SPRING" => 1,
        "SUMMER" => 2,
        "FALL" => 3,
        _ => 4
    };

    private static string Humanize(string value)
    {
        var words = value
            .Trim()
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return "Unknown";
        }

        var result = words.Select(static (word, index) =>
        {
            // Short all-caps tokens are acronyms (OVA, ONA, TV) and must not be lower-cased.
            if (word.Length <= 3 && word.All(char.IsUpper))
            {
                return word;
            }

            return index == 0
                ? char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()
                : word.ToLowerInvariant();
        });

        return string.Join(' ', result);
    }
}
