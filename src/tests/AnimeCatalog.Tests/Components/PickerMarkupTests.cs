using System.Text.RegularExpressions;
using AnimeCatalog.Components;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Components;

/// <summary>
/// A Razor comment is only a comment where content is expected. Written between two attributes it
/// compiles without complaint and is emitted as literal text into the tag. bUnit renders through
/// AngleSharp, which tolerates the junk as bogus attributes and keeps every class and aria-label
/// selector working, so the component tests stay green -- but Blazor builds the real DOM with
/// setAttribute, which rejects a name like "@*" and leaves the component unmounted. These tests
/// check the attribute names a browser would actually be handed.
/// </summary>
public sealed partial class PickerMarkupTests
{
    [GeneratedRegex(@"^[A-Za-z_:][A-Za-z0-9_:.\-]*$")]
    private static partial Regex ValidAttributeName { get; }

    [Fact]
    public void ScorePicker_EmitsOnlyRealAttributes()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScorePicker>(parameters => parameters.Add(p => p.Value, 7m));

        AssertAttributeNamesAreValid(cut);
    }

    [Fact]
    public void CatalogStatusPicker_EmitsOnlyRealAttributes()
    {
        using var context = new BunitContext();

        var cut = context.Render<CatalogStatusPicker>(parameters => parameters
            .Add(p => p.Value, CatalogStatus.Watching));

        AssertAttributeNamesAreValid(cut);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(null)]
    public async Task EpisodePicker_EmitsOnlyRealAttributes(int? max)
    {
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(sp => new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()));

        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 4)
            .Add(p => p.Max, max));

        AssertAttributeNamesAreValid(cut);
    }

    private static void AssertAttributeNamesAreValid<TComponent>(IRenderedComponent<TComponent> cut)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        var invalid = cut.FindAll("*")
            .SelectMany(element => element.Attributes)
            .Select(attribute => attribute.Name)
            .Where(name => !ValidAttributeName.IsMatch(name))
            .Distinct()
            .ToArray();

        Assert.Empty(invalid);
    }
}
