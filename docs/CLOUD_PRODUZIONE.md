# FamCloud - Setup Produzione (GitHub + Supabase + Vercel)

Questa guida porta il progetto in modalita cloud-first per uso multiutente.

## 1) Database Supabase

1. Apri Supabase SQL Editor.
2. Esegui `docs/supabase/001_init_schema.sql`.
3. Verifica che esistano:
   - `app.families`
   - `app.parameters`
   - `app.web_to_revit_queue`

## 2) Variabili ambiente Vercel

Configura nel progetto Vercel (Production + Preview + Development):

- `NEXT_PUBLIC_SUPABASE_URL`
- `NEXT_PUBLIC_SUPABASE_ANON_KEY`
- `SUPABASE_SERVICE_ROLE_KEY`
- `ADDIN_API_KEY`
- `NEXT_PUBLIC_APP_URL`

Nota: `ADDIN_API_KEY` protegge gli endpoint coda usati dall'addin.

## 3) Deploy web app da GitHub a Vercel

1. Collega la repo GitHub a Vercel.
2. Imposta Root Directory: `web`.
3. Build command: `npm run build`.
4. Fai deploy su Production.

Endpoint cloud principali:

- `GET /api/health`
- `GET /api/families`
- `GET /api/family/{id}`
- `POST /api/queue/enqueue`
- `GET /api/queue/pending-count`

## 4) Nuovo addin cloud (Revit 2025)

Nuovo progetto: `src/FamCloud.Addin2025`.

Manifest: `addins/FamCloud.2025.addin`.

Variabili richieste sui PC utente:

- `FAMCLOUD_API_BASE_URL` (es. `https://<project>.vercel.app`)
- `FAMCLOUD_ADDIN_API_KEY` (deve coincidere con `ADDIN_API_KEY`)

L'addin espone due comandi:

- `Health API`
- `Pending Queue`

## 5) Hardening consigliato (passo successivo)

- Migrare il core addin da SQL Server a API HTTPS o Npgsql/Supabase.
- Introdurre auth utente e RLS per isolamento multi-tenant.
- Spostare preview/rfa da path locali a Supabase Storage URL.
