using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;

namespace AnimeCatalog.Infrastructure;

public sealed class BrowserStorageService : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;

    public BrowserStorageService(IJSRuntime jsRuntime)
    {
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/auth.js").AsTask());
    }

    public async Task<string?> GetItemAsync(string key)
    {
        var module = await _moduleTask.Value;
        return await module.InvokeAsync<string?>("getItem", key);
    }

    public async Task SetItemAsync(string key, string value)
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("setItem", key, value);
    }

    public async Task RemoveItemAsync(string key)
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("removeItem", key);
    }

    public async Task ReplaceUrlAsync(string url)
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("replaceUrl", url);
    }

    public async Task ScrollElementIntoViewAsync(ElementReference element)
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("scrollElementIntoView", element);
    }

    public async Task ShowModalDialogAsync(ElementReference element)
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("showModalDialog", element);
    }

    public async Task DownloadFileAsync(string fileName, string mimeType, string content)
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("downloadFile", fileName, mimeType, content);
    }

    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            var module = await _moduleTask.Value;
            await module.DisposeAsync();
        }
    }
}
