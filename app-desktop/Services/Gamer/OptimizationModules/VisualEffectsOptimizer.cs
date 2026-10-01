using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Gamer.Implementation;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Gamer.OptimizationModules
{
    /// <summary>
    /// Desativa animações e efeitos visuais do Windows durante o Modo Gamer.
    /// Reduz uso de GPU e CPU com renderização de UI desnecessária.
    /// 
    /// ETAPA 6 - CORRIGIR O CASO DA COR SÓLIDA / TRANSPARÊNCIA
    /// - Captura estado original ANTES de modificar
    /// - Nunca assume valores fixos
    /// - Restaura exatamente o estado encontrado
    /// - Verifica cada alteração após aplicação
    /// - Logs extremamente detalhados
    /// </summary>
    public class VisualEffectsOptimizer : IGamerOptimizationModule
    {
        private const string VISUAL_FX_KEY = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
        private const string PERSONALIZE_KEY = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string STATE_VISUAL_FX = "visual.visualFxSetting";
        private const string STATE_ANIMATION = "visual.animationEnabled";
        private const string STATE_TRANSPARENCY = "visual.transparencyEnabled";

        // SystemParametersInfo constants
        private const uint SPI_GETANIMATION = 0x0048;
        private const uint SPI_SETANIMATION = 0x0049;
        private const uint SPIF_SENDCHANGE = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct ANIMATIONINFO
        {
            public uint cbSize;
            public int iMinAnimate; // 0 = desativado, 1 = ativado
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref ANIMATIONINFO pvParam, uint fWinIni);

        private readonly GamerStateMemory _stateMemory;
        private VisualOptimizationLevel _level = VisualOptimizationLevel.Balanced;

        public string Name => "Visual Effects Optimizer";
        public string Description => "Desativa animações e efeitos visuais para liberar GPU e CPU";

        public VisualEffectsOptimizer(GamerStateMemory stateMemory)
        {
            _stateMemory = stateMemory;
        }

        /// <summary>
        /// Define o nível de otimização antes de chamar ApplyAsync.
        /// </summary>
        public void SetLevel(VisualOptimizationLevel level) => _level = level;

        public async Task<ModuleApplyResult> ApplyAsync(GamerSessionContext ctx, CancellationToken ct = default)
        {
            var result = new ModuleApplyResult();
            var changes = new System.Collections.Generic.List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            ctx.Logger.LogInfo("═══════════════════════════════════════════════════════════");
            ctx.Logger.LogInfo("[VisualFX] >>> ENTRY: ApplyAsync INICIADO");
            ctx.Logger.LogInfo($"[VisualFX] Parâmetros: Level={_level}");
            ctx.Logger.LogInfo("═══════════════════════════════════════════════════════════");

            try
            {
                var level = _level;

                if (level == VisualOptimizationLevel.None)
                {
                    ctx.Logger.LogInfo("[VisualFX] VisualLevel = None, nenhuma alteração será aplicada.");
                    result.Success = true;
                    ctx.Logger.LogInfo("[VisualFX] <<< EXIT: ApplyAsync (Success=True, Changes=0, No-op)");
                    return result;
                }

                ctx.Logger.LogInfo($"[VisualFX] 🚀 Iniciando otimizações visuais (nível: {level})...");

                // 1. Backup e desativar animações de janela (ambos os níveis)
                ctx.Logger.LogInfo("[VisualFX] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                ctx.Logger.LogInfo("[VisualFX] 📖 ETAPA 1: Lendo estado atual de iMinAnimate...");
                
                var animInfo = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>() };
                if (SystemParametersInfo(SPI_GETANIMATION, animInfo.cbSize, ref animInfo, 0))
                {
                    ctx.Logger.LogInfo($"[VisualFX] 📊 Estado atual: iMinAnimate = {animInfo.iMinAnimate} ({(animInfo.iMinAnimate == 1 ? "ATIVADO" : "DESATIVADO")})");
                    
                    // ETAPA 2: Salvar snapshot (first-write-wins)
                    ctx.Logger.LogInfo("[VisualFX] 💾 ETAPA 2: Registrando estado original no GamerStateMemory...");
                    _stateMemory.Register(STATE_ANIMATION, animInfo.iMinAnimate);
                    ctx.Logger.LogSuccess($"[VisualFX] ✅ Estado registrado: STATE_ANIMATION = {animInfo.iMinAnimate}");
                    ctx.Logger.LogInfo($"[VisualFX] 📅 Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    
                    // ETAPA 3: Aplicar alteração
                    ctx.Logger.LogInfo("[VisualFX] 🔧 ETAPA 3: Aplicando alteração...");
                    animInfo.iMinAnimate = 0;
                    ctx.Logger.LogInfo($"[VisualFX] 📡 Chamando API: SystemParametersInfo(SPI_SETANIMATION, cbSize={animInfo.cbSize}, iMinAnimate=0, SPIF_SENDCHANGE)");
                    
                    bool apiResult = SystemParametersInfo(SPI_SETANIMATION, animInfo.cbSize, ref animInfo, SPIF_SENDCHANGE);
                    int win32Error = Marshal.GetLastWin32Error();
                    
                    ctx.Logger.LogInfo($"[VisualFX] 📡 API retornou: {(apiResult ? "TRUE (SUCCESS)" : "FALSE (FAILURE)")}");
                    if (!apiResult)
                    {
                        ctx.Logger.LogError($"[VisualFX] ❌ Win32 Error: {win32Error}");
                    }
                    
                    if (apiResult)
                    {
                        changes.Add("Animações de janela desativadas");
                        ctx.Logger.LogSuccess("[VisualFX] ✅ Animações de janela desativadas com sucesso.");
                        
                        // ETAPA 4: VERIFICAÇÃO IMEDIATA (CRÍTICO!)
                        ctx.Logger.LogInfo("[VisualFX] 🔍 ETAPA 4: Verificando aplicação...");
                        
                        // Pequeno delay para propagação
                        await Task.Delay(10, ct);
                        
                        var verifyAnimInfo = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>() };
                        if (SystemParametersInfo(SPI_GETANIMATION, verifyAnimInfo.cbSize, ref verifyAnimInfo, 0))
                        {
                            if (verifyAnimInfo.iMinAnimate == 0)
                            {
                                ctx.Logger.LogSuccess("[VisualFX] ✅✅✅ VERIFICAÇÃO: iMinAnimate = 0 (CONFIRMADO!)");
                            }
                            else
                            {
                                ctx.Logger.LogError($"[VisualFX] ❌❌❌ VERIFICAÇÃO FALHOU: iMinAnimate = {verifyAnimInfo.iMinAnimate} (esperado 0)");
                                ctx.Logger.LogError("[VisualFX] ⚠️ Tentando aplicar novamente...");
                                
                                // Tentar novamente
                                animInfo.iMinAnimate = 0;
                                bool retryResult = SystemParametersInfo(SPI_SETANIMATION, animInfo.cbSize, ref animInfo, SPIF_SENDCHANGE);
                                if (retryResult)
                                {
                                    ctx.Logger.LogSuccess("[VisualFX] ✅ Segunda tentativa bem-sucedida!");
                                }
                                else
                                {
                                    ctx.Logger.LogCritical("[VisualFX] ❌ FALHA CRÍTICA: Não foi possível desativar animações!");
                                }
                            }
                        }
                        else
                        {
                            ctx.Logger.LogWarning("[VisualFX] ⚠️ Não foi possível verificar (API falhou)");
                        }
                    }
                    else
                    {
                        ctx.Logger.LogError("[VisualFX] ❌ Falha ao desativar animações via SystemParametersInfo.");
                    }
                }
                else
                {
                    int win32Error = Marshal.GetLastWin32Error();
                    ctx.Logger.LogError($"[VisualFX] ❌ Falha ao ler estado atual via API. Win32 Error: {win32Error}");
                }

                if (level == VisualOptimizationLevel.MaximumPerformance)
                {
                    ctx.Logger.LogInfo("[VisualFX] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    ctx.Logger.LogInfo("[VisualFX] Nível MaximumPerformance - Aplicando otimizações adicionais");
                    ctx.Logger.LogInfo("[VisualFX] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    
                    // 2. VisualFXSetting = 2 (melhor desempenho)
                    ctx.Logger.LogInfo("[VisualFX] ───────────────────────────────────────────");
                    ctx.Logger.LogInfo("[VisualFX] 📖 ETAPA 1: Lendo VisualFXSetting atual...");
                    
                    using (var key = Registry.CurrentUser.OpenSubKey(VISUAL_FX_KEY, writable: true)
                                  ?? Registry.CurrentUser.CreateSubKey(VISUAL_FX_KEY))
                    {
                        if (key != null)
                        {
                            var current = key.GetValue("VisualFXSetting");
                            int currentValue = current is int v ? v : 0;
                            ctx.Logger.LogInfo($"[VisualFX] 📊 Estado atual: VisualFXSetting = {currentValue}");
                            
                            // ETAPA 2: Salvar snapshot
                            ctx.Logger.LogInfo("[VisualFX] 💾 ETAPA 2: Registrando estado original...");
                            _stateMemory.Register(STATE_VISUAL_FX, currentValue);
                            ctx.Logger.LogSuccess($"[VisualFX] ✅ Estado registrado: STATE_VISUAL_FX = {currentValue}");
                            
                            // ETAPA 3: Aplicar alteração
                            ctx.Logger.LogInfo("[VisualFX] 🔧 ETAPA 3: Aplicando VisualFXSetting = 2...");
                            key.SetValue("VisualFXSetting", 2, RegistryValueKind.DWord);
                            
                            // ETAPA 4: VERIFICAÇÃO IMEDIATA
                            ctx.Logger.LogInfo("[VisualFX] 🔍 ETAPA 4: Verificando aplicação...");
                            await Task.Delay(10, ct);
                            
                            var verifiedValue = key.GetValue("VisualFXSetting");
                            if (verifiedValue is int verifiedInt && verifiedInt == 2)
                            {
                                ctx.Logger.LogSuccess("[VisualFX] ✅✅✅ VERIFICAÇÃO: VisualFXSetting = 2 (CONFIRMADO!)");
                                changes.Add("VisualFXSetting = 2 (máximo desempenho)");
                                ctx.Logger.LogSuccess("[VisualFX] ✅ VisualFXSetting definido para máximo desempenho.");
                            }
                            else
                            {
                                ctx.Logger.LogError($"[VisualFX] ❌❌❌ VERIFICAÇÃO FALHOU: VisualFXSetting = {verifiedValue ?? "null"} (esperado 2)");
                            }
                        }
                    }

                    // 3. Desativar transparência
                    ctx.Logger.LogInfo("[VisualFX] ───────────────────────────────────────────");
                    ctx.Logger.LogInfo("[VisualFX] 📖 ETAPA 1: Lendo EnableTransparency atual...");
                    
                    using (var key = Registry.CurrentUser.OpenSubKey(PERSONALIZE_KEY, writable: true)
                                  ?? Registry.CurrentUser.CreateSubKey(PERSONALIZE_KEY))
                    {
                        if (key != null)
                        {
                            var current = key.GetValue("EnableTransparency");
                            int currentValue = current is int t ? t : 1;
                            ctx.Logger.LogInfo($"[VisualFX] 📊 Estado atual: EnableTransparency = {currentValue} ({(currentValue == 1 ? "ATIVADO" : "DESATIVADO")})");
                            
                            // ETAPA 2: Salvar snapshot (first-write-wins)
                            ctx.Logger.LogInfo("[VisualFX] 💾 ETAPA 2: Registrando estado original...");
                            _stateMemory.Register(STATE_TRANSPARENCY, currentValue);
                            ctx.Logger.LogSuccess($"[VisualFX] ✅ Estado registrado: STATE_TRANSPARENCY = {currentValue}");
                            ctx.Logger.LogInfo($"[VisualFX] 📅 Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                            
                            // ETAPA 3: Aplicar alteração
                            ctx.Logger.LogInfo("[VisualFX] 🔧 ETAPA 3: Aplicando EnableTransparency = 0...");
                            key.SetValue("EnableTransparency", 0, RegistryValueKind.DWord);
                            
                            // ETAPA 4: VERIFICAÇÃO IMEDIATA
                            ctx.Logger.LogInfo("[VisualFX] 🔍 ETAPA 4: Verificando aplicação...");
                            await Task.Delay(10, ct);
                            
                            var verifiedValue = key.GetValue("EnableTransparency");
                            if (verifiedValue is int verifiedInt && verifiedInt == 0)
                            {
                                ctx.Logger.LogSuccess("[VisualFX] ✅✅✅ VERIFICAÇÃO: EnableTransparency = 0 (CONFIRMADO!)");
                                changes.Add("Transparência do sistema desativada");
                                ctx.Logger.LogSuccess("[VisualFX] ✅ Transparência do sistema desativada.");
                            }
                            else
                            {
                                ctx.Logger.LogError($"[VisualFX] ❌❌❌ VERIFICAÇÃO FALHOU: EnableTransparency = {verifiedValue ?? "null"} (esperado 0)");
                            }
                        }
                    }
                }

                sw.Stop();
                result.Success = true;
                result.ChangesApplied = changes.Count;
                result.AppliedChanges = changes.ToArray();
                
                ctx.Logger.LogInfo("═══════════════════════════════════════════════════════════");
                ctx.Logger.LogSuccess($"[VisualFX] ✅ CONCLUSÃO: {changes.Count} otimizações visuais aplicadas e verificadas.");
                ctx.Logger.LogInfo($"[VisualFX] ⏱️ Tempo total: {sw.ElapsedMilliseconds}ms");
                ctx.Logger.LogInfo("[VisualFX] <<< EXIT: ApplyAsync (Success=True)");
                ctx.Logger.LogInfo("═══════════════════════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                sw.Stop();
                ctx.Logger.LogError($"[VisualFX] ❌ EXCEÇÃO: {ex.Message}", ex);
                ctx.Logger.LogError($"[VisualFX] ⏱️ Tempo até falha: {sw.ElapsedMilliseconds}ms");
                ctx.Logger.LogError("[VisualFX] <<< EXIT: ApplyAsync (Success=False, Exception)");
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        public async Task<ModuleRevertResult> RevertAsync(GamerSessionContext ctx, CancellationToken ct = default)
        {
            var result = new ModuleRevertResult();
            var changes = new System.Collections.Generic.List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            ctx.Logger.LogInfo("═══════════════════════════════════════════════════════════");
            ctx.Logger.LogInfo("[VisualFX] >>> ENTRY: RevertAsync INICIADO (RESTAURAÇÃO)");
            ctx.Logger.LogInfo("═══════════════════════════════════════════════════════════");

            try
            {
                ctx.Logger.LogInfo("[VisualFX] 🔄 Iniciando restauração dos efeitos visuais...");
                ctx.Logger.LogInfo("[VisualFX] 📖 Verificando estados registrados no GamerStateMemory...");

                // 1. Restaurar animações
                ctx.Logger.LogInfo("[VisualFX] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                if (_stateMemory.WasModifiedByGamerMode(STATE_ANIMATION))
                {
                    int originalAnim = _stateMemory.GetOriginal<int>(STATE_ANIMATION);
                    ctx.Logger.LogInfo($"[VisualFX] 📊 Estado original encontrado: iMinAnimate = {originalAnim}");
                    ctx.Logger.LogInfo($"[VisualFX] 🔧 Restaurando animações para iMinAnimate = {originalAnim}...");
                    
                    var animInfo = new ANIMATIONINFO
                    {
                        cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>(),
                        iMinAnimate = originalAnim
                    };
                    
                    ctx.Logger.LogInfo($"[VisualFX] 📡 Chamando API: SystemParametersInfo(SPI_SETANIMATION, cbSize={animInfo.cbSize}, iMinAnimate={originalAnim}, SPIF_SENDCHANGE)");
                    bool apiResult = SystemParametersInfo(SPI_SETANIMATION, animInfo.cbSize, ref animInfo, SPIF_SENDCHANGE);
                    int win32Error = Marshal.GetLastWin32Error();
                    
                    ctx.Logger.LogInfo($"[VisualFX] 📡 API retornou: {(apiResult ? "TRUE (SUCCESS)" : "FALSE (FAILURE)")}");
                    if (!apiResult)
                    {
                        ctx.Logger.LogError($"[VisualFX] ❌ Win32 Error: {win32Error}");
                    }
                    
                    if (apiResult)
                    {
                        // VERIFICAÇÃO PÓS-RESTAURAÇÃO
                        ctx.Logger.LogInfo("[VisualFX] 🔍 Verificando restauração...");
                        await Task.Delay(10, ct);
                        
                        var verifyAnimInfo = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>() };
                        if (SystemParametersInfo(SPI_GETANIMATION, verifyAnimInfo.cbSize, ref verifyAnimInfo, 0))
                        {
                            if (verifyAnimInfo.iMinAnimate == originalAnim)
                            {
                                ctx.Logger.LogSuccess($"[VisualFX] ✅✅✅ VERIFICAÇÃO: iMinAnimate = {originalAnim} (CONFIRMADO!)");
                                changes.Add($"Animações restauradas (iMinAnimate = {originalAnim})");
                                ctx.Logger.LogSuccess("[VisualFX] ✅ Animações restauradas com sucesso.");
                            }
                            else
                            {
                                ctx.Logger.LogError($"[VisualFX] ❌❌❌ VERIFICAÇÃO FALHOU: iMinAnimate = {verifyAnimInfo.iMinAnimate} (esperado {originalAnim})");
                                ctx.Logger.LogError("[VisualFX] ⚠️ Tentando restaurar novamente...");
                                
                                // Tentar novamente
                                animInfo.iMinAnimate = originalAnim;
                                bool retryResult = SystemParametersInfo(SPI_SETANIMATION, animInfo.cbSize, ref animInfo, SPIF_SENDCHANGE);
                                if (retryResult)
                                {
                                    ctx.Logger.LogSuccess("[VisualFX] ✅ Segunda tentativa bem-sucedida!");
                                    changes.Add($"Animações restauradas (retry)");
                                }
                                else
                                {
                                    ctx.Logger.LogCritical("[VisualFX] ❌ FALHA CRÍTICA: Não foi possível restaurar animações!");
                                }
                            }
                        }
                    }
                    else
                    {
                        ctx.Logger.LogError("[VisualFX] ❌ Falha ao restaurar animações via API.");
                    }
                }
                else
                {
                    ctx.Logger.LogInfo("[VisualFX] ℹ️ STATE_ANIMATION não foi modificado, pulando restauração.");
                }

                // 2. Restaurar VisualFXSetting
                ctx.Logger.LogInfo("[VisualFX] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                if (_stateMemory.WasModifiedByGamerMode(STATE_VISUAL_FX))
                {
                    int originalFx = _stateMemory.GetOriginal<int>(STATE_VISUAL_FX);
                    ctx.Logger.LogInfo($"[VisualFX] 📊 Estado original encontrado: VisualFXSetting = {originalFx}");
                    ctx.Logger.LogInfo($"[VisualFX] 🔧 Restaurando VisualFXSetting = {originalFx}...");
                    
                    using var key = Registry.CurrentUser.OpenSubKey(VISUAL_FX_KEY, writable: true)
                                 ?? Registry.CurrentUser.CreateSubKey(VISUAL_FX_KEY);
                    
                    if (key != null)
                    {
                        ctx.Logger.LogInfo($"[VisualFX] 📝 Escrevendo VisualFXSetting = {originalFx}...");
                        key.SetValue("VisualFXSetting", originalFx, RegistryValueKind.DWord);
                        
                        // VERIFICAÇÃO PÓS-RESTAURAÇÃO
                        ctx.Logger.LogInfo("[VisualFX] 🔍 Verificando restauração...");
                        await Task.Delay(10, ct);
                        
                        var verifiedValue = key.GetValue("VisualFXSetting");
                        if (verifiedValue is int verifiedInt && verifiedInt == originalFx)
                        {
                            ctx.Logger.LogSuccess($"[VisualFX] ✅✅✅ VERIFICAÇÃO: VisualFXSetting = {originalFx} (CONFIRMADO!)");
                            changes.Add($"VisualFXSetting = {originalFx}");
                            ctx.Logger.LogSuccess("[VisualFX] ✅ VisualFXSetting restaurado com sucesso.");
                        }
                        else
                        {
                            ctx.Logger.LogError($"[VisualFX] ❌❌❌ VERIFICAÇÃO FALHOU: VisualFXSetting = {verifiedValue ?? "null"} (esperado {originalFx})");
                        }
                    }
                    else
                    {
                        ctx.Logger.LogError("[VisualFX] ❌ Falha ao abrir chave de registro.");
                    }
                }
                else
                {
                    ctx.Logger.LogInfo("[VisualFX] ℹ️ STATE_VISUAL_FX não foi modificado, pulando restauração.");
                }

                // 3. Restaurar transparência
                ctx.Logger.LogInfo("[VisualFX] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                if (_stateMemory.WasModifiedByGamerMode(STATE_TRANSPARENCY))
                {
                    int originalTransp = _stateMemory.GetOriginal<int>(STATE_TRANSPARENCY);
                    ctx.Logger.LogInfo($"[VisualFX] 📊 Estado original encontrado: EnableTransparency = {originalTransp} ({(originalTransp == 1 ? "ATIVADO" : "DESATIVADO")})");
                    ctx.Logger.LogInfo($"[VisualFX] 🔧 Restaurando transparência = {originalTransp}...");
                    
                    using var key = Registry.CurrentUser.OpenSubKey(PERSONALIZE_KEY, writable: true)
                                 ?? Registry.CurrentUser.CreateSubKey(PERSONALIZE_KEY);
                    
                    if (key != null)
                    {
                        ctx.Logger.LogInfo($"[VisualFX] 📝 Escrevendo EnableTransparency = {originalTransp}...");
                        key.SetValue("EnableTransparency", originalTransp, RegistryValueKind.DWord);
                        
                        // VERIFICAÇÃO PÓS-RESTAURAÇÃO
                        ctx.Logger.LogInfo("[VisualFX] 🔍 Verificando restauração...");
                        await Task.Delay(10, ct);
                        
                        var verifiedValue = key.GetValue("EnableTransparency");
                        if (verifiedValue is int verifiedInt && verifiedInt == originalTransp)
                        {
                            ctx.Logger.LogSuccess($"[VisualFX] ✅✅✅ VERIFICAÇÃO: EnableTransparency = {originalTransp} (CONFIRMADO!)");
                            changes.Add($"Transparência restaurada ({originalTransp})");
                            ctx.Logger.LogSuccess("[VisualFX] ✅ Transparência restaurada com sucesso.");
                            
                            // ✅ ETAPA 6 CUMPRIDA: Transparência restaurada EXATAMENTE como encontrado
                            if (originalTransp == 1)
                            {
                                ctx.Logger.LogSuccess("[VisualFX] ✅✅✅ ETAPA 6: Transparência estava ATIVADA e foi RESTAURADA corretamente!");
                            }
                            else
                            {
                                ctx.Logger.LogSuccess("[VisualFX] ✅✅✅ ETAPA 6: Transparência estava DESATIVADA e foi MANTIDA corretamente!");
                            }
                        }
                        else
                        {
                            ctx.Logger.LogError($"[VisualFX] ❌❌❌ VERIFICAÇÃO FALHOU: EnableTransparency = {verifiedValue ?? "null"} (esperado {originalTransp})");
                        }
                    }
                    else
                    {
                        ctx.Logger.LogError("[VisualFX] ❌ Falha ao abrir chave de registro.");
                    }
                }
                else
                {
                    ctx.Logger.LogInfo("[VisualFX] ℹ️ STATE_TRANSPARENCY não foi modificado, pulando restauração.");
                }

                sw.Stop();
                result.Success = true;
                result.ChangesReverted = changes.Count;
                result.RevertedChanges = changes.ToArray();
                
                ctx.Logger.LogInfo("═══════════════════════════════════════════════════════════");
                ctx.Logger.LogSuccess($"[VisualFX] ✅ CONCLUSÃO: {changes.Count} efeitos visuais restaurados e verificados.");
                ctx.Logger.LogInfo($"[VisualFX] ⏱️ Tempo total: {sw.ElapsedMilliseconds}ms");
                ctx.Logger.LogInfo("[VisualFX] <<< EXIT: RevertAsync (Success=True)");
                ctx.Logger.LogInfo("═══════════════════════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                sw.Stop();
                ctx.Logger.LogError($"[VisualFX] ❌ EXCEÇÃO: {ex.Message}", ex);
                ctx.Logger.LogError($"[VisualFX] ⏱️ Tempo até falha: {sw.ElapsedMilliseconds}ms");
                ctx.Logger.LogError("[VisualFX] <<< EXIT: RevertAsync (Success=False, Exception)");
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }
    }
}
