$ErrorActionPreference = "Stop"

$workspace = "d:\APLICATIVO VOLTRIS"
Set-Location $workspace

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "🚀 VOLTRIS OPTIMIZER - ENTERPRISE BUILD & RELEASE (v1.0.2.5)" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

Write-Host "`n[ETAPA 0] Limpando diretórios antigos de publish e artefatos temporários..." -ForegroundColor Yellow
$ErrorActionPreference = "SilentlyContinue"
Remove-Item "publish" -Recurse -Force
Remove-Item "@Resources" -Recurse -Force
Remove-Item "Update\VoltrisUpdater\Payload" -Recurse -Force
$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Path "publish\x86" | Out-Null
New-Item -ItemType Directory -Path "publish\x64" | Out-Null
New-Item -ItemType Directory -Path "@Resources" | Out-Null
New-Item -ItemType Directory -Path "Update\VoltrisUpdater\Payload" | Out-Null
New-Item -ItemType Directory -Path "publish\installers\x86" | Out-Null
New-Item -ItemType Directory -Path "publish\installers\x64" | Out-Null
New-Item -ItemType Directory -Path "publish\updaters\x86" | Out-Null
New-Item -ItemType Directory -Path "publish\updaters\x64" | Out-Null

Write-Host "`n[ETAPA 1] Compilando VoltrisOptimizer Principal (Arquivos Soltos / Non-Single-File)..." -ForegroundColor Yellow
Write-Host "  -> Compilando win-x86 (32-bit)..." -ForegroundColor Gray
dotnet publish VoltrisOptimizer.csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o "publish\x86"

Write-Host "  -> Compilando win-x64 (64-bit)..." -ForegroundColor Gray
dotnet publish VoltrisOptimizer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o "publish\x64"

Write-Host "`n[ETAPA 1.1] Liberando locks do compilador..." -ForegroundColor Gray
dotnet build-server shutdown
Start-Sleep -Seconds 3

Write-Host "`n[ETAPA 2] Compactando arquivos soltos em pacotes ZIP..." -ForegroundColor Yellow
function Compress-WithRetry {
    param($Path, $Dest)
    $maxTries = 6
    for ($i = 1; $i -le $maxTries; $i++) {
        try {
            Compress-Archive -Path $Path -DestinationPath $Dest -Force -ErrorAction Stop
            Write-Host "  -> Gerado com sucesso: $Dest" -ForegroundColor Green
            return
        } catch {
            Write-Host "  [Aviso] Arquivo ocupado, tentativa $i de $maxTries em 3s..." -ForegroundColor Yellow
            Start-Sleep -Seconds 3
        }
    }
    throw "Falha crítica ao compactar $Dest após múltiplas tentativas."
}

Compress-WithRetry -Path "publish\x86\*" -Dest "@Resources\VOLTris_Optimizer_x86.zip"
Compress-WithRetry -Path "publish\x64\*" -Dest "@Resources\VOLTris_Optimizer_x64.zip"

Write-Host "`n[ETAPA 2.1] Copiando ZIPs para o diretório de Payload do Updater..." -ForegroundColor Yellow
Copy-Item "@Resources\VOLTris_Optimizer_x86.zip" -Destination "Update\VoltrisUpdater\Payload\VOLTris_Optimizer_x86.zip"
Copy-Item "@Resources\VOLTris_Optimizer_x64.zip" -Destination "Update\VoltrisUpdater\Payload\VOLTris_Optimizer_x64.zip"

Write-Host "`n[ETAPA 3] Compilando Desinstalador (VoltrisUninstaller - Single File)..." -ForegroundColor Yellow
Write-Host "  -> Compilando Uninstaller win-x86..." -ForegroundColor Gray
dotnet publish "Instalador Voltris Optmizer\VoltrisUninstaller\VoltrisUninstaller.csproj" -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -o "publish\temp_uninst_x86"

Write-Host "  -> Compilando Uninstaller win-x64..." -ForegroundColor Gray
dotnet publish "Instalador Voltris Optmizer\VoltrisUninstaller\VoltrisUninstaller.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "publish\temp_uninst_x64"

Copy-Item "publish\temp_uninst_x86\uninstall.exe" -Destination "@Resources\uninstall_x86_single.exe"
Copy-Item "publish\temp_uninst_x64\uninstall.exe" -Destination "@Resources\uninstall_x64_single.exe"
Remove-Item "publish\temp_uninst_x86" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "publish\temp_uninst_x64" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n[ETAPA 4] Compilando Instaladores (VoltrisOptimizerInstaller)..." -ForegroundColor Yellow
Write-Host "  -> Compilando Instalador win-x86..." -ForegroundColor Gray
dotnet publish "Instalador Voltris Optmizer\VoltrisOptimizerInstaller\VoltrisOptimizerInstaller.csproj" -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o "publish\installers\x86"

Write-Host "  -> Compilando Instalador win-x64..." -ForegroundColor Gray
dotnet publish "Instalador Voltris Optmizer\VoltrisOptimizerInstaller\VoltrisOptimizerInstaller.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o "publish\installers\x64"

Write-Host "`n[ETAPA 5] Compilando Voltris Updater (Single-File Blindado)..." -ForegroundColor Yellow
Write-Host "  -> Compilando VoltrisUpdater win-x86..." -ForegroundColor Gray
dotnet publish "Update\VoltrisUpdater\VoltrisUpdater.csproj" -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o "publish\updaters\x86"

Write-Host "  -> Compilando VoltrisUpdater win-x64..." -ForegroundColor Gray
dotnet publish "Update\VoltrisUpdater\VoltrisUpdater.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o "publish\updaters\x64"

# Criar cópias com nomes amigáveis em publish\
Copy-Item "publish\installers\x86\VoltrisOptimizerInstaller.exe" -Destination "publish\installers\VoltrisOptimizerInstaller_x86.exe" -Force
Copy-Item "publish\installers\x64\VoltrisOptimizerInstaller.exe" -Destination "publish\installers\VoltrisOptimizerInstaller_x64.exe" -Force
Copy-Item "publish\updaters\x86\VoltrisUpdater.exe" -Destination "publish\updaters\VoltrisUpdater_x86.exe" -Force
Copy-Item "publish\updaters\x64\VoltrisUpdater.exe" -Destination "publish\updaters\VoltrisUpdater_x64.exe" -Force
Copy-Item "publish\updaters\x64\VoltrisUpdater.exe" -Destination "publish\updaters\VoltrisUpdate_x64.exe" -Force

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "SUCESSO ABSOLUTO! BUILD ENTERPRISE COMPLETO (v1.0.2.5)" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green

Write-Host "`nARQUIVOS GERADOS:" -ForegroundColor Cyan
Get-ChildItem -Path "publish\installers\*.exe" | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  [Instalador] $($_.Name) - $size MB" -ForegroundColor White
}
Get-ChildItem -Path "publish\updaters\*.exe" | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  [Updater]    $($_.Name) - $size MB" -ForegroundColor White
}
Get-ChildItem -Path "@Resources\*.zip" | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  [ZIP Payload] $($_.Name) - $size MB" -ForegroundColor White
}
