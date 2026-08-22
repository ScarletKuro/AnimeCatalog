using AnimeCatalog.Components;
using AnimeCatalog.Models;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class FranchisePickerTests
{
    private static readonly Franchise[] Franchises =
    [
        new() { Id = 1, Title = "Monogatari", Slug = "monogatari" },
        new() { Id = 2, Title = "Fate", Slug = "fate" },
    ];

    [Fact]
    public void EmptyPicker_RendersNoClearAffordance()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchisePicker>(parameters => parameters
            .Add(p => p.Franchises, Franchises)
            .Add(p => p.Value, null));

        Assert.Empty(cut.FindAll(".franchise-picker__clear"));
    }

    [Fact]
    public void SelectedFranchise_RendersClearInsideTheField()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchisePicker>(parameters => parameters
            .Add(p => p.Franchises, Franchises)
            .Add(p => p.Value, 1L));

        // The adornment lives inside the field, beside the input, not as a sibling control.
        var clear = cut.Find(".franchise-picker__field .franchise-picker__clear");

        Assert.Equal("Clear franchise", clear.GetAttribute("aria-label"));
    }

    [Fact]
    public void ClickingClear_ClearsTheSelection()
    {
        using var context = new BunitContext();

        long? selectedId = 1;
        var cut = context.Render<FranchisePicker>(parameters => parameters
            .Add(p => p.Franchises, Franchises)
            .Add(p => p.Value, 1L)
            .Add(p => p.ValueChanged, value => selectedId = value));

        cut.Find(".franchise-picker__clear").Click();

        Assert.Null(selectedId);
    }

    // A query with no selection behind it is still the user's text to wipe.
    [Fact]
    public void TypedQueryWithoutSelection_IsClearable()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchisePicker>(parameters => parameters
            .Add(p => p.Franchises, Franchises)
            .Add(p => p.Value, null));

        cut.Find(".text-input").Input("mono");

        Assert.NotEmpty(cut.FindAll(".franchise-picker__option"));

        cut.Find(".franchise-picker__clear").Click();

        Assert.Equal(string.Empty, cut.Find(".text-input").GetAttribute("value"));
        Assert.Empty(cut.FindAll(".franchise-picker__option"));
        Assert.Empty(cut.FindAll(".franchise-picker__clear"));
    }
}
