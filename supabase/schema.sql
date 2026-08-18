create or replace function public.set_updated_at()
returns trigger
language plpgsql
as $$
begin
    new.updated_at = now();
    return new;
end;
$$;

create table if not exists public.franchises (
    id bigint generated always as identity primary key,
    title text not null,
    slug text not null unique,
    cover_url text,
    description text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table if not exists public.anime_entries (
    id bigint generated always as identity primary key,
    anilist_id integer not null unique,
    franchise_id bigint null
        references public.franchises(id)
        on delete set null,
    title_romaji text not null,
    title_english text,
    title_native text,
    cover_url text,
    format text,
    season text,
    season_year integer,
    episodes integer,
    start_date date,
    end_date date,
    season_number integer null,
    part_number integer null,
    display_order integer not null default 0,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table if not exists public.anime_relations (
    id bigint generated always as identity primary key,
    source_anime_id bigint not null
        references public.anime_entries(id)
        on delete cascade,
    target_anilist_id integer not null,
    relation_type text not null,
    unique(source_anime_id, target_anilist_id, relation_type)
);

create table if not exists public.catalog_entries (
    id bigint generated always as identity primary key,
    anime_entry_id bigint not null unique
        references public.anime_entries(id)
        on delete cascade,
    status text not null,
    score numeric(3,1),
    episodes_watched integer not null default 0,
    notes text,
    started_at date,
    completed_at date,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint catalog_status_check
        check (status in ('planned', 'watching', 'completed', 'on_hold', 'dropped')),
    constraint score_check
        check (score is null or (score >= 0 and score <= 10)),
    constraint progress_check
        check (episodes_watched >= 0)
);

create table if not exists public.app_admins (
    user_id uuid primary key
        references auth.users(id)
        on delete cascade
);

drop trigger if exists set_franchises_updated_at on public.franchises;
create trigger set_franchises_updated_at
before update on public.franchises
for each row
execute function public.set_updated_at();

drop trigger if exists set_anime_entries_updated_at on public.anime_entries;
create trigger set_anime_entries_updated_at
before update on public.anime_entries
for each row
execute function public.set_updated_at();

drop trigger if exists set_catalog_entries_updated_at on public.catalog_entries;
create trigger set_catalog_entries_updated_at
before update on public.catalog_entries
for each row
execute function public.set_updated_at();

create or replace function public.is_admin()
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select exists (
        select 1
        from public.app_admins
        where user_id = auth.uid()
    );
$$;

revoke all on public.franchises from anon, authenticated;
revoke all on public.anime_entries from anon, authenticated;
revoke all on public.anime_relations from anon, authenticated;
revoke all on public.catalog_entries from anon, authenticated;
revoke all on public.app_admins from anon, authenticated;

grant usage on schema public to anon, authenticated;
grant execute on function public.is_admin() to anon, authenticated;

grant select on public.franchises to anon, authenticated;
grant select on public.anime_entries to anon, authenticated;
grant select on public.anime_relations to anon, authenticated;
grant select on public.catalog_entries to anon, authenticated;

grant insert, update, delete on public.franchises to authenticated;
grant insert, update, delete on public.anime_entries to authenticated;
grant insert, update, delete on public.anime_relations to authenticated;
grant insert, update, delete on public.catalog_entries to authenticated;

alter table public.franchises enable row level security;
alter table public.anime_entries enable row level security;
alter table public.anime_relations enable row level security;
alter table public.catalog_entries enable row level security;
alter table public.app_admins enable row level security;

alter table public.franchises force row level security;
alter table public.anime_entries force row level security;
alter table public.anime_relations force row level security;
alter table public.catalog_entries force row level security;
alter table public.app_admins force row level security;

drop policy if exists franchises_select_public on public.franchises;
create policy franchises_select_public
on public.franchises
for select
to anon, authenticated
using (true);

drop policy if exists franchises_modify_admin on public.franchises;
create policy franchises_modify_admin
on public.franchises
for all
to authenticated
using (public.is_admin())
with check (public.is_admin());

drop policy if exists anime_entries_select_public on public.anime_entries;
create policy anime_entries_select_public
on public.anime_entries
for select
to anon, authenticated
using (true);

drop policy if exists anime_entries_modify_admin on public.anime_entries;
create policy anime_entries_modify_admin
on public.anime_entries
for all
to authenticated
using (public.is_admin())
with check (public.is_admin());

drop policy if exists anime_relations_select_public on public.anime_relations;
create policy anime_relations_select_public
on public.anime_relations
for select
to anon, authenticated
using (true);

drop policy if exists anime_relations_modify_admin on public.anime_relations;
create policy anime_relations_modify_admin
on public.anime_relations
for all
to authenticated
using (public.is_admin())
with check (public.is_admin());

drop policy if exists catalog_entries_select_public on public.catalog_entries;
create policy catalog_entries_select_public
on public.catalog_entries
for select
to anon, authenticated
using (true);

drop policy if exists catalog_entries_modify_admin on public.catalog_entries;
create policy catalog_entries_modify_admin
on public.catalog_entries
for all
to authenticated
using (public.is_admin())
with check (public.is_admin());
