create table if not exists public.app_settings (
    id integer primary key,
    public_catalog_enabled boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

insert into public.app_settings (id, public_catalog_enabled)
values (1, true)
on conflict (id) do nothing;

drop trigger if exists set_app_settings_updated_at on public.app_settings;
create trigger set_app_settings_updated_at
before update on public.app_settings
for each row
execute function public.set_updated_at();

create or replace function public.can_read_catalog()
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select
        coalesce(
            (
                select s.public_catalog_enabled
                from public.app_settings s
                where s.id = 1
            ),
            false
        )
        or public.is_admin();
$$;

revoke all on public.app_settings from anon, authenticated;
grant execute on function public.can_read_catalog() to anon, authenticated;
grant select, update on public.app_settings to authenticated;

alter table public.app_settings enable row level security;
alter table public.app_settings force row level security;

drop policy if exists app_settings_admin_only on public.app_settings;
create policy app_settings_admin_only
on public.app_settings
for all
to authenticated
using (public.is_admin())
with check (public.is_admin());

drop policy if exists franchises_select_public on public.franchises;
drop policy if exists anime_entries_select_public on public.anime_entries;
drop policy if exists anime_relations_select_public on public.anime_relations;
drop policy if exists catalog_entries_select_public on public.catalog_entries;

drop policy if exists franchises_select_toggle on public.franchises;
create policy franchises_select_toggle
on public.franchises
for select
to anon, authenticated
using (public.can_read_catalog());

drop policy if exists anime_entries_select_toggle on public.anime_entries;
create policy anime_entries_select_toggle
on public.anime_entries
for select
to anon, authenticated
using (public.can_read_catalog());

drop policy if exists anime_relations_select_toggle on public.anime_relations;
create policy anime_relations_select_toggle
on public.anime_relations
for select
to anon, authenticated
using (public.can_read_catalog());

drop policy if exists catalog_entries_select_toggle on public.catalog_entries;
create policy catalog_entries_select_toggle
on public.catalog_entries
for select
to anon, authenticated
using (public.can_read_catalog());
