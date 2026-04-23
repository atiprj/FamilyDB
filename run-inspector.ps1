$ErrorActionPreference = "Stop"

$appDir = "F:\Documenti Utili\BANCA DATI REVIT PROGETTI\Famiglie\Database Famiglie\PluginRevitFamilyDB\tools\RevitFamilyDb.Inspector"
if (-not (Test-Path $appDir)) {
    throw "Cartella inspector non trovata: $appDir"
}

Push-Location $appDir
try {
    # Profilo http fisso: stessa porta di launchSettings (evita https/porta diversa).
    dotnet run --launch-profile http --urls "http://localhost:5076"
}
finally {
    Pop-Location
}
