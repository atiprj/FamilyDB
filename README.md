# Revit Family DB

Plugin Revit multi-versione (2023–2026) collegato a SQL Server: catalogo famiglie da modelli libreria ARC/FUR, anteprime, parametri, export `.rfa`, e **coda Web → Revit** tramite web app locale Inspector.

## Stabilizzazione passo passo (da zero)

Non aggiungere pezzi finché il passo precedente non è verificato. Checklist completa e ordine consigliato: **[`docs/STABILIZZAZIONE.md`](docs/STABILIZZAZIONE.md)**.

**Errore «Not Found» nell’Inspector:** quasi sempre la pagina è stata aperta come **file** (`index.html` da Esplora file) invece che tramite **`dotnet run`** / **`run-inspector.ps1`**. Usare solo l’URL `http://localhost:…` mostrato in console.

## Procedura operativa (ordine consigliato)

1. **SQL Server**  
   Database (es. `RevitFamilyLibrary` su istanza `REVITLIB`) raggiungibile dalla macchina dove girano Revit e l’Inspector.

2. **Configurare la connection string** (una sola volta per PC utente, se diversa dal default nel codice)  
   Priorità effettiva (stessa per add-in Revit e Inspector):
   - variabile d’ambiente **`REVIT_FAMILY_DB_CONN`** (massima priorità);
   - registry **`HKCU\Software\RevitFamilyDb`**, valore stringa **`ConnectionString`** (utile per policy senza toccare i file);
   - per l’Inspector: **`ConnectionStrings:RevitFamilyDb`** in `appsettings.json`;
   - fallback hardcoded nel progetto (solo sviluppo).

   Esempio variabile (PowerShell, poi riavviare Revit se già aperto):

   ```powershell
   setx REVIT_FAMILY_DB_CONN "Server=TUO_SERVER\REVITLIB;Database=RevitFamilyLibrary;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;"
   ```

3. **Deploy dell’add-in** (per la versione Revit in uso)  
   - Compilare in Release il progetto `RevitFamilyDb.Addin20XX`.  
   - Copiare le DLL in una cartella stabile (es. `C:\ProgramData\RevitFamilyDb\2025\`) e aggiornare il manifest `.addin` in `%AppData%\Autodesk\Revit\Addins\20XX\` con il path reale.  
   - Per 2025 esiste lo script `deploy-2025.ps1` (dopo `dotnet build` in Release).

4. **In Revit — tab `Family DB`**  
   - **Test DB**: verifica connessione.  
   - **Push libreria → DB** / **Sync ALL** (o Sync ARC / Sync FUR): legge i file libreria configurati nell’add-in, aggiorna SQL, export `.rfa`, anteprime PNG, parametri.  
   - **Elenco + Carica**: browser interno per caricare famiglie nel progetto attivo; **doppio click su una riga** attiva il modello libreria e apre il dialogo **Proprietà tipo** per quel tipo (famiglia di sistema tramite `SourceElementTypeId`, loadable tramite primo tipo simbolo).  
   - **Applica coda Web → progetto**: elabora le richieste messe in coda dall’Inspector.

5. **Inspector (browser)**  
   - Avvio: dalla cartella `tools\RevitFamilyDb.Inspector` eseguire `dotnet run` oppure `run-inspector.ps1`; aprire l’URL indicato in console (es. `http://localhost:5076`).  
   - Consultare catalogo, filtri, dettaglio parametri, anteprime.  
   - **In coda Revit** / **Metti in coda**: inserisce la famiglia nella tabella `WebToRevitQueue`; in Revit usare poi **Applica coda Web → progetto**.  
   - Se in `appsettings.json` è impostata **`Inspector:ApiKey`**, per le chiamate a `/api/queue/*` serve `Authorization: Bearer <chiave>`; nell’interfaccia usare il pulsante **API key coda…** per salvare la chiave in locale.

6. **Log add-in** (diagnostica)  
   File di testo in **`%ProgramData%\RevitFamilyDb\logs\`** (`revit-family-db-YYYYMMDD.log`): operazioni di sync e applicazione coda.

---

## Struttura repository

| Cartella | Contenuto |
|----------|-----------|
| `src/RevitFamilyDb.Core` | Repository SQL, schema esteso, coda Web, log, risoluzione connection string |
| `src/RevitFamilyDb.Addin2023` … `Addin2026` | Ribbon Revit e comandi per versione |
| `addins/*.addin` | Manifest da copiare in Addins dopo aver impostato il path assembly |
| `tools/RevitFamilyDb.Inspector` | Web app ASP.NET Core (API + UI statica) |
| `docs/` | Piano stabilizzazione, note SQL |

## Prerequisiti

- Revit con API in path standard (`C:\Program Files\Autodesk\Revit 20XX\RevitAPI.dll`).
- .NET SDK per compilare.
- SQL Server accessibile con la connection string configurata.

## Build

```powershell
cd "PluginRevitFamilyDB"
dotnet build .\RevitFamilyDb.slnx -c Release
```

## Documentazione Inspector

Dettagli avvio, API e configurazione: [`tools/RevitFamilyDb.Inspector/README.md`](tools/RevitFamilyDb.Inspector/README.md).
