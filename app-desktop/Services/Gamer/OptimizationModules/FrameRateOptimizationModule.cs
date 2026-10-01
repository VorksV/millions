using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VoltrisOptimizer.Services.Gamer.OptimizationModules
{
    /// <summary>
    /// Módulo de otimização de frame rate - otimizações para melhorar FPS
    /// 
    /// REUTILIZA: IRegistryService (não duplica lógica de registry)
    /// </summary>
    public class FrameRateOptimizationModule : IGamerOptimizationModule
    {
        public string Name => "Frame Rate Optimizations";
        public string Description => "Otimizações para melhorar FPS e estabilidade de frame rate";

        public async Task<ModuleApplyResult> ApplyAsync(GamerSessionContext ctx, CancellationToken ct = default)
        {
            var result = new ModuleApplyResult();
            var changes = new System.Collections.Generic.List<string>();

            try
            {
                ctx.Logger.LogInfo("[FrameRate] Aplicando otimizações de frame rate...");

                // 1. Desativar GameDVR completamente
                // HKEY_CURRENT_USER\System\GameConfigStore\GameDVR_Enabled
                var gameConfigKey = @"System\GameConfigStore";
                var gameDvrEnabled = "GameDVR_Enabled";
                
                var currentGameDvr = ctx.RegistryService.GetValue<int>(RegistryHive.CurrentUser, gameConfigKey, gameDvrEnabled, 1);
                ctx.RollbackRegistry.RegisterRegistryKey(RegistryHive.CurrentUser, gameConfigKey, gameDvrEnabled, currentGameDvr, RegistryValueKind.DWord);
                
                var gameDvrResult = ctx.RegistryService.SetValue(RegistryHive.CurrentUser, gameConfigKey, gameDvrEnabled, 0, RegistryValueKind.DWord);
                if (gameDvrResult.Success)
                {
                    changes.Add("GameDVR desativado");
                    result.ChangesApplied++;
                }

                // 2. Desativar Game Mode automático
                var autoGameMode = "AutoGameModeEnabled";
                var currentAutoMode = ctx.RegistryService.GetValue<int>(RegistryHive.CurrentUser, gameConfigKey, autoGameMode, 1);
                ctx.RollbackRegistry.RegisterRegistryKey(RegistryHive.CurrentUser, gameConfigKey, autoGameMode, currentAutoMode, RegistryValueKind.DWord);
                
                var autoModeResult = ctx.RegistryService.SetValue(RegistryHive.CurrentUser, gameConfigKey, autoGameMode, 0, RegistryValueKind.DWord);
                if (autoModeResult.Success)
                {
                    changes.Add("Game Mode automático desativado");
                    result.ChangesApplied++;
                }

                // 3. Otimizar DPC Timeout (já pode ter sido feito pelo LatencyReductionModule, mas é idempotente)
                var kernelKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
                var dpcTimeout = "DpcTimeout";
                
                var currentDpc = ctx.RegistryService.GetValue<uint>(RegistryHive.LocalMachine, kernelKey, dpcTimeout, 0);
                if (currentDpc != 4294967295U)
                {
                    // Só registrar se não foi registrado pelo LatencyReductionModule
                    var existingSnapshot = ctx.RollbackRegistry.GetRegistrySnapshots()
                        .FirstOrDefault(s => s.SubKey == kernelKey && s.ValueName == dpcTimeout);
                    
                    if (existingSnapshot == null)
                    {
                        ctx.RollbackRegistry.RegisterRegistryKey(RegistryHive.LocalMachine, kernelKey, dpcTimeout, currentDpc, RegistryValueKind.DWord);
                    }
                    
                    var dpcResult = ctx.RegistryService.SetValue(RegistryHive.LocalMachine, kernelKey, dpcTimeout, unchecked((int)4294967295), RegistryValueKind.DWord);
                    if (dpcResult.Success)
                    {
                        changes.Add("DPC Timeout otimizado");
                        result.ChangesApplied++;
                    }
                }

                // 4. Otimizar GPU Scheduling (HAGS)
                var graphicsKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
                var hwSchMode = "HwSchMode";
                
                var currentHwSch = ctx.RegistryService.GetValue<int>(RegistryHive.LocalMachine, graphicsKey, hwSchMode, 1);
                ctx.RollbackRegistry.RegisterRegistryKey(RegistryHive.LocalMachine, graphicsKey, hwSchMode, currentHwSch, RegistryValueKind.DWord);
                
                // HwSchMode = 2 (ativado)
                var hwSchResult = ctx.RegistryService.SetValue(RegistryHive.LocalMachine, graphicsKey, hwSchMode, 2, RegistryValueKind.DWord);
                if (hwSchResult.Success)
                {
                    changes.Add("Hardware Accelerated GPU Scheduling habilitado");
                    result.ChangesApplied++;
                }

                result.Success = true;
                result.AppliedChanges = changes.ToArray();
                ctx.Logger.LogSuccess($"[FrameRate] {result.ChangesApplied} otimizações aplicadas");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                ctx.Logger.LogError($"[FrameRate] Erro ao aplicar: {ex.Message}", ex);
            }

            return await Task.FromResult(result);
        }

        public async Task<ModuleRevertResult> RevertAsync(GamerSessionContext ctx, CancellationToken ct = default)
        {
            var result = new ModuleRevertResult();
            var changes = new System.Collections.Generic.List<string>();

            try
            {
                ctx.Logger.LogInfo("[FrameRate] Revertendo otimizações de frame rate...");

                // Reverter todas as chaves de registry registradas por este módulo
                var registrySnapshots = ctx.RollbackRegistry.GetRegistrySnapshots()
                    .Where(s => s.SubKey.Contains("GameConfigStore") || s.SubKey.Contains("GraphicsDrivers"))
                    .ToList();

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
                        ctx.Logger.LogWarning($"[FrameRate] Erro ao restaurar {snapshot.SubKey}\\{snapshot.ValueName}: {ex.Message}");
                    }
                }

                result.Success = true;
                result.RevertedChanges = changes.ToArray();
                ctx.Logger.LogSuccess($"[FrameRate] {result.ChangesReverted} otimizações revertidas");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                ctx.Logger.LogError($"[FrameRate] Erro ao reverter: {ex.Message}", ex);
            }

            return await Task.FromResult(result);
        }
    }
}

