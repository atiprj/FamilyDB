$ErrorActionPreference = "Stop"

$root = "F:\Documenti Utili\BANCA DATI REVIT PROGETTI\Famiglie\Database Famiglie\PluginRevitFamilyDB"
$src = Join-Path $root "src\FamCloud.Addin2025\bin\Release\net48"
$dst = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2025\FamCloud"
$familyDbDst = "C:\ProgramData\RevitFamilyDb\2025"

if (-not (Test-Path $src)) {
    Write-Host "Build FamCloud..." -ForegroundColor Yellow
    dotnet build (Join-Path $root "src\FamCloud.Addin2025\FamCloud.Addin2025.csproj") -c Release | Out-Null
    if (-not (Test-Path $src)) {
        throw "Build non trovato: $src"
    }
}

function Copy-DeployFile {
    param([string]$FromPath, [string]$ToPath)
    New-Item -ItemType Directory -Path (Split-Path $ToPath -Parent) -Force | Out-Null
    try {
        Copy-Item $FromPath $ToPath -Force
    }
    catch {
        $bak = (Split-Path $ToPath -Leaf) + ".deploybak." + [DateTime]::UtcNow.ToString("yyyyMMddHHmmss")
        Rename-Item -LiteralPath $ToPath -NewName $bak -Force -ErrorAction SilentlyContinue
        Copy-Item $FromPath $ToPath -Force
    }
}

New-Item -ItemType Directory -Path $dst -Force | Out-Null
New-Item -ItemType Directory -Path $familyDbDst -Force | Out-Null

foreach ($file in @("FamCloud.Addin2025.dll", "RevitFamilyDb.Core.dll", "RevitFamilyDb.Addin2025.dll")) {
    $from = Join-Path $src $file
    if (-not (Test-Path $from)) {
        throw "File mancante: $from"
    }
    Copy-DeployFile -FromPath $from -ToPath (Join-Path $dst $file)
    if ($file -like "RevitFamilyDb.*") {
        Copy-DeployFile -FromPath $from -ToPath (Join-Path $familyDbDst $file)
    }
}

Write-Host "FamCloud deploy OK -> $dst" -ForegroundColor Green
Write-Host "Family DB allineato -> $familyDbDst (necessario per Elenco+Carica)" -ForegroundColor Green
Get-Item (Join-Path $dst "FamCloud.Addin2025.dll"), (Join-Path $familyDbDst "RevitFamilyDb.Addin2025.dll") |
    Select-Object FullName, Length, LastWriteTime
