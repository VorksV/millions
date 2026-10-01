using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Gamer.Implementation;

namespace VoltrisOptimizer.Services.Gamer.OptimizationModules
{
    /// <summary>
    /// Desativa globalmente a execução de apps UWP em segundo plano durante o Modo Gamer.
    /// Libera CPU e memória consumidos por apps como Notícias, Clima, Xbox, etc.
    /// </summary>
    public class UwpBackgroundAppsModule : IGamerOptimizationModule
    {
        private const string SUBKEY = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
        private const string VALUE_NAME = "GlobalUserDisabled";
        private const string STATE_KEY = "uwp.globalUserDisabled";

        private readonly GamerStateMemory _stateMemory;

        public string Name => "UWP Background Apps";
        public string Description => "Desativa apps UWP em segundo plano para liberar CPU e memória";

        public UwpBackgroundAppsModule(GamerStateMemory stateMemory)
        {
            _stateMemory = stateMemory;
        }

        public Task<ModuleApplyResult> ApplyAsync(GamerSessionContext ctx, CancellationToken ct = default)
        {
            var result = new ModuleApplyResult();
            try
            {
                ctx.Logger.LogInfo("[UwpBg] Verificando apps UWP em segundo plano...");

                using var key = Registry.CurrentUser.OpenSubKey(SUBKEY, writable: true)
                             ?? Registry.CurrentUser.CreateSubKey(SUBKEY, writable: true);

                if (key == null)
                {
                    ctx.Logger.LogWarning("[UwpBg] Chave de registro não encontrada, pulando.");
                    result.Success = true;
                    return Task.FromResult(result);
                }

                var current = key.GetValue(VALUE_NAME);
                int originalValue = current is int i ? i : 0; // padrão = ativado (0 = apps podem rodar)

                _stateMemory.Register(STATE_KEY, originalValue);

                if (originalValue == 1)
                {
                    ctx.Logger.LogInfo("[UwpBg] Apps UWP em background já estavam desativados, nenhuma alteração necessária.");
                    result.Success = true;
                    result.AppliedChanges = Array.Empty<string>();
                    return Task.FromResult(result);
                }

                key.SetValue(VALUE_NAME, 1, RegistryValueKind.DWord);
                ctx.Logger.LogSuccess("[UwpBg] ✅ Apps UWP em segundo plano desativados.");
                result.Success = true;
                result.ChangesApplied = 1;
                result.AppliedChanges = new[] { "GlobalUserDisabled = 1" };
            }
            catch (Exception ex)
            {
                ctx.Logger.LogError($"[UwpBg] ❌ Erro ao desativar apps UWP: {ex.Message}", ex);
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            return Task.FromResult(result);
        }

        public Task<ModuleRevertResult> RevertAsync(GamerSessionContext ctx, CancellationToken ct = default)
        {
            var result = new ModuleRevertResult();
            try
            {
                if (!_stateMemory.WasModifiedByGamerMode(STATE_KEY))
                {
                    ctx.Logger.LogInfo("[UwpBg] Nenhum estado registrado para restaurar.");
                    result.Success = true;
                    return Task.FromResult(result);
                }

                int original = _stateMemory.GetOriginal<int>(STATE_KEY);
                ctx.Logger.LogInfo($"[UwpBg] Restaurando GlobalUserDisabled = {original}...");

                using var key = Registry.CurrentUser.OpenSubKey(SUBKEY, writable: true)
                             ?? Registry.CurrentUser.CreateSubKey(SUBKEY, writable: true);

                key?.SetValue(VALUE_NAME, original, RegistryValueKind.DWord);
                ctx.Logger.LogSuccess($"[UwpBg] ✅ Apps UWP restaurados para valor original ({original}).");
                result.Success = true;
                result.ChangesReverted = 1;
                result.RevertedChanges = new[] { $"GlobalUserDisabled = {original}" };
            }
            catch (Exception ex)
            {
                ctx.Logger.LogError($"[UwpBg] ❌ Erro ao restaurar apps UWP: {ex.Message}", ex);
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            return Task.FromResult(result);
        }
    }
}
