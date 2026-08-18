using AnimeCatalog.Infrastructure;

namespace AnimeCatalog.Tests;

public sealed class AniListDescriptionSanitizerTests
{
    // AniList descriptions are community-editable and the API returns them verbatim, so these are
    // the cases that decide whether rendering one as a MarkupString is safe.
    [Fact]
    public void Sanitize_NeutralizesScriptElement()
    {
        var result = AniListDescriptionSanitizer.Sanitize("Before <script>alert(1)</script> after");

        Assert.DoesNotContain("<script", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</script", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Before", result.Html);
        Assert.Contains("after", result.Html);
    }

    [Fact]
    public void Sanitize_DropsEventHandlerAttributesFromUnquotedMarkup()
    {
        // The exact shape AniList was observed to pass through unescaped.
        var result = AniListDescriptionSanitizer.Sanitize("Synopsis <img src=x onerror=alert(1)> tail");

        Assert.DoesNotContain("onerror", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Synopsis", result.Html);
        Assert.Contains("tail", result.Html);
    }

    [Fact]
    public void Sanitize_KeepsAnchorTextButDropsHref()
    {
        var result = AniListDescriptionSanitizer.Sanitize("""See <a href="javascript:alert(1)">this link</a> now""");

        Assert.DoesNotContain("javascript", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("this link", result.Html);
    }

    [Theory]
    [InlineData("<style>body{display:none}</style>Text")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>Text")]
    [InlineData("<div class='youtube' id='https://youtu.be/x'></div>Text")]
    [InlineData("<span class='markdown_spoiler'><span>hidden</span></span>Text")]
    public void Sanitize_EmitsNoTagsBeyondParagraphsAndBreaks(string raw)
    {
        var result = AniListDescriptionSanitizer.Sanitize(raw);

        foreach (var tag in ExtractTags(result.Html))
        {
            Assert.Contains(tag, new[] { "p", "/p", "br /" });
        }
    }

    [Fact]
    public void Sanitize_ConvertsSingleBreakToLineBreak()
    {
        var result = AniListDescriptionSanitizer.Sanitize("First line<br>Second line");

        Assert.Contains("<br />", result.Html);
        Assert.Contains("First line", result.Html);
        Assert.Contains("Second line", result.Html);
    }

    [Fact]
    public void Sanitize_ConvertsDoubleBreakToSeparateParagraphs()
    {
        // This is the real shape of an AniList synopsis: <br><br> plus a literal newline.
        var result = AniListDescriptionSanitizer.Sanitize("Para one. <br><br>\nPara two.");

        Assert.Equal(2, CountOccurrences(result.Html, "<p>"));
        Assert.Contains("Para one.", result.Html);
        Assert.Contains("Para two.", result.Html);
    }

    [Fact]
    public void Sanitize_StripsSpoilerMarkersButKeepsText()
    {
        var result = AniListDescriptionSanitizer.Sanitize("Plot ~!the twist!~ end");

        Assert.DoesNotContain("~!", result.Html);
        Assert.DoesNotContain("!~", result.Html);
        Assert.Contains("the twist", result.PlainText);
    }

    [Fact]
    public void Sanitize_DoesNotDoubleEncodeExistingEntities()
    {
        var result = AniListDescriptionSanitizer.Sanitize("Tom &amp; Jerry&#39;s show");

        Assert.Contains("Tom &amp; Jerry&#39;s show", result.Html);
        Assert.DoesNotContain("&amp;amp;", result.Html);
        Assert.Equal("Tom & Jerry's show", result.PlainText);
    }

    [Fact]
    public void Sanitize_EncodesStrayAngleBracketsAsText()
    {
        var result = AniListDescriptionSanitizer.Sanitize("5 < 7 and 9 > 3");

        Assert.Contains("&lt;", result.Html);
        Assert.Contains("&gt;", result.Html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<br><br>")]
    public void Sanitize_ReturnsEmptyForContentlessInput(string? raw)
    {
        var result = AniListDescriptionSanitizer.Sanitize(raw);

        Assert.False(result.HasContent);
        Assert.Equal(string.Empty, result.Html);
        Assert.Equal(string.Empty, result.PlainText);
    }

    [Fact]
    public void Sanitize_ToleratesMalformedSelfClosingTag()
    {
        // Observed in real AniList data.
        var result = AniListDescriptionSanitizer.Sanitize("Before <i/> after");

        Assert.Contains("Before", result.Html);
        Assert.Contains("after", result.Html);
    }

    [Fact]
    public void Sanitize_ToleratesUnterminatedTag()
    {
        var result = AniListDescriptionSanitizer.Sanitize("Text <b unterminated");

        Assert.Contains("Text", result.Html);
    }

    [Fact]
    public void Sanitize_RealAniListDescription_SplitsParagraphsAndEmitsOnlySafeTags()
    {
        // Verbatim from AniList media 9736 (Astarotte no Omocha!) with asHtml:false — note that the
        // API still returns <br> tags and literal newlines even in the "not HTML" mode.
        const string raw = "While job hunting, Naoya is taken by a mysterious girl to a magical land."
            + "\n<br><br>\n(Source: Anime News Network)";

        var result = AniListDescriptionSanitizer.Sanitize(raw);

        Assert.Equal(2, CountOccurrences(result.Html, "<p>"));
        Assert.Contains("While job hunting", result.Html);
        Assert.Contains("(Source: Anime News Network)", result.Html);

        foreach (var tag in ExtractTags(result.Html))
        {
            Assert.Contains(tag, new[] { "p", "/p", "br /" });
        }
    }

    [Fact]
    public void ToPlainText_StripsAllMarkup()
    {
        var plain = AniListDescriptionSanitizer.ToPlainText("<p><b>Bold</b> and <i>italic</i></p>");

        Assert.Equal("Bold and italic", plain);
    }

    [Fact]
    public void Truncate_BreaksOnWordBoundary()
    {
        var truncated = AniListDescriptionSanitizer.Truncate("The quick brown fox jumps over", 15);

        Assert.EndsWith("…", truncated);
        Assert.DoesNotContain("bro…", truncated);
        Assert.StartsWith("The quick", truncated);
    }

    [Fact]
    public void Truncate_LeavesShortTextUntouched()
    {
        Assert.Equal("Short", AniListDescriptionSanitizer.Truncate("Short", 40));
    }

    [Fact]
    public void Truncate_HardCutsSingleOverlongWord()
    {
        var truncated = AniListDescriptionSanitizer.Truncate(new string('a', 50), 10);

        Assert.EndsWith("…", truncated);
        Assert.True(truncated.Length <= 11);
    }

    private static IEnumerable<string> ExtractTags(string html)
    {
        var index = 0;
        while ((index = html.IndexOf('<', index)) >= 0)
        {
            var close = html.IndexOf('>', index);
            if (close < 0)
            {
                yield break;
            }

            yield return html[(index + 1)..close];
            index = close + 1;
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
