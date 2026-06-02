param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

# Скрипт лежить у NetSdrMonitor\install\ і працює відносно себе.
# Тека install\ — сусід проєкту NetSdrMonitor.Desktop, а не його батько.
$installDir = $PSScriptRoot
$repoRoot   = Split-Path $installDir -Parent                  # ...\NetSdrMonitor
$projectDir = Join-Path $repoRoot "NetSdrMonitor.Desktop"     # WPF-проєкт
$csproj     = Join-Path $projectDir "NetSdrMonitor.Desktop.csproj"
$iss        = Join-Path $installDir "setup.iss"
$publishDir = Join-Path $projectDir "bin\Release\net10.0-windows\win-x64\publish"
# OutputDir=. у setup.iss → інсталятор з'являється поруч із .iss, у install\.
$outputExe  = Join-Path $installDir "NetSdrMonitorSetup-$Version.exe"

Write-Host "==== NetSdrMonitor -- installer build (v$Version) ====" -ForegroundColor Cyan

# 1. Знаходимо ISCC.exe (компілятор Inno Setup).
$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe not found. Install Inno Setup 6+ from https://jrsoftware.org/isdl.php"
}
Write-Host "[1/5] ISCC: $iscc" -ForegroundColor DarkGray

# 2. Перевіряємо, що вшитий інсталятор рантайму лежить поруч зі скриптом.
$runtime = Join-Path $installDir "windowsdesktop-runtime-10.0.8-win-x64.exe"
if (-not (Test-Path $runtime)) {
    throw "Missing: $runtime`r`n    Download the .NET Desktop Runtime 10.0.8 (x64) installer into install\."
}
Write-Host "[2/5] Runtime bundle -- OK" -ForegroundColor DarkGray

# 3. Чистимо попередній publish і попередній інсталятор — щоб перезапуск не тягнув
#    застарілі .dll/.exe і не зливався з уже наявним Setup-*.exe. Обидва — суто
#    вихідні артефакти, тож видаляти безпечно.
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "[3/5] Cleaned $publishDir" -ForegroundColor DarkGray
} else {
    Write-Host "[3/5] No previous publish to clean" -ForegroundColor DarkGray
}
if (Test-Path $outputExe) {
    try {
        Remove-Item -Path $outputExe -Force
        Write-Host "      Removed previous installer: $outputExe" -ForegroundColor DarkGray
    } catch {
        throw "Cannot delete previous installer at $outputExe. Close it if it's running, then retry."
    }
}

# 4. dotnet publish -- framework-dependent (рантайм ставиться кроком [Run] у setup.iss),
#    символи прибрано. Вихід лягає в <projectDir>\bin\Release\net10.0-windows\win-x64\publish\,
#    який setup.iss читає як "..\NetSdrMonitor.Desktop\bin\Release\...\publish\*".
Write-Host "[4/5] dotnet publish..." -ForegroundColor DarkGray
& dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit $LASTEXITCODE)"
}

# 5. Компілюємо інсталятор. OutputDir=. у setup.iss → .exe з'являється в install\.
Write-Host "[5/5] ISCC..." -ForegroundColor DarkGray
& $iscc "/DMyAppVersion=$Version" $iss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed (exit $LASTEXITCODE)"
}

# 6. Переконуємось, що файл справді на місці — ISCC буває «успішним» на ворнінгах.
if (-not (Test-Path $outputExe)) {
    throw "ISCC reported success but $outputExe is missing. Inspect Inno output above."
}

Write-Host ""
Write-Host "Done! Installer: $outputExe" -ForegroundColor Green
$len = (Get-Item $outputExe).Length
Write-Host ("       Size: {0:N0} bytes ({1:N1} MB)" -f $len, ($len / 1MB)) -ForegroundColor DarkGray
