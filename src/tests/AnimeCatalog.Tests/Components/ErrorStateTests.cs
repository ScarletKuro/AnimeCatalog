using AnimeCatalog.Components;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class ErrorStateTests
{
    [Fact]
    public void RetryButton_RaisesCallback()
    {
        var triggered = false;
        using var context = new BunitContext();
        var cut = context.Render<ErrorState>(parameters => parameters
            .Add(p => p.Title, "Failed")
            .Add(p => p.Message, "Boom")
            .Add(p => p.Retry, () => triggered = true));

        cut.Find("button").Click();
        Assert.True(triggered);
    }
}
