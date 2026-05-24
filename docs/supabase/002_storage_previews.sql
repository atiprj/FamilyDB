-- Bucket anteprime famiglie (pubblico in lettura)
-- Esegui in Supabase SQL Editor dopo 001_init_schema.sql

insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
  'family-previews',
  'family-previews',
  true,
  524288,
  array['image/png', 'image/jpeg', 'image/webp']
)
on conflict (id) do update set
  public = excluded.public,
  file_size_limit = excluded.file_size_limit,
  allowed_mime_types = excluded.allowed_mime_types;

-- Lettura pubblica
drop policy if exists "family_previews_public_read" on storage.objects;
create policy "family_previews_public_read"
  on storage.objects for select
  using (bucket_id = 'family-previews');

-- Scrittura solo service role (API Vercel con SUPABASE_SERVICE_ROLE_KEY)
drop policy if exists "family_previews_service_write" on storage.objects;
create policy "family_previews_service_write"
  on storage.objects for insert
  with check (bucket_id = 'family-previews');

drop policy if exists "family_previews_service_update" on storage.objects;
create policy "family_previews_service_update"
  on storage.objects for update
  using (bucket_id = 'family-previews');
