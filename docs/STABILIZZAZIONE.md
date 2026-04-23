# Piano di stabilizzazione (da zero, passo per passo)

Obiettivo: ogni fase è **verificabile** prima di passare alla successiva. Non aggiungere funzioni finché la fase corrente non è OK.

---

## Fase 1 — Database SQL

- [ ] SQL Server in esecuzione e istanza raggiungibile (es. `REVITLIB`).
- [ ] Database `RevitFamilyLibrary` creato (o nome concordato).
- [ ] Test da **SSMS** o `sqlcmd`: `SELECT DB_NAME();`

**Criterio di successo:** query eseguita senza errore dalla stessa macchina dove gireranno Revit e l’Inspector.

---

## Fase 2 — Connection string sul PC

Scegliere **una** modalità e annotarla per il team:

- Variabile utente `REVIT_FAMILY_DB_CONN`, oppure  
- Registry `HKCU\Software\RevitFamilyDb` → valore `ConnectionString`.

**Criterio di successo:** la stringa punta al DB della Fase 1 (server, database, autenticazione corretti).

---

## Fase 3 — Web Inspector (solo salute)

1. Cartella `tools\RevitFamilyDb.Inspector`
2. `dotnet run` (oppure `.\run-inspector.ps1` dalla root del plugin)
3. Aprire **solo** l’URL `http://localhost:...` stampato in console (non aprire `index.html` da Esplora file).

**Criterio di successo:** in pagina compare “Connesso” e il nome del database. Se vedi “Not Found” o errori strani, quasi sempre la pagina è stata aperta come **file locale** (`file://`) oppure il processo `dotnet run` non è quello giusto.

Verifica opzionale da terminale (con il server avviato):

```powershell
Invoke-RestMethod http://localhost:5076/api/health
```

(Sostituisci la porta se la console ne mostra un’altra.)

---

## Fase 4 — Dati in tabella `dbo.Families`

- [ ] Revit installato, add-in deployato e tab **Family DB** visibile.
- [ ] Comando **Test DB** → OK.
- [ ] Comando **Push libreria → DB** (o Sync) eseguito almeno una volta su un modello libreria configurato.

**Criterio di successo:** nell’Inspector, tabella famiglie con righe; anteprime/parametri dopo sync completo.

---

## Fase 5 — Coda Web → Revit

- [ ] Tabella `WebToRevitQueue` presente (creata dall’add-in con **EnsureExtendedSchema**).
- [ ] Da Inspector: **In coda Revit** su una famiglia.
- [ ] In Revit: **Applica coda Web → progetto** → famiglia caricata.

**Criterio di successo:** stato coda “Done” e famiglia nel progetto.

---

## Se qualcosa si rompe

| Sintomo | Cosa controllare |
|--------|-------------------|
| `Not Found` / pagina vuota in alto | Pagina aperta come file (`file://`) invece che tramite `dotnet run`. |
| Errore connessione SQL | Connection string, firewall SQL, `TrustServerCertificate` se serve. |
| Inspector senza righe | Sync da Revit non ancora eseguito o DB vuoto. |

---

## Ordine consigliato in sintesi

**SQL → connection string → `dotnet run` Inspector → Test DB Revit → Push libreria → coda e Applica.**
