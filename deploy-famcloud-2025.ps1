$ErrorActionPreference = "Stop"

$root = "F:\Documenti Utili\BANCA DATI REVIT PROGETTI\Famiglie\Database Famiglie\PluginRevitFamilyDB"
$src = Join-Path $root "src\FamCloud.Addin2025\bin\Release\net48"
$dst = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2025\FamCloud"

if (-not (Test-Path $src)) {
    throw "Build non trovato. Esegui: dotnet build `"$root\src\FamCloud.Addin2025\FamCloud.Addin2025.csproj`" -c Release"
}

New-Item -ItemType Directory -Path $dst -Force | Out-Null

foreach ($file in @("FamCloud.Addin2025.dll", "RevitFamilyDb.Core.dll", "RevitFamilyDb.Addin2025.dll")) {
    $from = Join-Path $src $file
    $to = Join-Path $dst $file
    if (-not (Test-Path $from)) {
        throw "File mancante: $from"
    }
    Copy-Item $from $to -Force
}

Write-Host "FamCloud deploy OK -> $dst" -ForegroundColor Green
Get-Item (Join-Path $dst "FamCloud.Addin2025.dll") | Select-Object FullName, Length, LastWriteTime
