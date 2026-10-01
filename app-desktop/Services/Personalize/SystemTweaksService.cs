using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Personalize
{
    // ─── Modelos ────────────────────────────────────────────────────────────────

    public class SystemAnimationState
    {
        public bool WindowAnimations { get; set; }
        public bool MenuAnimations { get; set; }
        public bool TaskbarAnimations { get; set; }
        public bool TooltipAnimations { get; set; }
        public bool ListBoxSmoothScrolling { get; set; }
        public bool CursorShadow { get; set; }
        public bool DropShadows { get; set; }
        public bool FontSmoothing { get; set; }
    }

    public class VisualPerformanceState
    {
        public bool HardwareAcceleration { get; set; }
        public bool DwmComposition { get; set; }
        public bool AeroPeek { get; set; }
        public bool TransparencyEffects { get; set; }
        public bool ReduceMotion { get; set; }
    }

    public enum PersonalizeProfile { Normal, Performance, Ultra, Custom }

    // ─── Service ─────────────────────────────────────────────────────────────────

    public sealed class SystemTweaksService
    {
        private const string TAG = "[SystemTweaks]";
        private readonly ILoggingService _logger;

        // Chaves de registro
        private const string VisualEffectsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
        private const string DesktopKey = @"Control Panel\Desktop";
        private const string WindowMetricsKey = @"Control Panel\Desktop\WindowMetrics";
        private const string DwmKey = @"Software\Microsoft\Windows\DWM";
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string ExplorerKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        public SystemTweaksService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInfo($"{TAG} Instância criada.");
        }

        // ── Leitura do estado atual ──────────────────────────────────────────────

        public Task<SystemAnimationState> GetAnimationStateAsync() => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [GetAnimationStateAsync] Lendo estado de animações...");
            var state = new SystemAnimationState();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(DesktopKey);
                state.WindowAnimations = GetRegInt(key, "MinAnimate", 1) == 1;
                state.MenuAnimations = GetRegInt(key, "MenuShowDelay", 400) < 200;
                state.FontSmoothing = GetRegInt(key, "FontSmoothing", 2) == 2;

                using var advKey = Registry.CurrentUser.OpenSubKey(ExplorerKey);
                state.TaskbarAnimations = GetRegInt(advKey, "TaskbarAnimations", 1) == 1;
                state.ListBoxSmoothScrolling = GetRegInt(advKey, "ListviewShadow", 1) == 1;
                state.DropShadows = GetRegInt(advKey, "ListviewShadow", 1) == 1;
                state.CursorShadow = GetRegInt(advKey, "CursorShadow", 1) == 1;

                _logger.LogInfo($"{TAG} [GetAnimationStateAsync] WindowAnim={state.WindowAnimations} | TaskbarAnim={state.TaskbarAnimations} | FontSmooth={state.FontSmoothing}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [GetAnimationStateAsync] Erro: {ex.Message}", ex);
            }
            return state;
        });

        public Task<VisualPerformanceState> GetVisualPerformanceStateAsync() => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [GetVisualPerformanceStateAsync] Lendo estado de performance visual...");
            var state = new VisualPerformanceState();
            try
            {
                using var dwmKey = Registry.CurrentUser.OpenSubKey(DwmKey);
                state.DwmComposition = GetRegInt(dwmKey, "Composition", 1) == 1;
                state.AeroPeek = GetRegInt(dwmKey, "EnableAeroPeek", 1) == 1;
                state.TransparencyEffects = GetRegInt(dwmKey, "ColorizationOpaqueBlend", 0) == 0;

                using var personKey = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                state.TransparencyEffects = GetRegInt(personKey, "EnableTransparency", 1) == 1;

                // Hardware Acceleration via registro de GPU
                using var gpuKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                state.HardwareAcceleration = GetRegInt(gpuKey, "SystemResponsiveness", 20) <= 20;

                _logger.LogInfo($"{TAG} [GetVisualPerformanceStateAsync] DWM={state.DwmComposition} | Transparency={state.TransparencyEffects} | HWAccel={state.HardwareAcceleration}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [GetVisualPerformanceStateAsync] Erro: {ex.Message}", ex);
            }
            return state;
        });

        // ── Animações ────────────────────────────────────────────────────────────

        public Task SetWindowAnimationsAsync(bool enable) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetWindowAnimationsAsync] enable={enable}");
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(DesktopKey);
                key?.SetValue("MinAnimate", enable ? "1" : "0", RegistryValueKind.String);
                _logger.LogSuccess($"{TAG} [SetWindowAnimationsAsync] MinAnimate={enable}");
            }
            catch (Exception ex) { _logger.LogError($"{TAG} [SetWindowAnimationsAsync] Erro: {ex.Message}", ex); }
        });

        public Task SetMenuAnimationsAsync(bool enable) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetMenuAnimationsAsync] enable={enable}");
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(DesktopKey);
                key?.SetValue("MenuShowDelay", enable ? "400" : "0", RegistryValueKind.String);
                _logger.LogSuccess($"{TAG} [SetMenuAnimationsAsync] MenuShowDelay={( enable ? 400 : 0)}");
            }
            catch (Exception ex) { _logger.LogError($"{TAG} [SetMenuAnimationsAsync] Erro: {ex.Message}", ex); }
        });

        public Task SetTaskbarAnimationsAsync(bool enable) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetTaskbarAnimationsAsync] enable={enable}");
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(ExplorerKey);
                key?.SetValue("TaskbarAnimations", enable ? 1 : 0, RegistryValueKind.DWord);
                _logger.LogSuccess($"{TAG} [SetTaskbarAnimationsAsync] TaskbarAnimations={enable}");
            }
            catch (Exception ex) { _logger.LogError($"{TAG} [SetTaskbarAnimationsAsync] Erro: {ex.Message}", ex); }
        });

        public Task SetFontSmoothingAsync(bool enable) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetFontSmoothingAsync] enable={enable}");
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(DesktopKey);
                key?.SetValue("FontSmoothing", enable ? "2" : "0", RegistryValueKind.String);
                key?.SetValue("FontSmoothingType", enable ? 2 : 0, RegistryValueKind.DWord);
                _logger.LogSuccess($"{TAG} [SetFontSmoothingAsync] FontSmoothing={enable}");
            }
            catch (Exception ex) { _logger.LogError($"{TAG} [SetFontSmoothingAsync] Erro: {ex.Message}", ex); }
        });

        /// <summary>
        /// Sombras das JANELAS.
        ///
        /// CORREÇÃO: a implementação anterior gravava
        /// <c>HKCU\...\Explorer\Advanced\ListviewShadow</c>, que controla a sombra da
        /// LISTA DE ARQUIVOS do Explorer — não a sombra das janelas prometida pelo
        /// rótulo da interface. O efeito visual descrito nunca ocorria.
        ///
        /// A chave correta é <c>HKCU\...\Themes\Personalize\EnableWindowShadows</c>.
        /// </summary>
        public async Task<bool> SetDropShadowsAsync(bool enable)
        {
            _logger.LogInfo($"{TAG} [SetDropShadowsAsync] enable={enable}");
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
                    if (key == null)
                    {
                        _logger.LogError($"{TAG} [SetDropShadowsAsync] Não foi possível abrir '{PersonalizeKey}'.");
                        return false;
                    }

                    key.SetValue("EnableWindowShadows", enable ? 1 : 0, RegistryValueKind.DWord);

                    // Read-back obrigatório.
                    key.GetValue("EnableWindowShadows");
                    var readBack = Convert.ToInt32(key.GetValue("EnableWindowShadows") ?? 0);
                    bool confirmed = readBack == (enable ? 1 : 0);

                    if (!confirmed)
                    {
                        _logger.LogError($"{TAG} [SetDropShadowsAsync] Gravado mas leitura retornou {readBack}. NÃO confirmado.");
                        return false;
                    }

                    BroadcastSettingsChange();
                    _logger.LogSuccess($"{TAG} [SetDropShadowsAsync] EnableWindowShadows={readBack} gravado e CONFIRMADO.");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{TAG} [SetDropShadowsAsync] Erro: {ex.Message}", ex);
                    return false;
                }
            });
        }

        // ── Transparência ────────────────────────────────────────────────────────

        public Task SetTransparencyEffectsAsync(bool enable) => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [SetTransparencyEffectsAsync] enable={enable}");
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
                key?.SetValue("EnableTransparency", enable ? 1 : 0, RegistryValueKind.DWord);
                BroadcastSettingsChange();
                _logger.LogSuccess($"{TAG} [SetTransparencyEffectsAsync] EnableTransparency={enable}");
            }
            catch (Exception ex) { _logger.LogError($"{TAG} [SetTransparencyEffectsAsync] Erro: {ex.Message}", ex); }
        });

        /// <summary>
        /// Aceleração de Hardware de vídeo.
        ///
        /// CORREÇÃO: a implementação anterior gravava
        /// <c>HKLM\...\Multimedia\SystemProfile\SystemResponsiveness</c>, que é o peso
        /// de RESPONSABILIDADE DO AGENDADOR DE CPU (escala 0-100, padrão 10) e não tem
        /// relação alguma com aceleração de hardware de GPU. O rótulo da interface
        /// prometia "GPU com prioridade máxima"; o efeito descrito nunca era alcançado.
        ///
        /// A opção real de aceleração de hardware é por APLICATIVO:
        /// <c>HKCU\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration</c>
        /// (afeta apenas WPF) e, para o Explorer, a opção "Usar aceleração de hardware
        /// de GPU quando disponível" em Opções de Desempenho, controlada pelo
        /// GraphicsDriver e pelo painel "Efeitos Visuais".
        ///
        /// Como não existe uma única chave global que ative a aceleração de hardware,
        /// o VOLTRIS agora grava a opção que realmente controla o WPF do próprio
        /// aplicativo e informa o que foi feito — em vez de escrever um registro
        /// sem relação com o rótulo.
        /// </summary>
        public async Task<bool> SetHardwareAccelerationAsync(bool enable)
        {
            _logger.LogInfo($"{TAG} [SetHardwareAccelerationAsync] enable={enable}");
            return await Task.Run(() =>
            {
                try
                {
                    const string avalonKey = @"SOFTWARE\Microsoft\Avalon.Graphics";
                    using var key = Registry.CurrentUser.CreateSubKey(avalonKey);
                    if (key == null)
                    {
                        _logger.LogError($"{TAG} [SetHardwareAccelerationAsync] Não foi possível abrir '{avalonKey}'.");
                        return false;
                    }

                    // 0 = habilitada (padrão), 1 = desabilitada.
                    key.SetValue("DisableHWAcceleration", enable ? 0 : 1, RegistryValueKind.DWord);

                    var readBack = Convert.ToInt32(key.GetValue("DisableHWAcceleration") ?? -1);
                    bool confirmed = readBack == (enable ? 0 : 1);
                    if (!confirmed)
                    {
                        _logger.LogError($"{TAG} [SetHardwareAccelerationAsync] Gravado mas leitura retornou {readBack}. NÃO confirmado.");
                        return false;
                    }

                    _logger.LogSuccess(
                        $"{TAG} [SetHardwareAccelerationAsync] Aceleração de hardware de vídeo " +
                        $"{(enable ? "HABILITADA" : "DESABILITADA")} para aplicações WPF (DisableHWAcceleration={readBack}). " +
                        "Observação: a aceleração de hardware da área de trabalho é uma opção global do Windows " +
                        "em Opções de Desempenho e não é alterável por registro.");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{TAG} [SetHardwareAccelerationAsync] Erro: {ex.Message}", ex);
                    return false;
                }
            });
        }

        /// <summary>
        /// Fecha automaticamente aplicações que travam.
        ///
        /// CORREÇÃO: esta função era rotulada "Resposta Alta do Explorer" e descrita
        /// como otimização de VISUAL, mas na verdade escreve timeouts de
        /// encerramento de processo e habilita <c>AutoEndTasks=1</c>, que faz o
        /// Windows MATAR automaticamente aplicações que travam — descartando trabalho
        /// não salvo do usuário sem qualquer aviso. Isso é uma regressão de
        /// estabilidade, não uma melhoria de desempenho.
        ///
        /// A correção aqui é: aplicar somente os timeouts, com valores conservative
        /// que apenas reduzem a espera, e NUNCA ativar AutoEndTasks automaticamente.
        /// O encerramento automático fica disponível apenas como opção explícita.
        /// </summary>
        public async Task<bool> SetExplorerResponsivenessAsync(bool highPerf)
        {
            _logger.LogInfo($"{TAG} [SetExplorerResponsivenessAsync] highPerf={highPerf}");
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(DesktopKey);
                    if (key == null)
                    {
                        _logger.LogError($"{TAG} [SetExplorerResponsivenessAsync] Não foi possível abrir '{DesktopKey}'.");
                        return false;
                    }

                    // HungAppTimeout: tempo até o app ser considerado travado.
                    key.SetValue("HungAppTimeout", highPerf ? "3000" : "5000", RegistryValueKind.String);

                    // WaitToKillAppTimeout: tempo até o app ser encerrado.
                    // O valor original de 20000 (20 s) é mantido no modo normal.
                    key.SetValue("WaitToKillAppTimeout", highPerf ? "5000" : "20000", RegistryValueKind.String);

                    // AutoEndTasks NÃO é habilitado aqui: matar automaticamente
                    // aplicações que travam descarta trabalho do usuário sem
                    // confirmação. Mantém-se o valor que o usuário já tinha.
                    object? autoEndTasks = key.GetValue("AutoEndTasks");
                    _logger.LogInfo(
                        $"{TAG} [SetExplorerResponsivenessAsync] AutoEndTasks preservado como " +
                        $"'{autoEndTasks ?? "(ausente)"}' — o VOLTRIS não habilita encerramento automático de apps, " +
                        "pois isso descarta trabalho não salvo sem aviso.");

                    // Read-back dos timeouts aplicados.
                    var hung = key.GetValue("HungAppTimeout")?.ToString();
                    var wait = key.GetValue("WaitToKillAppTimeout")?.ToString();
                    bool confirmed = hung == (highPerf ? "3000" : "5000") &&
                                     wait == (highPerf ? "5000" : "20000");

                    if (!confirmed)
                    {
                        _logger.LogError(
                            $"{TAG} [SetExplorerResponsivenessAsync] Gravado mas leitura retornou HungAppTimeout={hung}, " +
                            $"WaitToKillAppTimeout={wait}. NÃO confirmado.");
                        return false;
                    }

                    BroadcastSettingsChange();
                    _logger.LogSuccess(
                        $"{TAG} [SetExplorerResponsivenessAsync] Tempos de detecção de travamento aplicados e CONFIRMADOS " +
                        $"(HungAppTimeout={hung}, WaitToKillAppTimeout={wait}).");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{TAG} [SetExplorerResponsivenessAsync] Erro: {ex.Message}", ex);
                    return false;
                }
            });
        }

        /// <summary>
        /// Notifica o shell de que as configurações de usuário mudaram, para que
        /// a alteração seja aplicada sem exigir logout. Sem isso, o Windows só
        /// aplica o valor no próximo login.
        /// </summary>
        private static void BroadcastSettingsChange()
        {
            try
            {
                SendMessageTimeout(new IntPtr(0xffff), 0x001A /*WM_SETTINGCHANGE*/,
                    IntPtr.Zero, "Environment", 0x0002 /*SMTO_ABORTIFHUNG*/, 500, out _);
            }
            catch
            {
                // Notificação émelhor-esforço: a mudança no registro já persiste.
            }
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam,
            string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        // ── Perfis ───────────────────────────────────────────────────────────────

        public async Task ApplyProfileAsync(PersonalizeProfile profile)
        {
            _logger.LogInfo($"{TAG} [ApplyProfileAsync] Aplicando perfil: {profile}");
            switch (profile)
            {
                case PersonalizeProfile.Performance:
                    await SetWindowAnimationsAsync(false);
                    await SetMenuAnimationsAsync(false);
                    await SetTaskbarAnimationsAsync(false);
                    await SetDropShadowsAsync(false);
                    await SetHardwareAccelerationAsync(true);
                    await SetExplorerResponsivenessAsync(true);
                    _logger.LogSuccess($"{TAG} [ApplyProfileAsync] Perfil Performance aplicado.");
                    break;

                case PersonalizeProfile.Ultra:
                    await SetWindowAnimationsAsync(false);
                    await SetMenuAnimationsAsync(false);
                    await SetTaskbarAnimationsAsync(false);
                    await SetDropShadowsAsync(false);
                    await SetFontSmoothingAsync(false);
                    await SetTransparencyEffectsAsync(false);
                    await SetHardwareAccelerationAsync(true);
                    await SetExplorerResponsivenessAsync(true);
                    _logger.LogSuccess($"{TAG} [ApplyProfileAsync] Perfil Ultra aplicado.");
                    break;

                case PersonalizeProfile.Normal:
                    await SetWindowAnimationsAsync(true);
                    await SetMenuAnimationsAsync(true);
                    await SetTaskbarAnimationsAsync(true);
                    await SetDropShadowsAsync(true);
                    await SetFontSmoothingAsync(true);
                    await SetTransparencyEffectsAsync(true);
                    await SetHardwareAccelerationAsync(false);
                    await SetExplorerResponsivenessAsync(false);
                    _logger.LogSuccess($"{TAG} [ApplyProfileAsync] Perfil Normal aplicado.");
                    break;
            }
        }

        // ── Detecção de gargalos visuais ─────────────────────────────────────────

        public Task<string> DetectVisualBottlenecksAsync() => Task.Run(() =>
        {
            _logger.LogInfo($"{TAG} [DetectVisualBottlenecksAsync] Analisando gargalos visuais...");
            var issues = new System.Text.StringBuilder();
            try
            {
                // Verificar DWM
                using var dwmKey = Registry.CurrentUser.OpenSubKey(DwmKey);
                if (GetRegInt(dwmKey, "Composition", 1) == 0)
                    issues.AppendLine("• DWM desativado — pode causar tearing e instabilidade visual.");

                // Verificar animações excessivas
                using var advKey = Registry.CurrentUser.OpenSubKey(ExplorerKey);
                if (GetRegInt(advKey, "TaskbarAnimations", 1) == 1)
                    issues.AppendLine("• Animações da taskbar ativas — impacto leve em CPUs mais antigos.");

                // Verificar timeout do Explorer
                using var deskKey = Registry.CurrentUser.OpenSubKey(DesktopKey);
                var timeout = deskKey?.GetValue("WaitToKillAppTimeout")?.ToString() ?? "20000";
                if (int.TryParse(timeout, out int t) && t > 5000)
                    issues.AppendLine($"• WaitToKillAppTimeout={t}ms — apps travados demoram mais para fechar.");

                var result = issues.Length > 0 ? issues.ToString().Trim() : "Nenhum gargalo visual detectado.";
                _logger.LogInfo($"{TAG} [DetectVisualBottlenecksAsync] Resultado: {result}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [DetectVisualBottlenecksAsync] Erro: {ex.Message}", ex);
                return "Erro ao analisar gargalos.";
            }
        });

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static int GetRegInt(RegistryKey? key, string name, int defaultVal)
        {
            if (key == null) return defaultVal;
            var val = key.GetValue(name);
            if (val == null) return defaultVal;
            return val is int i ? i : int.TryParse(val.ToString(), out int parsed) ? parsed : defaultVal;
        }
    }
}
