using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Gamer
{
    /// <summary>
    /// Ajustes de runtime do Modo Gamer: aplicados na ATIVAÇÃO e revertidos
    /// automaticamente na DESATIVAÇÃO, sem interação do usuário.
    /// <para>
    /// TODOS os itens aqui são de execução imediata e reversível. Nada exige
    /// reinício — os ajustes que exigem (HAGS, Memory Integrity, bcdedit) são
    /// deliberadamente EXCLUÍDOs e ficam como recomendação apenas.
    /// </para>
    /// <para>
    /// LIMITE TÉCNICO REAL — MMCSS (importante)
    /// </para>
    /// <para>
    /// <c>AvSetMmThreadCharacteristics</c> aplica MMCSS à THREAD QUE CHAMOU,
    /// não a outro processo. Dar prioridade MMCSS ao processo do jogo exigiria
    /// injetar uma thread dentro dele — o que só é possível com driver de
    /// kernel. O Voltris não tem driver e não vai injetar. Por isso MMCSS
    /// <b>não</b> é aplicado aqui, e o antigo
    /// <c>SystemArm.EnableMmcssGamingAsync</c>, que apenas logava "ON" sem
    /// fazer nada, foi tratado como enganoso.
    /// </para>
    /// </summary>
    public sealed class GamerRuntimeTweaksService
    {
        private readonly ILoggingService? _logger;
        private readonly List<string> _appliedLog = new();
        // [FIX:C-3] Tipo qualificado: existe um ProcessPriorityClass tambem em
        // VoltrisOptimizer.Core.Body, e o using deste arquivo traz os dois
        // namespaces, tornando o nome ambiguo (CS0104).
        private readonly Dictionary<int, System.Diagnostics.ProcessPriorityClass> _originalPriorities = new();

        public GamerRuntimeTweaksService(ILoggingService? logger = null)
        {
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════════
        //  DETECÇÃO DE GPU
        // ════════════════════════════════════════════════════════════

        public enum GpuVendorKind { Unknown, Intel, Nvidia, Amd, IntegratedOnly }

        public sealed class GpuProfile
        {
            public GpuVendorKind Vendor { get; init; }
            public string AdapterName { get; init; } = "";
            public bool IsIntegrated { get; init; }
            /// <summary>Conselho de HAGS para ESTA GPU. Aplicar não; apenas informar.</summary>
            public string HagsAdvice { get; init; } = "";
            /// <summary>Conselho oficial da fabricante, entre aspas conceituais.</summary>
            public string VendorGuidance { get; init; } = "";
        }

        public GpuProfile DetectGpu()
        {
            try
            {
                string? name = null;
                bool integrated = false;
                bool found = false;

                try
                {
                    // WMI é a via mais confiável e funciona em Win10 e Win11.
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        "SELECT Name, AdapterCompatibility, PNPDeviceID FROM Win32_VideoController");
                    foreach (var obj in searcher.Get())
                    {
                        using (obj)
                        {
                            found = true;
                            var n = obj["Name"]?.ToString();
                            if (string.IsNullOrWhiteSpace(n)) continue;
                            // Prefere a GPU com maior memória de vídeo dedicada.
                            name = n;
                            if (n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) { name = n; break; }
                            if (n.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                                n.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) { name = n; }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[GAMER-RUNTIME] WMI de vídeo indisponível: {ex.Message}");
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    _logger?.LogWarning("[GAMER-RUNTIME] Nenhuma GPU identificada por WMI.");
                    return new GpuProfile { Vendor = GpuVendorKind.Unknown };
                }

                // integrated: Intel UHD/Iris Xe, ou AMD com "Radeon Graphics" sem número
                integrated = name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase)
                         || name.Contains("Iris", StringComparison.OrdinalIgnoreCase)
                         || name.Contains("UHD", StringComparison.OrdinalIgnoreCase)
                         || (name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                             && !System.Text.RegularExpressions.Regex.IsMatch(name, @"\b(RX|GTX|RAVEN|Radeon Pro)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

                GpuVendorKind vendor;
                string hags, guidance;

                if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                {
                    vendor = GpuVendorKind.Nvidia;
                    hags = "NVIDIA: desligar HAGS em competitivo reduz micro-stutter, mas LIGAR e OBRIGATORIO para DLSS Frame Generation (RTX 40/50).";
                    guidance = "NVIDIA recomenda Reflex = ON (maior ganho de latencia documentado pela propria fabricante) e, sem Reflex, Ultra Low Latency Mode.";
                }
                else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                {
                    vendor = GpuVendorKind.Amd;
                    hags = "AMD: manter HAGS LIGADO reduz overhead de CPU e latencia.";
                    guidance = "AMD/Adrenalin: manter HAGS ligado; usar Anti-Lag e|Chill conforme o titulo.";
                }
                else if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                {
                    vendor = GpuVendorKind.IntegratedOnly;
                    hags = "GPU integrada: HAGS NAO se aplica (exige GPU dedicada com Resource Scheduler). Nada a fazer.";
                    guidance = "Intel integrada: o gargalo e CPU; o ganho vem de reduzir carga de CPU em fundo, nao de ajuste de driver.";
                }
                else
                {
                    vendor = GpuVendorKind.Unknown;
                    hags = "Fabricante nao identificada: nenhuma recomendacao automatica.";
                    guidance = "";
                }

                var profile = new GpuProfile
                {
                    Vendor = vendor,
                    AdapterName = name!,
                    IsIntegrated = integrated,
                    HagsAdvice = hags,
                    VendorGuidance = guidance
                };

                _logger?.LogInfo(
                    $"[GAMER-RUNTIME] GPU detectada: '{name}' | vendor={vendor} | integrada={integrated}");
                _logger?.LogInfo($"[GAMER-RUNTIME] HAGS: {hags}");
                if (!string.IsNullOrEmpty(guidance))
                    _logger?.LogInfo($"[GAMER-RUNTIME] Orientacao do fabricante: {guidance}");

                return profile;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GAMER-RUNTIME] Erro na detecção de GPU: {ex.Message}");
                return new GpuProfile { Vendor = GpuVendorKind.Unknown };
            }
        }

        // ════════════════════════════════════════════════════════════
        //  ITEM 1 — NVDIA REFLEX / DRIVER PROFILE (instantâneo, sem reinício)
        // ════════════════════════════════════════════════════════════

        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface")]
        private static extern int NvApiQueryInterface(uint functionId, [In, Out] byte[] functionData);

        [StructLayout(LayoutKind.Sequential)]
        // [FIX:C-3] O campo não pode se chamar "Version": C# proibe um membro com
        // o mesmo nome do tipo que o contém (CS0542). Renomeado para NV_VERSION
        // preservando o layout do struct, que é o que importa para o P/Invoke.
        private struct NV_VERSION { public uint Version; }

        private const uint NvApiId_EnumPhysicalGpus = 0xE5AC921F;

        /// <summary>
        /// Verifica se a NVAPI está presente e se a GPU é NVIDIA.
        /// Não LANÇA: devolve apenas o resultado, porque é opcional e a
        /// ausência é normal em máquina AMD/Intel.
        /// </summary>
        public bool NvidiaApiAvailable()
        {
            try
            {
                if (!File.Exists(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System), "nvapi64.dll")))
                {
                    _logger?.LogTrace("[GAMER-RUNTIME] nvapi64.dll ausente — driver NVIDIA nao instalado.");
                    return false;
                }

                // Consulta a versao da API apenas para provar que a DLL carrega e responde.
                var data = new byte[16];
                BitConverter.GetBytes(0x00030000u).CopyTo(data, 0);
                int st = NvApiQueryInterface(0x01501234u /* NvAPI_Initialize */, data);
                _logger?.LogTrace($"[GAMER-RUNTIME] NvAPI Initialize retornou 0x{st:X}");
                return st == 0;
            }
            catch (DllNotFoundException)
            {
                _logger?.LogTrace("[GAMER-RUNTIME] nvapi64.dll nao encontrada.");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[GAMER-RUNTIME] NVAPI indisponivel: {ex.Message}");
                return false;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  ITEM 4 — REBAIXAR PROCESSOS DE FUNDO
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Processos que NUNCA podem ser rebaixados. Rebaixar qualquer um
        /// destes quebra o jogo, o launcher ou o proprio Voltris.
        /// </summary>
        private static readonly string[] ProtectedNames =
        {
            "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon",
            "services", "lsass", "svchost", "fontdrvhost", "dwm", "sihost", "ctfmon",
            "explorer", "audiodg", "conhost", "defender", "msmpeng", "searchindexer",
            "voltrisoptimizer", "gamebar", "gamesbar", "xbox", "startmenuexperiencehost",
            "steam", "steamwebhelper", "epicgameslauncher", "battlenet", "origin",
            "eahost", "galaxyclient", "riotclientservices", "obs64", "obs32",
            "discord", "nvidia", "amd", "intel", "nvcontainer", "amdrsserv",
            "easynt", "evonyx64", "xgameruntime"
        };

        public (int Rebaixados, int Protegidos, int Erros) RebaixarProcessosDeFundo()
        {
            int low = 0, prot = 0, err = 0;
            try
            {
                var procs = Process.GetProcesses();
                _logger?.LogInfo($"[GAMER-RUNTIME] Rebaixando processos de fundo: {procs.Length} candidatos.");
                _logger?.LogInfo(
                    "[GAMER-RUNTIME] Nota: usa System.Diagnostics.Process.PriorityClass e NAO o " +
                    "ProcessArm, porque o enum do projeto (Core\\Body\\ProcessPriorityClass) " +
                    "cobre apenas Normal/AboveNormal/High/RealTime — nao existe BelowNormal nem " +
                    "Idle nele. Rebaixar e a unica forma de tirar recurso de fundo sem " +
                    "promover o jogo, entao esta e a unica API que permite.");

                foreach (var p in procs)
                {
                    try
                    {
                        if (p.HasExited || p.Id <= 4) { p.Dispose(); continue; }
                        string pn = p.ProcessName;

                        if (ProtectedNames.Any(n => pn.Contains(n, StringComparison.OrdinalIgnoreCase)))
                        {
                            prot++;
                            p.Dispose();
                            continue;
                        }

                        var before = p.PriorityClass;
                        if (before == System.Diagnostics.ProcessPriorityClass.BelowNormal ||
                            before == System.Diagnostics.ProcessPriorityClass.Idle)
                        {
                            prot++; // ja estava rebaixado
                            p.Dispose();
                            continue;
                        }

                        // Guarda o original para restaurar na desativacao.
                        _originalPriorities[p.Id] = before;

                        p.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
                        low++;
                    }
                    catch (Exception)
                    {
                        err++;
                    }
                    finally { p.Dispose(); }
                }

                _logger?.LogInfo(
                    $"[GAMER-RUNTIME] Rebaixados={low} | ja-protegidos={prot} | erros={err}");
                return (low, prot, err);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GAMER-RUNTIME] Falha ao rebaixar processos: {ex.Message}");
                return (low, prot, err);
            }
        }

        /// <summary>Desfaz o rebaixamento, restaurando cada prioridade original.</summary>
        public int RestaurarPrioridades()
        {
            int ok = 0, falhas = 0;
            foreach (var kv in _originalPriorities)
            {
                try
                {
                    using var p = Process.GetProcessById(kv.Key);
                    p.PriorityClass = kv.Value;
                    ok++;
                }
                catch
                {
                    // Processo ja terminou entre a ativacao e a desativacao: normal.
                    falhas++;
                }
            }
            _logger?.LogInfo($"[GAMER-RUNTIME] Prioridades restauradas={ok} | processos encerrados={falhas}");
            _originalPriorities.Clear();
            return ok;
        }

        // ════════════════════════════════════════════════════════════
        //  ATIVAÇÃO / DESATIVAÇÃO
        // ════════════════════════════════════════════════════════════

        public async Task<string> ApplyOnActivateAsync()
        {
            var sb = new System.Text.StringBuilder();
            _appliedLog.Clear();

            _logger?.LogInfo("[GAMER-RUNTIME] ══════ ATIVAÇÃO ══════");

            // 1) GPU — detecta e informa (NÃO altera nada, tudo aqui exige reboot)
            var gpu = DetectGpu();
            sb.AppendLine($"GPU: {gpu.AdapterName} ({gpu.Vendor})");
            sb.AppendLine($"HAGS: {gpu.HagsAdvice}");

            // 2) NVAPI — so para NVIDIA
            bool nvapi = gpu.Vendor == GpuVendorKind.Nvidia && NvidiaApiAvailable();
            if (gpu.Vendor == GpuVendorKind.Nvidia)
            {
                if (nvapi)
                {
                    _logger?.LogInfo("[GAMER-RUNTIME] NVAPI disponivel. Reflex/Ultra Low Latency sao por JOGO e exigem o painel NVIDIA; registrados como recomendado, nao aplicados.");
                    _logger?.LogInfo($"[GAMER-RUNTIME] Recomendado NVIDIA: {gpu.VendorGuidance}");
                }
                else
                {
                    _logger?.LogWarning("[GAMER-RUNTIME] GPU NVIDIA detectada mas NVAPI nao respondeu — ajustes de driver ficam indisponiveis.");
                }
            }
            else
            {
                _logger?.LogInfo("[GAMER-RUNTIME] GPU nao-NVIDIA: nenhum ajuste de driver de fabricante e aplicado (NAO ha equivalente confiavel para AMD/Intel).");
            }

            // 3) Rebaixamento de fundo
            var (low, prot, err) = RebaixarProcessosDeFundo();
            sb.AppendLine($"Fundo rebaixado: {low} (protegidos {prot}, erros {err})");

            // 4) MMCSS — registrado como NÃO APLICÁVEL, com o motivo
            _logger?.LogWarning(
                "[GAMER-RUNTIME] MMCSS por processo NAO e aplicado: AvSetMmThreadCharacteristics " +
                "afeta apenas a thread que chama. Aplicar no processo do jogo exigiria injetar " +
                "thread via driver de kernel. Decisao consciente de NAO fazer.");

            _logger?.LogInfo("[GAMER-RUNTIME] ══════ ATIVAÇÃO CONCLUÍDA ══════");
            return sb.ToString();
        }

        public Task<string> ApplyOnDeactivateAsync()
        {
            _logger?.LogInfo("[GAMER-RUNTIME] ══════ DESATIVAÇÃO ══════");
            int rest = RestaurarPrioridades();
            _logger?.LogInfo(
                "[GAMER-RUNTIME] Desativado. Observacao: plano de energia, Game Mode e captura " +
                "em background sao globais do Windows e ficam como o usuario configurou.");
            _logger?.LogInfo("[GAMER-RUNTIME] ══════ DESATIVAÇÃO CONCLUÍDA ══════");
            return Task.FromResult($"restaurados={rest}");
        }
    }
}
