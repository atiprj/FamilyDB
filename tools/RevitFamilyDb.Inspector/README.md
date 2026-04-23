# RevitFamilyDb Inspector

Web app locale per consultare il database `RevitFamilyLibrary`, mettere in coda famiglie per il caricamento in Revit e verificare la qualità dei dati.

## Avvio rapido

```powershell
cd "…\PluginRevitFamilyDB\tools\RevitFamilyDb.Inspector"
dotnet run
```

Oppure dalla root del plugin:

```powershell
.\run-inspector.ps1
```

Aprire l’URL mostrato in console (con `run-inspector.ps1` è tipicamente **`http://localhost:5076`**).

**Non** aprire `wwwroot\index.html` da Esplora file: il browser userebbe `file://` e le chiamate `/api/...` fallirebbero con «Not Found».

## Configurazione connection string

Ordine di priorità (allineato all’add-in Revit):

1. Variabile d’ambiente **`REVIT_FAMILY_DB_CONN`**
2. Registry Windows: **`HKCU\Software\RevitFamilyDb`**, valore **`ConnectionString`**
3. **`ConnectionStrings:RevitFamilyDb`** in `appsettings.json`
4. Fallback nel codice (solo sviluppo)

## API key sulla coda (opzionale)

In `appsettings.json`, se **`Inspector:ApiKey`** non è vuoto, gli endpoint sotto **`/api/queue/*`** richiedono autenticazione:

- header **`Authorization: Bearer <chiave>`**, oppure  
- header **`X-Api-Key: <chiave>`**

Nell’interfaccia web usare **API key coda…** per salvare la chiave nel browser (`localStorage`). Gli altri endpoint (`/api/health`, `/api/families`, ecc.) restano pubblici se non diversamente configurato.

## Cosa mostra l’interfaccia

- Stato connessione e versione app (`/api/version`)
- Qualità dati e elenco tabelle
- Tabella famiglie con filtri (disciplina, tipo Loadable/System, categoria, ricerca)
- Dettaglio famiglia e parametri; anteprime da path su disco
- Contatore coda pending e azioni **In coda Revit**

## API principali

| Metodo | Percorso | Note |
|--------|----------|------|
| GET | `/api/health` | Test DB |
| GET | `/api/version` | Prodotto e versione |
| GET | `/api/config` | Es. `queueApiKeyRequired` |
| GET | `/api/tables` | Elenco tabelle |
| GET | `/api/quality` | Contatori qualità |
| GET | `/api/families?…` | Elenco con filtri |
| GET | `/api/family/{id}` | Dettaglio + parametri |
| GET | `/api/preview?path=` | File immagine anteprima |
| POST | `/api/queue/enqueue` | Body JSON `{ "familyId": n }` |
| GET | `/api/queue/pending-count` | Richieste in attesa |

Dopo `POST /api/queue/enqueue`, in Revit eseguire il comando ribbon **Applica coda Web → progetto**.
