$ErrorActionPreference = "Stop"

$workspace = "d:\APLICATIVO VOLTRIS"
Set-Location $workspace

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "🔄 VOLTRIS - BUILD UPDATE ONLY (v1.0.2.5)" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# ── Limpeza dos outputs anteriores ──────────────────────────────
Write-Host "`n[ETAPA 0] Limpando outputs anteriores do updater..." -ForegroundColor Yellow
$ErrorActionPreference = "SilentlyContinue"
Remove-Item "publish\updaters" -Recurse -Force
$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Path "publish\updaters\x86" | Out-Null
New-Item -ItemType Directory -Path "publish\updaters\x64" | Out-Null

# ── Verificar que os ZIPs do payload existem ────────────────────
Write-Host "`n[VERIFICAÇÃO] Checando ZIPs de payload..." -ForegroundColor Yellow
if (-not (Test-Path "@Resources\VOLTris_Optimizer_x86.zip")) {
    Write-Host "  ❌ ZIP x86 não encontrado em @Resources! Execute build_release.ps1 primeiro." -ForegroundColor Red
    exit 1
}
if (-not (Test-Path "@Resources\VOLTris_Optimizer_x64.zip")) {
    Write-Host "  ❌ ZIP x64 não encontrado em @Resources! Execute build_release.ps1 primeiro." -ForegroundColor Red
    exit 1
}
Write-Host "  ✅ VOLTris_Optimizer_x86.zip encontrado" -ForegroundColor Green
Write-Host "  ✅ VOLTris_Optimizer_x64.zip encontrado" -ForegroundColor Green

# ── Copiar ZIPs para o Payload do Updater ───────────────────────
Write-Host "`n[ETAPA 1] Atualizando Payload do Updater com os ZIPs..." -ForegroundColor Yellow
$ErrorActionPreference = "SilentlyContinue"
Remove-Item "Update\VoltrisUpdater\Payload" -Recurse -Force
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Path "Update\VoltrisUpdater\Payload" | Out-Null

Copy-Item "@Resources\VOLTris_Optimizer_x86.zip" -Destination "Update\VoltrisUpdater\Payload\VOLTris_Optimizer_x86.zip"
Copy-Item "@Resources\VOLTris_Optimizer_x64.zip" -Destination "Update\VoltrisUpdater\Payload\VOLTris_Optimizer_x64.zip"
Write-Host "  ✅ Payload atualizado" -ForegroundColor Green

# ── Compilar VoltrisUpdater x86 ─────────────────────────────────
Write-Host "`n[ETAPA 2] Compilando VoltrisUpdater win-x86 (Single-File Blindado)..." -ForegroundColor Yellow
dotnet publish "Update\VoltrisUpdater\VoltrisUpdater.csproj" `
    -c Release -r win-x86 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -o "publish\updaters\x86"

# ── Compilar VoltrisUpdater x64 ─────────────────────────────────
Write-Host "`n[ETAPA 3] Compilando VoltrisUpdater win-x64 (Single-File Blindado)..." -ForegroundColor Yellow
dotnet publish "Update\VoltrisUpdater\VoltrisUpdater.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -o "publish\updaters\x64"

# ── Copiar com nomes amigáveis ───────────────────────────────────
Write-Host "`n[ETAPA 4] Criando cópias com nomes de release..." -ForegroundColor Yellow
Copy-Item "publish\updaters\x86\VoltrisUpdater.exe" -Destination "publish\updaters\VoltrisUpdater_x86.exe" -Force
Copy-Item "publish\updaters\x64\VoltrisUpdater.exe" -Destination "publish\updaters\VoltrisUpdater_x64.exe" -Force
Copy-Item "publish\updaters\x64\VoltrisUpdater.exe" -Destination "publish\updaters\VoltrisUpdate_x64.exe" -Force

# ── Resultado ───────────────────────────────────────────────────
Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "✅ BUILD UPDATE COMPLETO (v1.0.2.5)" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green

Write-Host "`nARQUIVOS GERADOS:" -ForegroundColor Cyan
Get-ChildItem -Path "publish\updaters\*.exe" | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  [Updater] $($_.Name) - $size MB" -ForegroundColor White
}

Write-Host "`nPROXIMO PASSO:" -ForegroundColor Yellow
Write-Host "  Faca upload dos arquivos abaixo no GitHub Release v1.0.2.5:" -ForegroundColor White
Write-Host "  -> publish\updaters\VoltrisUpdater_x86.exe" -ForegroundColor Gray
Write-Host "  -> publish\updaters\VoltrisUpdater_x64.exe" -ForegroundColor Gray
Write-Host "  -> publish\updaters\VoltrisUpdate_x64.exe" -ForegroundColor Gray
Write-Host "  E atualize update.json + version.json no repositorio voltris-releases." -ForegroundColor Gray
