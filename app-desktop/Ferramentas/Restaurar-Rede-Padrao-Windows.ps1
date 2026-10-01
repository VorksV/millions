<#
.SYNOPSIS
    Restaura ao padrao do Windows as configuracoes de rede alteradas pelo Voltris Optimizer.

.DESCRIPTION
    Corrige os picos de latencia (instabilidade de MS/ping em jogos) revertendo:
      - Politicas QoS DSCP 46 (registro HKLM\SOFTWARE\Policies\Microsoft\Windows\QoS\VoltrisQoS_*)
      - Politicas QoS criadas por NetQosPolicy (VoltrisQoS_*)
      - TcpAckFrequency / TcpDelAckTicks / TCPNoDelay (por interface e globais)
      - DefaultReceiveWindow / DefaultSendWindow (AFD)
      - NetworkThrottlingIndex / SystemResponsiveness (padrao 10 / 20)
      - Parametros globais do TCP (netsh)
      - Interrupt Moderation e afinidade de interrupcao da NIC (opcional)

.PARAMETER Force
    Nao pede confirmacao.

.PARAMETER DryRun
    Apenas mostra o que seria feito, sem alterar nada no sistema.

.PARAMETER SkipNic
    Nao mexe nas configuracoes avancadas da placa de rede.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Restaurar-Rede-Padrao-Windows.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Restaurar-Rede-Padrao-Windows.ps1 -DryRun
#>

[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$DryRun,
    [switch]$SkipNic
)

$ErrorActionPreference = 'SilentlyContinue'
$ProgressPreference   = 'SilentlyContinue'

function Write-Head { param([string]$m) Write-Host ""; Write-Host "== $m ==" -ForegroundColor White }
function Write-Step { param([string]$m) Write-Host "  [*] $m" -ForegroundColor Cyan }
function Write-Ok   { param([string]$m) Write-Host "  [+] $m" -ForegroundColor Green }
function Write-Skip { param([string]$m) Write-Host "  [-] $m" -ForegroundColor DarkGray }
function Write-Warn { param([string]$m) Write-Host "  [!] $m" -ForegroundColor Yellow }

$logPath = Join-Path $env:TEMP ("Voltris_RestaurarRede_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))
try { Start-Transcript -Path $logPath -Force | Out-Null } catch {}

Write-Host ""
Write-Host "==============================================================" -ForegroundColor White
Write-Host "  RESTAURAR REDE - PADRAO DO WINDOWS (correcao de lag/MS)" -ForegroundColor White
Write-Host "==============================================================" -ForegroundColor White

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Warn "Este script precisa ser executado como Administrador."
    Write-Host ""
    Write-Host "Dica: use o arquivo Restaurar-Rede-Padrao.cmd na mesma pasta," -ForegroundColor Yellow
    Write-Host "ele solicita a elevacao automaticamente." -ForegroundColor Yellow
    try { Stop-Transcript | Out-Null } catch {}
    Read-Host "Pressione ENTER para sair"
    exit 1
}

if ($DryRun) { Write-Warn "MODO SIMULACAO (DryRun): nenhuma alteracao sera feita." }

if (-not $Force -and -not $DryRun) {
    Write-Host ""
    Write-Host "  Este script vai reverter as otimizacoes de rede do Voltris" -ForegroundColor Yellow
    Write-Host "  que causam instabilidade de MS, voltando ao padrao do Windows." -ForegroundColor Yellow
    Write-Host ""
    $resp = Read-Host "  Deseja continuar? (S/N)"
    if ($resp -notmatch '^[sSyY]') {
        Write-Warn "Cancelado pelo usuario."
        try { Stop-Transcript | Out-Null } catch {}
        exit 0
    }
}

# ---------------------------------------------------------------------------
# 1/7 - QoS DSCP (registro)
# ---------------------------------------------------------------------------
Write-Head "1/7 - Politicas QoS DSCP (registro)"
$qosRoot = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\QoS'
$qosSubs = @(Get-ChildItem -Path $qosRoot -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -like 'VoltrisQoS_*' })
if ($qosSubs.Count -gt 0) {
    foreach ($s in $qosSubs) {
        if ($DryRun) { Write-Skip "DryRun: removeria $($s.PSPath)" }
        else {
            $p = $s.PSPath
            Remove-Item -Path $p -Recurse -Force -ErrorAction SilentlyContinue
            if (-not (Test-Path -LiteralPath $p)) { Write-Ok "Politica removida: $($s.PSChildName)" }
            else { Write-Warn "Nao foi possivel remover: $($s.PSChildName)" }
        }
    }
} else {
    Write-Ok "Nenhuma politica QoS do Voltris no registro."
}

