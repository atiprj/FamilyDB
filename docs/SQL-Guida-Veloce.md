# SQL Guida Veloce (zero sbatti)

## Serve Azure Data Studio?
No, non e' obbligatorio.  
Puoi usare:
- Azure Data Studio (consigliato, piu' semplice),
- SQL Server Management Studio (SSMS),
- oppure `sqlcmd` da terminale.

## Connessione
- Server: `DESKTOP-A6NC714\REVITLIB`
- Database: `RevitFamilyLibrary`
- Auth: Windows (Trusted)

## Query 1 - elenco famiglie
```sql
SELECT TOP 200
  FamilyName, CategoryName, FamilyKind, SourceDiscipline, SourceModelPath, RfaPath, SourceElementTypeId, UpdatedAtUtc
FROM dbo.Families
ORDER BY UpdatedAtUtc DESC;
```

## Query 2 - struttura database (tabelle e colonne)
```sql
SELECT TABLE_SCHEMA, TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE='BASE TABLE'
ORDER BY TABLE_SCHEMA, TABLE_NAME;

SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='Families'
ORDER BY ORDINAL_POSITION;
```

## Query 3 - controlli qualita'
```sql
SELECT
  SUM(CASE WHEN FamilyKind = 'Loadable' AND (RfaPath NOT LIKE '%.rfa' OR RfaPath IS NULL) THEN 1 ELSE 0 END) AS LoadableWithoutRealRfa,
  SUM(CASE WHEN FamilyKind = 'System' AND SourceElementTypeId IS NULL THEN 1 ELSE 0 END) AS SystemWithoutTypeId,
  SUM(CASE WHEN SourceModelPath IS NULL OR LTRIM(RTRIM(SourceModelPath)) = '' THEN 1 ELSE 0 END) AS MissingSourceModelPath
FROM dbo.Families;
```

## Come aggiornare il DB dai modelli libreria (dentro Revit)
Nel menu `Family DB Tools`:
- `Sync ARC` aggiorna da modello ARC
- `Sync FUR` aggiorna da modello FUR
- `Sync ALL` aggiorna entrambi (consigliato)
- `Push libreria → DB` fa la stessa cosa di `Sync ALL` ma con nome esplicito: esporta anche `.rfa` in `C:\ProgramData\RevitFamilyDb\2025\ExportedRfa\...`, salva anteprime PNG in `...\Previews\...` e scrive i parametri in `dbo.Parameters`.

## Web → progetto Revit (coda)
1. Dalla web app: pulsante **In coda Revit** su una riga (o da **Esplora**).
2. In Revit, comando **Applica coda Web → progetto** sul progetto aperto: carica le famiglie richieste dal DB.

## Nota importante sul tuo dubbio
Il comando di aggiornamento dai modelli libreria **e' proprio** `Sync ARC/FUR/ALL`.
Se non lo vedi nel ribbon:
- verifica di aver caricato l'addin 2025 aggiornato,
- chiudi/riapri Revit dopo deploy,
- controlla che il manifest punti alla DLL corretta.
