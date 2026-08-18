using AnimeCatalog;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Options;
using AnimeCatalog.Services;
using AnimeCatalog.State;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddOptions();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection(SupabaseOptions.SectionName));
builder.Services.Configure<AniListOptions>(builder.Configuration.GetSection(AniListOptions.SectionName));

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<BrowserStorageService>();
builder.Services.AddScoped<AppAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AppAuthenticationStateProvider>());
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IAccessTokenProvider>(sp => sp.GetRequiredService<AuthService>());
builder.Services.AddScoped<IAdminAuthorizationService>(sp => sp.GetRequiredService<AuthService>());
builder.Services.AddScoped<SupabaseRestService>();
builder.Services.AddScoped<ISupabaseRestService>(sp => sp.GetRequiredService<SupabaseRestService>());
builder.Services.AddScoped<AniListService>();
builder.Services.AddScoped<IAniListService>(sp => sp.GetRequiredService<AniListService>());
// Scoped is app-lifetime in WebAssembly, so the enrichment cache survives navigation.
builder.Services.AddScoped<AniListEnrichmentService>();
builder.Services.AddScoped<IAniListEnrichmentService>(sp => sp.GetRequiredService<AniListEnrichmentService>());
builder.Services.AddScoped<FranchiseService>();
builder.Services.AddScoped<FranchiseGapService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<ICatalogService>(sp => sp.GetRequiredService<CatalogService>());
builder.Services.AddScoped<ICatalogAccessService, CatalogAccessService>();
builder.Services.AddScoped<AdminCatalogService>();
builder.Services.AddScoped<CatalogTransferService>();

var host = builder.Build();

var authService = host.Services.GetRequiredService<AuthService>();
await authService.InitializeAsync();

await host.RunAsync();
