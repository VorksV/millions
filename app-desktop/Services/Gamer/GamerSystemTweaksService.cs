using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Gamer
{
    /// <summary>
    /// Ajustes de sistema ligados ao Modo Gamer, com base em medição publicada.
    /// <para>
    /// ESCOPO DELIBERADO
    /// </para>
    /// <para>
    /// Só entra aqui o que tem evidência medida de efeito em jogos. Cada item
    /// abaixo foi verificado contra testes de terceiros (Tom's Hardware,
    /// TechSpot, ComputerBase) e contra o que a propria Microsoft publica.
    /// </para>
    /// <para>
    /// FORA DE ESCOPO, DE PROPOSITO:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>bcdedit / timers / HPET</b> — NAO tocado. Forcar
    /// Platform Clock causa queda comprovada de FPS. O unico modulo que mexia
    /// nisso (HpetModule) teve o rollback corrigido para usar /deletevalue.</description></item>
    /// <item><description><b>Otimizações de rede</b> (winsock/int ip/flush DNS) —
    /// NAO entra no Modo Gamer. Latência de jogo é dominada pelo caminho até o
    /// servidor, não pela pilha local; resetar a pilha só derruba a conexão.</description></item>
    /// <item><description><b>EmptyWorkingSet em todos os processos</b> — NAO entra.
    /// E cosmético e causa rajada de page faults.</description></item>
    /// <item><description><b>Timer resolution</b> — ja existente e correto
    /// (reversivel, restaura o valor original). Nada a fazer aqui.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// SEGURANÇA
    /// </para>
    /// <para>
    /// A Memory Integrity (HVCI) é o item de maior impacto medido, mas
    /// desligá-la é uma REDUÇÃO DE SEGURÇA, não um ajuste de performance. Por
    /// isso ela <b>não</b> é alterada automaticamente: o servico apenas
    /// DETECTA e RELATA, e a mudanca exige consentimento explicito do usuario e
    /// reinicializacao. A Microsoft passou a ativa-la automaticamente no Patch
    /// Tuesday de outubro justamente porque Many users a tinham desligada — o
    /// que torna a decisao do usuario ainda mais relevante de expor.
    /// </para>
    /// </summary>
    public sealed class GamerSystemTweaksService
    {
        private readonly ILoggingService? _logger;

        public GamerSystemTweaksService(ILoggingService? logger = null)
        {
            _logger = logger;
        }

        /// <summary>Estado da Memory Integrity, lido do Device Guard.</summary>
        public sealed class MemoryIntegrityState
        {
            /// <summary>True se o HVCI (Memory Integrity) está ATIVADO.</summary>
            public bool HvciRunning { get; init; }
            /// <summary>True se a Virtualization-Based Security está em uso.</summary>
            public bool VbsRunning { get; init; }
            /// <summary>True se o Device Guard está presente (Win10 1903+ / Win11).</summary>
            public bool DeviceGuardPresent { get; init; }
            /// <summary>True se é possível ler o estado (sem elevação pode falhar).</summary>
            public bool Readable { get; init; }
            public string RawSummary { get; init; } = "";
        }

        /// <summary>
        /// Lê o estado de Memory Integrity / VBS.
        /// <para>
        /// Funciona em Windows 10 (1903+) e Windows 11 pela classe
        /// <c>Win32_DeviceGuard</c> no namespace <c>root\Microsoft\Windows\DeviceGuard</c>,
        /// que é a via suportada nos dois. <c>SecurityServicesRunning</c> é um
        /// array de <c>uint</c> onde o valor <c>2</c> significa HVCI rodando.
        /// </para>
        /// </summary>
        public MemoryIntegrityState ReadMemoryIntegrity()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"\\.\root\Microsoft\Windows\DeviceGuard",
                    "SELECT VirtualizationBasedSecurityStatus, SecurityServicesConfigured, SecurityServicesRunning FROM Win32_DeviceGuard");

                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    using (obj)
                    {
                        uint vbsStatus = Convert.ToUInt32(obj["VirtualizationBasedSecurityStatus"] ?? 0u);
                        var running = obj["SecurityServicesRunning"] as ushort[];

                        bool hvci = false;
                        if (running != null)
                        {
                            foreach (var svc in running)
                            {
                                // 2 = HVCI (Memory Integrity)
                                if (svc == 2) { hvci = true; break; }
                            }
                        }

                        // 2 = enabled and running, 1 = enabled but not running
                        bool vbs = vbsStatus == 2 || vbsStatus == 1;

                        string summary =
                            $"VBS status={vbsStatus} (0=off,1=on,2=on+rodando) | " +
                            $"HVCI/MemoryIntegrity={(hvci ? "ATIVADO" : "desativado")} | " +
                            $"servicos rodando=[{(running == null ? "n/d" : string.Join(",", running))}]";

                        _logger?.LogInfo($"[GAMER-TWEAKS] Estado lido: {summary}");

                        return new MemoryIntegrityState
                        {
                            HvciRunning = hvci,
                            VbsRunning = vbs,
                            DeviceGuardPresent = true,
                            Readable = true,
                            RawSummary = summary
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                // Provável: sem elevação, ou build de Windows sem Device Guard.
                _logger?.LogWarning($"[GAMER-TWEAKS] Não foi possível ler o estado de Memory Integrity: {ex.Message}");
            }

            return new MemoryIntegrityState { Readable = false, DeviceGuardPresent = false, RawSummary = "indisponivel" };
        }

        /// <summary>
        /// Desliga a captura em background do Xbox Game Bar.
        /// <para>
        /// "Record what happened" (GameDVR_Enabled em
        /// HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR) grava o jogo
        /// continuamente mesmo sem o usuário pedir. É reversível, não afeta
        /// segurança e elimina consumo de CPU/GPU em background durante a partida.
        /// </para>
        /// </summary>
        public (bool Applied, string Detail) DisableBackgroundCapture()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\GameDVR");
                if (key == null) return (false, "chave de registro inacessivel");

                object? current = key.GetValue("AppCaptureEnabled");
                key.SetValue("AppCaptureEnabled", 0, Microsoft.Win32.RegistryValueKind.DWord);

                string detail = $"GameDVR AppCaptureEnabled: {current ?? "(nao definido)"} -> 0";
                _logger?.LogInfo($"[GAMER-TWEAKS] Captura em background desligada. {detail}");
                return (true, detail);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[GAMER-TWEAKS] Falha ao desligar captura em background: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Garante o Game Mode ligado.
        /// <para>
        /// <c>AllowAutoGameMode</c> = 1 liga o Game Mode, que segundo a própria
        /// Microsoft impede o Windows Update de instalar drivers durante o jogo e
        /// reduz contenção de threads. É o padrão do Windows, então garantir é
        /// apenas impedir que algo tenha desligado.
        /// </para>
        /// </summary>
        public (bool Applied, string Detail) EnsureGameMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\GameBar");
                if (key == null) return (false, "chave de registro inacessivel");

                object? current = key.GetValue("AllowAutoGameMode");
                key.SetValue("AllowAutoGameMode", 1, Microsoft.Win32.RegistryValueKind.DWord);

                string detail = $"GameMode AllowAutoGameMode: {current ?? "(nao definido)"} -> 1";
                _logger?.LogInfo($"[GAMER-TWEAKS] Game Mode garantido. {detail}");
                return (true, detail);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[GAMER-TWEAKS] Falha ao garantir Game Mode: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Relatório completo do estado, para o usuário e para o log.
        /// Chamado na ativação do Modo Gamer.
        /// </summary>
        public string BuildDiagnosticReport()
        {
            var mi = ReadMemoryIntegrity();
            bool win11 = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

            var lines = new List<string>
            {
                $"SO: Windows {(win11 ? "11" : "10 ou anterior")} (build {Environment.OSVersion.Version})",
                $"Elevado: {IsElevated()}",
                $"Memory Integrity: {(mi.Readable ? (mi.HvciRunning ? "ATIVADO" : "desativado") : "nao lido")}",
                $"VBS: {(mi.Readable ? (mi.VbsRunning ? "em uso" : "desligado") : "nao lido")}",
                $"Bruto: {mi.RawSummary}"
            };

            string report = string.Join(Environment.NewLine, lines);
            _logger?.LogInfo("[GAMER-TWEAKS] === DIAGNOSTICO ===" + Environment.NewLine + report);
            return report;
        }

        private static bool IsElevated()
        {
            try
            {
                using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
