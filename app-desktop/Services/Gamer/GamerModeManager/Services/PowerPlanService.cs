using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Management;
using VoltrisOptimizer.Core.Constants;
using VoltrisOptimizer.Utils.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager
{
    public enum PowerPlanType
    {
        Balanced,
        HighPerformance,
        UltimatePerformance,
        PowerSaver
    }

    public interface IPowerPlanService : IDisposable
    {
        (string Guid, string Name) GetActivePowerPlan();
        (string Guid, string Name) SetPowerPlan(PowerPlanType type);
        bool SetPowerPlanByGuid(string guid);
        IReadOnlyList<(string Guid, string Name)> GetAvailablePlans();
    }

    public class PowerPlanService : IPowerPlanService
    {
[Obsolete("Use VoltrisOptimizer.Core.Constants.PowerPlanLock.IsLocked instead")]
        public static bool PowerPlanLocked
        {
            get => PowerPlanLock.IsLocked;
            set => PowerPlanLock.IsLocked = value;
        }

        private readonly ILoggingService _logger;

        private static readonly Dictionary<PowerPlanType, string> StandardGuids = new()
        {
            { PowerPlanType.Balanced, "381b4222-f694-41f0-9685-ff5bb260df2e" },
            { PowerPlanType.HighPerformance, "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" },
            { PowerPlanType.UltimatePerformance, "e9a42b02-d5df-448d-aa00-03f14749eb61" },
            { PowerPlanType.PowerSaver, "a1841308-3541-4fab-bc81-f71556f20b4a" }
        };

        private static readonly Guid ProcessorSubGroup = Guid.Parse("54533251-82be-4824-96c1-47b60b740d00");
        private static readonly Guid PerfBoostMode = Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7");
        private static readonly Guid MinProcessorState = Guid.Parse("893dee8e-2bef-41e0-89c6-b55d0929964c");
        private static readonly Guid MaxProcessorState = Guid.Parse("bc5038f7-23e0-4960-96da-33abaf5935ec");
        private static readonly Guid SystemCoolingPolicy = Guid.Parse("94d3a615-a899-4ac5-ae2b-e4d8f634367f");
        private static readonly Guid UsbSubGroup = Guid.Parse("2a737441-1930-4402-8d77-b2bebba308a3");
        private static readonly Guid UsbSelectiveSuspend = Guid.Parse("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");
        private static readonly Guid PciExpressSubGroup = Guid.Parse("501a4d13-42af-4429-9fd1-a8218c268e20");
        private static readonly Guid PciExpressAspm = Guid.Parse("ee12f906-d277-404b-b6da-e5fa1a576df5");

        private const uint AccessScheme = 16;
        private const uint ErrorNoMoreItems = 259;

        public PowerPlanService(ILoggingService logger)
        {
            _logger.LogEntry(nameof(PowerPlanService));
_logger = logger;
            _logger.LogExit(nameof(PowerPlanService));
}

        public (string Guid, string Name) GetActivePowerPlan()
        {
            try
            {
                if (PowerNativeMethods.TryGetActiveSchemeGuid(out var guid))
                {
                    var name = PowerNativeMethods.GetPlanName(guid);
return (guid.ToString().ToLowerInvariant(), name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PowerPlan] Erro ao obter plano ativo: {ex.Message}");
            }
return ("", "Desconhecido");
        }

        public (string Guid, string Name) SetPowerPlan(PowerPlanType type)
        {
            try
            {
                if (type == PowerPlanType.UltimatePerformance || type == PowerPlanType.HighPerformance)
                {
                    if (DetectIsApuSystem())
                    {
                        _logger.LogInfo("[PowerPlan] ⚡ Sistema APU-Only detectado (iGPU sem placa dedicada).");
                        _logger.LogInfo("[PowerPlan] 🛡️ Ignorando totalmente alterações no plano de energia.");
                        _logger.LogInfo("[PowerPlan] Motivo: Travar a CPU em 100% rouba a energia da iGPU, derrubando os FPS.");
                        var current = GetActivePowerPlan();
return current;
                    }
                }

                if (type == PowerPlanType.UltimatePerformance)
                {
                    var plans = GetAvailablePlans();
                    var ultimate = plans.FirstOrDefault(p =>
                        p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase) ||
                        p.Guid.Equals(StandardGuids[PowerPlanType.UltimatePerformance], StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(ultimate.Guid))
                    {
                        if (SetPowerPlanByGuid(ultimate.Guid))
return (ultimate.Guid, ultimate.Name);
                    }

                    // [FIX:UNICO-DONO-DE-ENERGIA] O Modo Gamer nao cria mais
                    // planos. Se o "Ultimate Performance" nao existir no sistema,
                    // o certo e' deixar o Perfil Inteligente decidir o plano a
                    // partir do perfil escolhido e da capacidade da maquina —
                    // e nao fabricar um plano com valores inventados aqui.
                    _logger.LogInfo(
                        "[PowerPlan] Ultimate Performance ausente e criacao neutralizada. " +
                        "O Perfil Inteligente decide o plano; o Modo Gamer nao fabrica mais planos.");
                    type = PowerPlanType.HighPerformance;
                    _logger.LogInfo("[PowerPlan] Ultimate Performance não disponível, usando High Performance");
                }

                if (StandardGuids.TryGetValue(type, out var guid))
                {
                    if (SetPowerPlanByGuid(guid))
                    {
                        var name = type.ToString();
return (guid, name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PowerPlan] Erro ao definir plano: {ex.Message}");
            }
return GetActivePowerPlan();
        }

        public bool SetPowerPlanByGuid(string guid)
        {
            _logger.LogEntry(nameof(SetPowerPlanByGuid));
if (PowerPlanLock.IsLocked)
            {
                _logger.LogDebug($"[PowerPlan] [LOCKED] Alteração de plano bloqueada pelo Modo Gamer — ignorando GUID: {guid}");
                return false;
            }

            try
            {
                var schemeGuid = Guid.Parse(guid);
                var result = ProfilePowerAuthority.RequestProfileApply("GamerMode", "pedido legado de troca de plano", _logger);

                if (!result)
                {
                    _logger.LogWarning($"[PowerPlan] Orchestrator recusou plano {guid}");
                    return false;
                }

                var current = GetActivePowerPlan();
                return current.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PowerPlan] Erro ao definir plano {guid}: {ex.Message}");
                return false;
            }
            _logger.LogExit(nameof(SetPowerPlanByGuid));
}

        public IReadOnlyList<(string Guid, string Name)> GetAvailablePlans()
        {
try
            {
                return GetPlansNative().AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PowerPlan] Erro ao listar planos: {ex.Message}");
                return Array.Empty<(string, string)>();
            }
}

        private List<(string Guid, string Name)> GetPlansNative()
        {
var plans = new List<(string, string)>();
            uint index = 0;
            uint bufferSize = 16;
            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);

            try
            {
                while (true)
                {
                    bufferSize = 16;
                    uint res = PowerNativeMethods.PowerEnumerate(
                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        AccessScheme, index, buffer, ref bufferSize);

                    if (res == ErrorNoMoreItems) break;
                    if (res == 0)
                    {
                        var guid = Marshal.PtrToStructure<Guid>(buffer);
                        var name = PowerNativeMethods.GetPlanName(guid);
                        plans.Add((guid.ToString().ToLowerInvariant(), name));
                    }
                    index++;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return plans;
}

        // [FIX:UNICO-DONO-DE-ENERGIA] O Modo Gamer nao cria mais planos de energia.
        //
        // Este serviço era a segunda porta de entrada da energia: o Modo Gamer
        // duplicava o plano "Ultimate Performance", renomeava, gravava seis
        // settings (boost, min, max, cooling, USB, PCIe) e ativava o resultado.
        //
        // Tudo isso saia do Perfil Inteligente, que é o dono. A consequencia nao
        // era so duplicidade: os seis valores gravados aqui eram CHAMADOS de
        // "ultimate" e nao coincidiam com a tabela do perfil. Dois donos para os
        // mesmos quatro settings, com numeros diferentes, e nenhuma forma de o
        // Perfil saber que o outro dono tinha passado por cima.
        //
        // A porta legítima do Modo Gamer passou a ser mudar o PERFIL — o
        // RequestProfile abaixo. O plano vem como consequência.
        private bool TryCreateUltimatePerformancePlan()
        {
            _logger.LogEntry(nameof(TryCreateUltimatePerformancePlan));
            try
            {
                _logger.LogInfo(
                    "[PowerPlan] Criacao de plano 'Ultimate Performance' neutralizada. " +
                    "O Perfil Inteligente e' o unico dono dos planos de energia; o Modo Gamer " +
                    "muda o perfil, e o plano vem como consequencia.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PowerPlan] Falha ao neutralizar criacao de plano: {ex.Message}");
                return false;
            }
            finally
            {
                _logger.LogExit(nameof(TryCreateUltimatePerformancePlan));
            }
        }

        // =================================================================
        // [FIX:UNICO-DONO-DE-ENERGIA] OS TRÊS MÉTODOS SEGUINTES FORAM
        // NEUTRALIZADOS, E ESTÃO AQUI POR UM MOTIVO ESPECÍFICO.
        //
        // Eles gravavam energia fora do Perfil Inteligente:
        //   TryDuplicateScheme -> PowerDuplicateScheme (cria plano)
        //   RenameScheme       -> PowerWriteFriendlyName (renomeia plano)
        //   ApplyUltimateSettings -> 6x ApplyPowerSetting (boost, min, max,
        //                               cooling, USB, PCIe)
        //
        // A tentação seria apagá-los. Não foram apagados porque são a prova
        // documental de que a segunda porta existiu: qualquer pessoa que
        // rodar uma auditoria amanhã precisa ver que estes seis writes
        // estavam aqui, e não pode concluir que "sempre esteve certo".
        //
        // Eles permanecem como stubs que registram a recusa. Apagar a
        // evidência do defeito é como se ele nunca tivesse acontecido — e o
        // próximo a reimplementar "só o boost" faz exatamente o mesmo
        // caminho, porque vai ler a tabela do perfil e achar que o boost é
        // dele.
        // =================================================================

        private bool TryDuplicateScheme(Guid sourceGuid, out Guid newGuid, out string name)
        {
            newGuid = Guid.Empty;
            name = "";
            _logger.LogWarning(
                "[PowerPlan] TryDuplicateScheme RECUSADO: criar planos é privilégio do " +
                "Perfil Inteligente. O Modo Gamer muda o perfil, não o plano.");
            return false;
        }

        private void RenameScheme(Guid schemeGuid, string displayName, string description)
        {
            _logger.LogWarning(
                $"[PowerPlan] RenameScheme RECUSADO: o plano '{displayName}' não é criado " +
                "nada renomeado. Quem nomeia planos é o Perfil Inteligente.");
        }

        private void ApplyUltimateSettings(Guid schemeGuid)
        {
            _logger.LogWarning(
                "[PowerPlan] ApplyUltimateSettings RECUSADO: boost, min, max, cooling, " +
                "USB e PCIe são valores do Perfil Inteligente. Gravar aqui por fora da " +
                "tabela é a segunda via de escrita que esta refatoração eliminou.");
        }

        private bool DetectIsApuSystem()
        {
            _logger.LogEntry(nameof(DetectIsApuSystem));
try
            {
                bool hasIntegrated = false;
                bool hasDedicated = false;

                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                {
                    foreach (ManagementBaseObject baseObj in searcher.Get())
                    {
                        using (var obj = (ManagementObject)baseObj)
{
                            string name = obj["Name"]?.ToString() ?? "";
                            if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Vega", StringComparison.OrdinalIgnoreCase))
                            {
                                hasIntegrated = true;
                            }

                            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("RX ", StringComparison.OrdinalIgnoreCase))
                            {
                                hasDedicated = true;
                            }
                        }
                    }
                }
                return hasIntegrated && !hasDedicated;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[PowerPlan] Falha ao detectar APU: {ex.Message}");
                return false;
            }
            _logger.LogExit(nameof(DetectIsApuSystem));
}

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            _logger.LogExit(nameof(Dispose));
}
}
}
