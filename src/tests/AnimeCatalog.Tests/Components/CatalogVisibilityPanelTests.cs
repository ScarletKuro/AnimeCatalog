using AnimeCatalog.Components;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Components;

public sealed class CatalogVisibilityPanelTests
{
    [Fact]
    public async Task PrivateState_RendersALockedSwitch()
    {
        await using var context = CreateContext(out _);

        var cut = context.Render<CatalogVisibilityPanel>(parameters => parameters.Add(p => p.Enabled, false));

        var toggle = cut.Find(".visibility-switch");
        Assert.Equal("switch", toggle.GetAttribute("role"));
        Assert.Equal("false", toggle.GetAttribute("aria-checked"));
        Assert.Contains("visibility-switch--private", toggle.ClassList);
        Assert.Contains("Private", cut.Markup);
        Assert.Contains("Admins only", cut.Markup);
    }

    [Fact]
    public async Task PublicState_RendersAnOpenSwitch()
    {
        await using var context = CreateContext(out _);

        var cut = context.Render<CatalogVisibilityPanel>(parameters => parameters.Add(p => p.Enabled, true));

        var toggle = cut.Find(".visibility-switch");
        Assert.Equal("true", toggle.GetAttribute("aria-checked"));
        Assert.Contains("visibility-switch--public", toggle.ClassList);
        Assert.Contains("Anyone can read the catalog", cut.Markup);
    }

    [Fact]
    public async Task GoingPublic_AsksForConfirmationBeforeWriting()
    {
        await using var context = CreateContext(out var access);

        var cut = context.Render<CatalogVisibilityPanel>(parameters => parameters.Add(p => p.Enabled, false));

        cut.Find(".visibility-switch").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".confirm-dialog")));
        Assert.Empty(access.Writes);
    }

    [Fact]
    public async Task CancellingTheConfirmation_LeavesTheCatalogPrivate()
    {
        await using var context = CreateContext(out var access);

        var cut = context.Render<CatalogVisibilityPanel>(parameters => parameters.Add(p => p.Enabled, false));

        cut.Find(".visibility-switch").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".confirm-dialog")));

        cut.Find(".confirm-dialog .button--ghost").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".confirm-dialog")));
        Assert.Empty(access.Writes);
    }

    [Fact]
    public async Task ConfirmingTheConfirmation_WritesAndReportsTheNewState()
    {
        await using var context = CreateContext(out var access);

        var changes = new List<bool>();
        var cut = context.Render<CatalogVisibilityPanel>(parameters => parameters
            .Add(p => p.Enabled, false)
            .Add(p => p.EnabledChanged, enabled => changes.Add(enabled)));

        cut.Find(".visibility-switch").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".confirm-dialog")));

        cut.Find(".confirm-dialog .button--danger").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".confirm-dialog"));
            Assert.Equal<bool>([true], access.Writes);
            Assert.Equal<bool>([true], changes);
            Assert.Contains("Public catalog access enabled.", cut.Markup);
        });
    }

    [Fact]
    public async Task GoingPrivate_WritesImmediatelyWithoutConfirmation()
    {
        await using var context = CreateContext(out var access);

        var changes = new List<bool>();
        var cut = context.Render<CatalogVisibilityPanel>(parameters => parameters
            .Add(p => p.Enabled, true)
            .Add(p => p.EnabledChanged, enabled => changes.Add(enabled)));

        cut.Find(".visibility-switch").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".confirm-dialog"));
            Assert.Equal<bool>([false], access.Writes);
            Assert.Equal<bool>([false], changes);
            Assert.Contains("Public catalog access disabled.", cut.Markup);
        });
    }

    [Fact]
    public async Task AFailedWrite_ReportsTheErrorInline()
    {
        await using var context = CreateContext(out var access);
        access.Failure = new UnauthorizedAccessException("Admin access is required.");

        var changes = new List<bool>();
        var cut = context.Render<CatalogVisibilityPanel>(parameters => parameters
            .Add(p => p.Enabled, true)
            .Add(p => p.EnabledChanged, enabled => changes.Add(enabled)));

        cut.Find(".visibility-switch").Click();

        cut.WaitForAssertion(() =>
        {
            var feedback = cut.Find(".action-feedback");
            Assert.Contains("action-feedback--error", feedback.ClassList);
            Assert.Contains("Admin access is required.", feedback.TextContent);
        });

        Assert.Empty(changes);
    }

    [Fact]
    public async Task TheConfirmation_OpensAsANativeModalDialog()
    {
        await using var context = CreateContext(out _);
        var module = context.JSInterop.SetupModule("./js/auth.js");

        var cut = context.Render<CatalogVisibilityPanel>(parameters => parameters.Add(p => p.Enabled, false));

        module.VerifyNotInvoke("showModalDialog");

        cut.Find(".visibility-switch").Click();

        // showModal() is what puts the dialog in the top layer; a plain fixed-position div sat at
        // z-index: auto and lost to any .panel later in the DOM.
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("dialog", cut.Find(".confirm-dialog").TagName.ToLowerInvariant());
            module.VerifyInvoke("showModalDialog");
        });
    }

    private static BunitContext CreateContext(out RecordingCatalogAccessService access)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        access = new RecordingCatalogAccessService();
        context.Services.AddSingleton<ICatalogAccessService>(access);
        context.Services.AddSingleton(sp => new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()));
        return context;
    }

    private sealed class RecordingCatalogAccessService : ICatalogAccessService
    {
        public List<bool> Writes { get; } = [];

        public Exception? Failure { get; set; }

        public Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task SetPublicCatalogEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Writes.Add(enabled);
            return Task.CompletedTask;
        }
    }
}
