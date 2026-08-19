using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Options;
using AnimeCatalog.State;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace AnimeCatalog.Services;

public sealed class AuthService : IAccessTokenProvider, IAdminAuthorizationService, IAuthStateNotifier
{
    private const string SessionStorageKey = "animeCatalog.auth.session";
    private const string PkceVerifierStorageKey = "animeCatalog.auth.pkce.verifier";
    private const string ReturnUrlStorageKey = "animeCatalog.auth.returnUrl";

    private readonly HttpClient _httpClient;
    private readonly BrowserStorageService _browserStorageService;
    private readonly NavigationManager _navigationManager;
    private readonly SupabaseOptions _supabaseOptions;
    private readonly AppAuthenticationStateProvider _authenticationStateProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private AuthSession? _session;
    private bool _initialized;
    private bool _isAdmin;

    public AuthService(
        HttpClient httpClient,
        BrowserStorageService browserStorageService,
        NavigationManager navigationManager,
        IOptions<SupabaseOptions> supabaseOptions,
        AppAuthenticationStateProvider authenticationStateProvider)
    {
        _httpClient = httpClient;
        _browserStorageService = browserStorageService;
        _navigationManager = navigationManager;
        _supabaseOptions = supabaseOptions.Value;
        _authenticationStateProvider = authenticationStateProvider;
    }

    public event Action? StateChanged;

    public AuthSession? CurrentSession => _session;

    // The session id, not the access token: a refresh replaces the token but not the user, and
    // subscribers have to be able to tell those two apart.
    public string? CurrentUserId => _session?.User.Id;