# ---------------------------------------------------------------------------
# 2/7 - QoS (cmdlet NetQos)
# ---------------------------------------------------------------------------
Write-Head "2/7 - Politicas QoS (cmdlet NetQos)"
try {
    $netQos = @(Get-NetQosPolicy -ErrorAction Stop | Where-Object { $_.Name -like 'VoltrisQoS_*' })
    if ($netQos.Count -gt 0) {
        foreach ($x in $netQos) {
            if ($DryRun) { Write-Skip "DryRun: removeria NetQosPolicy '$($x.Name)'" }
            else {
                Remove-NetQosPolicy -Name $x.Name -Confirm:$false -ErrorAction SilentlyContinue
                Write-Ok "NetQosPolicy removida: $($x.Name)"
            }
        }
    } else {
        Write-Ok "Nenhuma NetQosPolicy do Voltris."
    }
} catch {
    Write-Warn "Nao foi possivel consultar NetQosPolicy: $($_.Exception.Message)"
}

# ---------------------------------------------------------------------------
# 3/7 - Parametros TCP (por interface + globais)
# ---------------------------------------------------------------------------
Write-Head "3/7 - Parametros TCP (interfaces e globais)"
$tcpProps = @('TcpAckFrequency', 'TcpDelAckTicks', 'TCPNoDelay')
$tcpBase  = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'
$tcpKeys  = @($tcpBase)
$tcpKeys += @(Get-ChildItem -Path "$tcpBase\Interfaces" -ErrorAction SilentlyContinue | ForEach-Object { $_.PSPath })
$tcpFixed = 0
foreach ($k in $tcpKeys) {
    foreach ($prop in $tcpProps) {
        $exists = $null -ne (Get-ItemProperty -Path $k -Name $prop -ErrorAction SilentlyContinue)
        if ($exists) {
            if ($DryRun) {
                Write-Skip "DryRun: removeria '$prop' em $k"
            } else {
                Remove-ItemProperty -Path $k -Name $prop -Force -ErrorAction SilentlyContinue
                if ($null -eq (Get-ItemProperty -Path $k -Name $prop -ErrorAction SilentlyContinue)) { $tcpFixed++ }
            }
        }
    }
}
if (-not $DryRun) { Write-Ok "Entradas de tweak TCP removidas: $tcpFixed" }

# ---------------------------------------------------------------------------
# 4/7 - Buffers AFD
# ---------------------------------------------------------------------------
Write-Head "4/7 - Buffers AFD (janelas TCP)"
$afdPath = 'HKLM:\SYSTEM\CurrentControlSet\Services\AFD\Parameters'
$afdFixed = 0
foreach ($prop in @('DefaultReceiveWindow', 'DefaultSendWindow')) {
    $exists = $null -ne (Get-ItemProperty -Path $afdPath -Name $prop -ErrorAction SilentlyContinue)
    if ($exists) {
        if ($DryRun) { Write-Skip "DryRun: removeria '$prop'" }
        else {
            Remove-ItemProperty -Path $afdPath -Name $prop -Force -ErrorAction SilentlyContinue
            $afdFixed++
        }
    }
}
if (-not $DryRun) {
    if ($afdFixed -gt 0) { Write-Ok "Buffers AFD restaurados (auto-tuning do Windows)." }
    else { Write-Ok "Nenhum buffer AFD alterado encontrado." }
}

# ---------------------------------------------------------------------------
# 5/7 - Multimedia / SystemProfile
# ---------------------------------------------------------------------------
Write-Head "5/7 - Multimedia (NetworkThrottlingIndex / SystemResponsiveness)"
$mmPath = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'
if (Test-Path -LiteralPath $mmPath) {
    if ($DryRun) {
        Write-Skip "DryRun: NetworkThrottlingIndex=10 e SystemResponsiveness=20"
    } else {
        Set-ItemProperty -Path $mmPath -Name 'NetworkThrottlingIndex' -Value 10 -Type DWord -ErrorAction SilentlyContinue
        Set-ItemProperty -Path $mmPath -Name 'SystemResponsiveness'  -Value 20 -Type DWord -ErrorAction SilentlyContinue
        Write-Ok "Restaurado padrao: NetworkThrottlingIndex=10, SystemResponsiveness=20."
    }
} else {
    Write-Warn "Chave Multimedia\\SystemProfile nao encontrada."
}

