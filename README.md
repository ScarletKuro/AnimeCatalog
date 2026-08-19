# Anime Catalog

A personal anime watch list: track what you have **watched**, what you are
**watching**, what is **planned**, plus what you put **on hold** or **dropped**.
Each entry keeps its own progress — episodes watched, a 1–10 score, start and
finish dates, and free-form notes — and the whole catalog is published as a page
other people can browse, or kept private with a single switch.

It is franchise-aware: sequels, prequels, spin-offs, movies, and OVAs stay
separate entries grouped under one franchise, instead of being flattened into a
fake "season" model. A **Watch next** view uses AniList relations to surface
titles from franchises you have already watched but never added.

You are the only one who edits it. Sign-in is GitHub OAuth through Supabase, a
single admin user owns the catalog, and everyone else reads.

## What you can do

- **Track status** — `Planned`, `Watching`, `Completed`, `On Hold`, `Dropped`.
- **Track progress** — episodes watched per entry; picking the last episode
  completes the entry automatically.
- **Score** — 1–10 per entry, click the same score again to clear it.
- **Record dates and notes** — started, completed, and anything worth
  remembering about the show.
- **Search and filter** — by romaji or English title, by status, sorted by
  title, score, recently added, or recently completed.
- **Group into franchises** — a franchise page with a timeline, per-franchise
  stats, and the entries you have not watched yet.
- **Find what is next** — the `Watch next` page ranks unwatched franchise
  entries by AniList score.
- **Pull metadata from AniList** — titles, posters, banners, episode counts, and
  descriptions come from the AniList GraphQL API.
- **Publish or hide** — flip the whole catalog between public and private.
- **Back up and restore** — export as portable JSON and merge a backup back in;
  importing never deletes.

Built with `.NET 10`, `Blazor WebAssembly Standalone`, `Supabase`, and `AniList`.

## Stack

- `Blazor WebAssembly Standalone`
- `Supabase` Data API + Auth
- `AniList` GraphQL API
- `GitHub Pages` + `GitHub Actions`

No ASP.NET Core hosted backend is used. The app stays fully client-side.

## Setup

### 1. Supabase project

1. Create a Supabase project.
2. Run [supabase/schema.v2.sql](supabase/schema.v2.sql) in the Supabase SQL Editor.
   `schema.v2.sql` is the current, complete schema — it already includes the
   `supabase/patch-*.sql` files. Use `schema.sql` only for an older deployment
   that still needs the patches applied one by one.

### 2. GitHub OAuth App

Supabase does not create the OAuth application for you. You must register it
yourself.

1. Go to [github.com/settings/developers](https://github.com/settings/developers) →
   **OAuth Apps** → **New OAuth App**.
   Register an **OAuth App**, not a GitHub App — the two are different and only
   the OAuth App works here.
2. Fill in:
   - **Application name**: anything, for example `Anime Catalog`.
   - **Homepage URL**: your site, for example `https://USERNAME.github.io/REPOSITORY/`.
   - **Authorization callback URL**: the Supabase callback, exactly
     `https://PROJECT_REF.supabase.co/auth/v1/callback`.
     This is the Supabase URL, not your site URL. Supabase shows the exact
     value on the GitHub provider page in step 3.
3. Create the app, then copy the **Client ID** and generate a **Client secret**.
   The secret is shown once — copy it now.

For local development you can reuse the same OAuth App, because the browser is
always redirected to Supabase first and Supabase redirects back to you.

### 3. Supabase Auth — GitHub provider

1. In Supabase, go to **Authentication** → **Sign In / Providers** → **GitHub**.
2. Enable it and paste the **Client ID** and **Client secret** from step 2.
3. Save.

### 4. Supabase Auth — URL Configuration

Enabling the provider is not enough. The redirect targets must be allow-listed
separately, otherwise sign-in bounces back to the Supabase site URL and the app
never receives the code.

1. In Supabase, go to **Authentication** → **URL Configuration**.
2. Set **Site URL** to your deployed site, for example
   `https://USERNAME.github.io/REPOSITORY/`.
3. Under **Redirect URLs**, add every origin that signs in. The app always
   returns to the `/login` route:
   - `https://localhost:7227/login` (local `https` profile)
   - `http://localhost:5250/login` (local `http` profile)
   - `https://USERNAME.github.io/REPOSITORY/login` (GitHub Pages)

   The local ports come from
   [launchSettings.json](src/AnimeCatalog/Properties/launchSettings.json); change
   them here if you change them there.

### 5. Admin user and app configuration

1. Sign in once through the app and copy your Supabase user UUID
   (**Authentication** → **Users**).
2. Insert that UUID into `public.app_admins`.
3. Fill in [appsettings.json](src/AnimeCatalog/wwwroot/appsettings.json) with the
   Supabase project URL and publishable key.

### 6. Deploy

1. Enable GitHub Pages in repository settings and set the source to `GitHub Actions`.
2. Push to `main` or `master`.

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