    public bool IsAuthenticated => _session is not null;
    public bool IsAdmin => _isAdmin;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_supabaseOptions.Url) &&
        !string.IsNullOrWhiteSpace(_supabaseOptions.PublishableKey);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        if (!IsConfigured)
        {
            _authenticationStateProvider.SetSession(null, false);
            return;
        }

        var serializedSession = await _browserStorageService.GetItemAsync(SessionStorageKey);
        if (!string.IsNullOrWhiteSpace(serializedSession))
        {
            _session = JsonSerializer.Deserialize<AuthSession>(serializedSession, JsonDefaults.Web);
        }

        if (_session is not null && _session.IsExpired(DateTimeOffset.UtcNow))
        {
            _session = await RefreshSessionCoreAsync(cancellationToken);
        }

        _isAdmin = _session is not null && await CheckIsAdminAsync(_session.AccessToken, cancellationToken);
        _authenticationStateProvider.SetSession(_session, _isAdmin);
        StateChanged?.Invoke();
    }

    public async Task StartGitHubSignInAsync(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        if (!IsConfigured)
        {
            throw new InvalidOperationException("Supabase configuration is incomplete.");
        }

        var verifier = PkceUtility.CreateCodeVerifier();
        var challenge = PkceUtility.CreateCodeChallenge(verifier);
        var callbackUri = new Uri(new Uri(_navigationManager.BaseUri), "login").ToString();
        // Base-relative so the value keeps working under a sub-path deployment
        // such as GitHub Pages (https://user.github.io/Repository/).
        var targetReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
            ? _navigationManager.ToBaseRelativePath(_navigationManager.Uri)
            : returnUrl;

        await _browserStorageService.SetItemAsync(PkceVerifierStorageKey, verifier);
        await _browserStorageService.SetItemAsync(ReturnUrlStorageKey, targetReturnUrl);

        var authorizeUrl =
            $"{_supabaseOptions.Url.TrimEnd('/')}/auth/v1/authorize" +
            $"?provider=github&redirect_to={Uri.EscapeDataString(callbackUri)}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=S256";

        _navigationManager.NavigateTo(authorizeUrl, forceLoad: true);
    }

    public async Task HandleOAuthCallbackAsync(string authCode, CancellationToken cancellationToken = default)
    {
        var verifier = await _browserStorageService.GetItemAsync(PkceVerifierStorageKey);
        if (string.IsNullOrWhiteSpace(verifier))
        {
            throw new InvalidOperationException("PKCE code verifier is missing.");
        }

        using var request = CreateAuthRequest(HttpMethod.Post, "token?grant_type=pkce", new
        {
            auth_code = authCode,
            code_verifier = verifier
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Supabase OAuth callback failed: {payload}");
        }

        _session = JsonSerializer.Deserialize<AuthSession>(payload, JsonDefaults.Web)
            ?? throw new InvalidOperationException("Supabase callback returned an empty session.");

        _isAdmin = await CheckIsAdminAsync(_session.AccessToken, cancellationToken);
        await PersistSessionAsync(_session);
        await _browserStorageService.RemoveItemAsync(PkceVerifierStorageKey);
        _authenticationStateProvider.SetSession(_session, _isAdmin);
        StateChanged?.Invoke();
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (_session is not null)
        {
            using var request = CreateAuthRequest(HttpMethod.Post, "logout", body: null);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
            await _httpClient.SendAsync(request, cancellationToken);
        }

        _session = null;
        _isAdmin = false;
        await _browserStorageService.RemoveItemAsync(SessionStorageKey);
        await _browserStorageService.RemoveItemAsync(PkceVerifierStorageKey);
        _authenticationStateProvider.SetSession(null, false);
        StateChanged?.Invoke();
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null)
        {
            return null;
        }

        if (_session.IsExpired(DateTimeOffset.UtcNow))
        {
            await _refreshLock.WaitAsync(cancellationToken);
            try
            {
                if (_session is not null && _session.IsExpired(DateTimeOffset.UtcNow))
                {
                    _session = await RefreshSessionCoreAsync(cancellationToken);
                    _isAdmin = _session is not null && await CheckIsAdminAsync(_session.AccessToken, cancellationToken);
                    _authenticationStateProvider.SetSession(_session, _isAdmin);
                    StateChanged?.Invoke();
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        return _session?.AccessToken;
    }

    public async Task<bool> EnsureAdminAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        _isAdmin = await CheckIsAdminAsync(token, cancellationToken);
        _authenticationStateProvider.SetSession(_session, _isAdmin);
        return _isAdmin;
    }

    public async Task<string?> GetAndClearReturnUrlAsync()
    {
        var returnUrl = await _browserStorageService.GetItemAsync(ReturnUrlStorageKey);
        await _browserStorageService.RemoveItemAsync(ReturnUrlStorageKey);
        return returnUrl;
    }

    public Task<string?> GetStoredReturnUrlAsync() => _browserStorageService.GetItemAsync(ReturnUrlStorageKey);

    private async Task<AuthSession?> RefreshSessionCoreAsync(CancellationToken cancellationToken)
    {
        if (_session is null || string.IsNullOrWhiteSpace(_session.RefreshToken))
        {
            return null;
        }

        using var request = CreateAuthRequest(HttpMethod.Post, "token?grant_type=refresh_token", new
        {
            refresh_token = _session.RefreshToken
        });

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await LogoutAsync(cancellationToken);
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var refreshedSession = JsonSerializer.Deserialize<AuthSession>(payload, JsonDefaults.Web);

        if (refreshedSession is not null)
        {
            await PersistSessionAsync(refreshedSession);
        }

        return refreshedSession;
    }

    private async Task PersistSessionAsync(AuthSession session)
    {
        var serialized = JsonSerializer.Serialize(session, JsonDefaults.Web);
        await _browserStorageService.SetItemAsync(SessionStorageKey, serialized);
    }

    private async Task<bool> CheckIsAdminAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_supabaseOptions.Url.TrimEnd('/')}/rest/v1/rpc/is_admin");
        request.Headers.Add("apikey", _supabaseOptions.PublishableKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return bool.TryParse(payload, out var result) && result;
    }

    private HttpRequestMessage CreateAuthRequest(HttpMethod method, string relativePath, object? body)
    {
        var request = new HttpRequestMessage(method, $"{_supabaseOptions.Url.TrimEnd('/')}/auth/v1/{relativePath}");
        request.Headers.Add("apikey", _supabaseOptions.PublishableKey);

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonDefaults.Web), Encoding.UTF8, "application/json");
        }

        return request;
    }
}
