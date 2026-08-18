# Anime Catalog

Public, franchise-aware anime catalog built with `.NET 10`, `Blazor WebAssembly Standalone`, `Supabase`, and `AniList`.

## Stack

- `Blazor WebAssembly Standalone`
- `Supabase` Data API + Auth
- `AniList` GraphQL API
- `GitHub Pages` + `GitHub Actions`

No ASP.NET Core hosted backend is used. The app stays fully client-side.

## Setup

1. Create a Supabase project.
2. Run [supabase/schema.sql](/C:/Users/Kuro/source/repos/AnimeCatalog/supabase/schema.sql) in the Supabase SQL Editor.
3. In Supabase Auth, enable the GitHub provider.
4. Add redirect URLs for local development and GitHub Pages.
   Local example: `https://localhost:5001/login`
   GitHub Pages example: `https://USERNAME.github.io/REPOSITORY/login`
5. Sign in once through Supabase Auth and copy your Supabase user UUID.
6. Insert that UUID into `public.app_admins`.
7. Fill in [appsettings.json](/C:/Users/Kuro/source/repos/AnimeCatalog/src/AnimeCatalog/wwwroot/appsettings.json).
8. Enable GitHub Pages in repository settings and set the source to `GitHub Actions`.
9. Push to `main` or `master`.

## Local development

```powershell
dotnet restore src/AnimeCatalog/AnimeCatalog.csproj
dotnet build src/AnimeCatalog/AnimeCatalog.csproj
dotnet run --project src/AnimeCatalog/AnimeCatalog.csproj
dotnet test src/tests/AnimeCatalog.Tests/AnimeCatalog.Tests.csproj
```

## Notes

- The app uses the Supabase publishable key only.
- Authorization is enforced by PostgreSQL RLS, not by hidden UI buttons.
- GitHub OAuth is implemented with a client-side PKCE flow against Supabase Auth.
- GitHub Pages routing is handled with `404.html` plus history replacement and a workflow step that rewrites `<base href>`.
- CI runs the test suite first; the GitHub Pages deploy only happens after the tests pass. Pull requests are built and tested but never deployed.

## License

[PolyForm Noncommercial License 1.0.0](LICENSE.md) — `PolyForm-Noncommercial-1.0.0`.

Free to use, modify, and share for any noncommercial purpose: personal and hobby
use, study, research, education, and charitable, public-health, environmental, or
government work. Commercial use requires a separate license — open an issue to ask.

This is a source-available license, not an OSI-approved open source one; the
noncommercial restriction is what puts it outside that definition.