# ---------------------------------------------------------------------------
# 6/7 - Parametros globais do TCP (netsh)
# ---------------------------------------------------------------------------
Write-Head "6/7 - Parametros globais do TCP (netsh)"
$netshArgs = @(
    'int tcp set global autotuninglevel=normal',
    'int tcp set global rss=enabled',
    'int tcp set global dca=disabled',
    'int tcp set global ecncapability=disabled',
    'int tcp set global timestamps=disabled',
    'int tcp set global initialrto=3000',
    'int tcp set global nonsackrttresiliency=disabled',
    'int tcp set heuristics disabled'
)
foreach ($a in $netshArgs) {
    if ($DryRun) { Write-Skip "DryRun: netsh $a"; continue }
    $out = (& netsh.exe $a.Split(' ') 2>&1) -join ' '
    if ($LASTEXITCODE -eq 0) { Write-Ok "netsh $a" }
    else { Write-Warn "netsh $a -> $out" }
}

# ---------------------------------------------------------------------------
# 7/7 - NIC avancada (Interrupt Moderation / afinidade)
# ---------------------------------------------------------------------------
if ($SkipNic) {
    Write-Head "7/7 - NIC avancada"
    Write-Skip "Ignorado por -SkipNic."
} else {
    Write-Head "7/7 - NIC avancada (Interrupt Moderation / afinidade)"
    if ($DryRun) {
        Write-Skip "DryRun: resetaria Interrupt Moderation e afinidade das placas."
    } else {
        try {
            $adapters = @(Get-NetAdapter -ErrorAction Stop)
            foreach ($ad in $adapters) {
                $props = @(Get-NetAdapterAdvancedProperty -Name $ad.Name -ErrorAction SilentlyContinue |
                    Where-Object { $_.DisplayName -match 'Interrupt Moderation' })
                foreach ($p in $props) {
                    Reset-NetAdapterAdvancedProperty -Name $ad.Name -DisplayName $p.DisplayName -NoRestart -ErrorAction SilentlyContinue
                    Write-Ok "Interrupt Moderation restaurado: $($ad.Name) [$($p.DisplayName)]"
                }
            }
        } catch {
            Write-Warn "NIC (Interrupt Moderation): $($_.Exception.Message)"
        }

        try {
            Get-CimInstance -ClassName Win32_NetworkAdapter -ErrorAction SilentlyContinue |
                Where-Object { $_.PNPDeviceID -and $_.PNPDeviceID -like 'PCI*' } |
                ForEach-Object {
                    $aff = "HKLM:\SYSTEM\CurrentControlSet\Enum\$($_.PNPDeviceID)\Device Parameters\Interrupt Management\AffinityPolicy"
                    if (Test-Path -LiteralPath $aff) {
                        Remove-ItemProperty -Path $aff -Name 'AssignmentSetOverride' -Force -ErrorAction SilentlyContinue
                        Remove-ItemProperty -Path $aff -Name 'DevicePriority' -Force -ErrorAction SilentlyContinue
                        Write-Ok "Afinidade de interrupcao restaurada: $($_.PNPDeviceID)"
                    }
                }
        } catch {
            Write-Warn "NIC (afinidade): $($_.Exception.Message)"
        }
    }
}

# ---------------------------------------------------------------------------
# Finalizacao
# ---------------------------------------------------------------------------
Write-Head "Finalizando"
if (-not $DryRun) {
    & ipconfig.exe /flushdns 2>$null | Out-Null
    Write-Ok "Cache DNS limpo."
}

Write-Host ""
Write-Host "Concluido." -ForegroundColor Green
Write-Host "Log salvo em: $logPath" -ForegroundColor DarkGray
Write-Host "Recomendado reiniciar o PC para aplicar todas as alteracoes." -ForegroundColor Yellow
Write-Host ""
try { Stop-Transcript | Out-Null } catch {}
