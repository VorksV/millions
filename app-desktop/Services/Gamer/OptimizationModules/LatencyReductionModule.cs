using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VoltrisOptimizer.Services.Gamer.OptimizationModules
{
    /// <summary>
    /// Módulo de redução de latência - otimizações de input lag e latência de rede
    /// 
    /// REUTILIZA: IRegistryService (não duplica lógica de registry)
    /// </summary>
    public class LatencyReductionModule : IGamerOptimizationModule
    {
        public string Name => "Latency Reduction";
        public string Description => "Reduz latência de input e rede para melhor responsividade";

        public async Task<ModuleApplyResult> ApplyAsync(GamerSessionContext ctx, CancellationToken ct = default)
        {
            var result = new ModuleApplyResult();
            var changes = new System.Collections.Generic.List<string>();

            try
            {
                ctx.Logger.LogInfo("[LatencyReduction] Aplicando otimizações de latência...");

                // 1. Desativar Fullscreen Optimizations (reduz input lag)
                // Registry: HKEY_CURRENT_USER\System\GameConfigStore\windowsCompatible
                var fsoKey = @"System\GameConfigStore";
                var fsoValue = "windowsCompatible";
                
                var currentFso = ctx.RegistryService.GetValue<int>(RegistryHive.CurrentUser, fsoKey, fsoValue, 0);
                ctx.RollbackRegistry.RegisterRegistryKey(RegistryHive.CurrentUser, fsoKey, fsoValue, currentFso, RegistryValueKind.DWord);
                
                var fsoResult = ctx.RegistryService.SetValue(RegistryHive.CurrentUser, fsoKey, fsoValue, 1, RegistryValueKind.DWord);
                if (fsoResult.Success)
                {
                    changes.Add("Fullscreen Optimizations desativadas");
                    result.ChangesApplied++;
                }

                // 2. Otimizar TCP/IP para baixa latência
                // CORREÇÃO CRÍTICA: TcpAckFrequency NÃO é mais alterado — forçar ACK imediato
                // causa picos de latência (lag spikes), perda de pacotes e degradação de download
                // em ISPs residenciais. Mantido apenas TcpNoDelay (seguro).
                var tcpParamsKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
                var tcpNoDelay = "TCPNoDelay";

                // TCPNoDelay = 1 (desabilita Nagle)
                var currentNoDelay = ctx.RegistryService.GetValue<int>(RegistryHive.LocalMachine, tcpParamsKey, tcpNoDelay, 0);
                ctx.RollbackRegistry.RegisterRegistryKey(RegistryHive.LocalMachine, tcpParamsKey, tcpNoDelay, currentNoDelay, RegistryValueKind.DWord);
                
                var noDelayResult = ctx.RegistryService.SetValue(RegistryHive.LocalMachine, tcpParamsKey, tcpNoDelay, 1, RegistryValueKind.DWord);
                if (noDelayResult.Success)
                {
                    changes.Add("TCP No Delay habilitado");
                    result.ChangesApplied++;
                }

                // 3. Otimizar timer resolution (via registry - requer reinicialização para efeito completo)
                // HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\kernel\DpcTimeout
                var kernelKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
                var dpcTimeout = "DpcTimeout";
                
                var currentDpc = ctx.RegistryService.GetValue<uint>(RegistryHive.LocalMachine, kernelKey, dpcTimeout, 0);
                if (currentDpc != 4294967295U) // Só registrar se não estiver já otimizado
                {
                    ctx.RollbackRegistry.RegisterRegistryKey(RegistryHive.LocalMachine, kernelKey, dpcTimeout, currentDpc, RegistryValueKind.DWord);
                    
                    var dpcResult = ctx.RegistryService.SetValue(RegistryHive.LocalMachine, kernelKey, dpcTimeout, unchecked((int)4294967295), RegistryValueKind.DWord);
                    if (dpcResult.Success)
                    {
                        changes.Add("DPC Timeout otimizado");
                        result.ChangesApplied++;
                    }
                }

                result.Success = true;
                result.AppliedChanges = changes.ToArray();
                ctx.Logger.LogSuccess($"[LatencyReduction] {result.ChangesApplied} otimizações aplicadas");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                ctx.Logger.LogError($"[LatencyReduction] Erro ao aplicar: {ex.Message}", ex);
            }

            return await Task.FromResult(result);
        }

        public async Task<ModuleRevertResult> RevertAsync(GamerSessionContext ctx, CancellationToken ct = default)
        {
            var result = new ModuleRevertResult();
            var changes = new System.Collections.Generic.List<string>();

            try
            {
                ctx.Logger.LogInfo("[LatencyReduction] Revertendo otimizações de latência...");

                // Reverter todas as chaves de registry registradas
                var registrySnapshots = ctx.RollbackRegistry.GetRegistrySnapshots().ToList();
                foreach (var snapshot in registrySnapshots)
                {
                    try
                    {
                        // Restaurar valor original
                        if (snapshot.OriginalValue != null)
                        {
                            object? value = snapshot.OriginalValue;
                            
                            // Converter string para tipo apropriado
                            if (snapshot.ValueKind == RegistryValueKind.DWord)
                            {
                                if (uint.TryParse(snapshot.OriginalValue, out var uintVal))
                                    value = uintVal;
                                else if (int.TryParse(snapshot.OriginalValue, out var intVal))
                                    value = intVal;
                            }

                            var restoreResult = ctx.RegistryService.SetValue(snapshot.Hive, snapshot.SubKey, snapshot.ValueName, value!, snapshot.ValueKind);
                            if (restoreResult.Success)
                            {
                                changes.Add($"Registry restaurado: {snapshot.SubKey}\\{snapshot.ValueName}");
                                result.ChangesReverted++;
                            }
                        }
                        else
                        {
                            // Se não havia valor original, deletar
                            var deleteResult = ctx.RegistryService.DeleteValue(snapshot.Hive, snapshot.SubKey, snapshot.ValueName);
                            if (deleteResult.Success)
                            {
                                changes.Add($"Registry deletado: {snapshot.SubKey}\\{snapshot.ValueName}");
                                result.ChangesReverted++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger.LogWarning($"[LatencyReduction] Erro ao restaurar {snapshot.SubKey}\\{snapshot.ValueName}: {ex.Message}");
                    }
                }

                result.Success = true;
                result.RevertedChanges = changes.ToArray();
                ctx.Logger.LogSuccess($"[LatencyReduction] {result.ChangesReverted} otimizações revertidas");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                ctx.Logger.LogError($"[LatencyReduction] Erro ao reverter: {ex.Message}", ex);
            }

            return await Task.FromResult(result);
        }
    }
}

