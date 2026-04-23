$ErrorActionPreference = "Stop"



$root = "F:\Documenti Utili\BANCA DATI REVIT PROGETTI\Famiglie\Database Famiglie\PluginRevitFamilyDB"

$src = Join-Path $root "src\RevitFamilyDb.Addin2025\bin\Release\net48"

$dst = "C:\ProgramData\RevitFamilyDb\2025"



if (-not (Test-Path $src)) {

    throw "Output build non trovato: $src"

}



function Copy-DeployFile {

    param([string]$FromPath, [string]$ToPath, [switch]$Required)

    if (-not (Test-Path $FromPath)) {

        if ($Required) { throw "File mancante: $FromPath" }

        return

    }

    try {

        Copy-Item $FromPath $ToPath -Force

    }

    catch {

        $bak = (Split-Path $ToPath -Leaf) + ".deploybak." + [DateTime]::UtcNow.ToString("yyyyMMddHHmmss")

        Rename-Item -LiteralPath $ToPath -NewName $bak -Force

        Copy-Item $FromPath $ToPath -Force

    }

}



New-Item -ItemType Directory -Path $dst -Force | Out-Null

Copy-DeployFile -FromPath (Join-Path $src "RevitFamilyDb.Addin2025.dll") -ToPath (Join-Path $dst "RevitFamilyDb.Addin2025.dll") -Required

Copy-DeployFile -FromPath (Join-Path $src "RevitFamilyDb.Core.dll") -ToPath (Join-Path $dst "RevitFamilyDb.Core.dll") -Required

Copy-DeployFile -FromPath (Join-Path $src "dbcloud.png") -ToPath (Join-Path $dst "dbcloud.png")



Write-Host "Deploy 2025 completato." -ForegroundColor Green

