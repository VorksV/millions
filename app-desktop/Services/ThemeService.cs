using System;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Enum para os tipos de tema disponíveis
    /// </summary>
    public enum ThemeType
    {
        Dark,
        Light,
        Auto
    }

    /// <summary>
    /// Serviço centralizado para gerenciamento de temas da aplicação
    /// </summary>
    public class ThemeService
    {
        private static ThemeService? _instance;
        public static ThemeService Instance => _instance ??= new ThemeService();

        private readonly ILoggingService? _logger;
        private ThemeType _currentTheme = ThemeType.Dark;
        private bool _isWindowsDarkMode = true;

        public event EventHandler<ThemeType>? ThemeChanged;

        /// <summary>
        /// Tema atual aplicado
        /// </summary>
        public ThemeType CurrentTheme => _currentTheme;

        /// <summary>
        /// Indica se o Windows está em modo escuro
        /// </summary>
        public bool IsWindowsDarkMode => _isWindowsDarkMode;

        /// <summary>
        /// Tema efetivo (considerando Auto)
        /// </summary>
        public ThemeType EffectiveTheme => _currentTheme == ThemeType.Auto 
            ? (_isWindowsDarkMode ? ThemeType.Dark : ThemeType.Light) 
            : _currentTheme;

        private ThemeService()
        {
            _logger = App.LoggingService;
            DetectWindowsTheme();
            
            // Monitorar mudanças no tema do Windows
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        }

        /// <summary>
        /// Detecta o tema atual do Windows
        /// </summary>
        public void DetectWindowsTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    var value = key.GetValue("AppsUseLightTheme");
                    if (value != null)
                    {
                        _isWindowsDarkMode = (int)value == 0;
                        _logger?.LogInfo($"[THEME] Windows está em modo: {(_isWindowsDarkMode ? "Escuro" : "Claro")}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[THEME] Não foi possível detectar tema do Windows: {ex.Message}");
                _isWindowsDarkMode = true; // Padrão para dark
            }
        }

        /// <summary>
        /// Callback quando as preferências do sistema mudam
        /// </summary>
        private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                var previousWindowsMode = _isWindowsDarkMode;
                DetectWindowsTheme();
                
                // Se estamos em modo Auto e o tema do Windows mudou, reaplicar
                if (_currentTheme == ThemeType.Auto && previousWindowsMode != _isWindowsDarkMode)
                {
                    _logger?.LogInfo("[THEME] Tema do Windows mudou, reaplicando tema automático...");
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        ApplyTheme(ThemeType.Auto);
                    });
                }
            }
        }

        /// <summary>
        /// Aplica um tema à aplicação
        /// </summary>
        /// <param name="theme">Tipo de tema a aplicar</param>
        public void ApplyTheme(ThemeType theme)
        {
            try
            {
                _currentTheme = theme;
                
                // Determinar qual tema visual aplicar
                var effectiveTheme = theme == ThemeType.Auto 
                    ? (_isWindowsDarkMode ? ThemeType.Dark : ThemeType.Light) 
                    : theme;

                _logger?.LogInfo($"[THEME] Aplicando tema: {theme} (Efetivo: {effectiveTheme})");

                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        var dicts = Application.Current.Resources.MergedDictionaries;
                        
                        // Remover tema anterior (Dark ou Light)
                        for (int i = dicts.Count - 1; i >= 0; i--)
                        {
                            var src = dicts[i].Source?.ToString() ?? string.Empty;
                            if (src.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                                src.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase))
                            {
                                dicts.RemoveAt(i);
                                _logger?.LogInfo($"[THEME] Removido: {src}");
                            }
                        }

                        // Adicionar novo tema (antes dos Styles para que os estilos usem as cores corretas)
                        var themePath = effectiveTheme == ThemeType.Light 
                            ? "/UI/Themes/LightTheme.xaml" 
                            : "/UI/Themes/DarkTheme.xaml";

                        // Encontrar a posição correta (após Colors.xaml, antes de Styles.xaml)
                        int insertIndex = 1; // Após Aero2 e Colors
                        for (int i = 0; i < dicts.Count; i++)
                        {
                            var src = dicts[i].Source?.ToString() ?? string.Empty;
                            if (src.EndsWith("Colors.xaml", StringComparison.OrdinalIgnoreCase))
                            {
                                insertIndex = i + 1;
                                break;
                            }
                        }

                        var themeDict = new ResourceDictionary 
                        { 
                            Source = new Uri(themePath, UriKind.Relative) 
                        };
                        
                        if (insertIndex < dicts.Count)
                        {
                            dicts.Insert(insertIndex, themeDict);
                        }
                        else
                        {
                            dicts.Add(themeDict);
                        }

                        _logger?.LogSuccess($"[THEME] Tema {effectiveTheme} aplicado com sucesso");
                        
                        // Notificar mudança
                        ThemeChanged?.Invoke(this, effectiveTheme);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"[THEME] Erro ao aplicar tema: {ex.Message}", ex);
                    }
                });

                // Salvar configuração
                try
                {
                    var settings = SettingsService.Instance.Settings;
                    settings.Theme = theme.ToString();
                    SettingsService.Instance.SaveSettings();
                }
                catch { }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[THEME] Erro crítico ao aplicar tema: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Aplica tema a partir de string (para compatibilidade)
        /// </summary>
        public void ApplyTheme(string themeName)
        {
            if (Enum.TryParse<ThemeType>(themeName, true, out var theme))
            {
                ApplyTheme(theme);
            }
            else
            {
                _logger?.LogWarning($"[THEME] Tema desconhecido: {themeName}, aplicando Dark");
                ApplyTheme(ThemeType.Dark);
            }
        }

        /// <summary>
        /// Carrega e aplica o tema salvo nas configurações
        /// </summary>
        public void LoadSavedTheme()
        {
            try
            {
                var settings = SettingsService.Instance.Settings;
                var themeName = settings.Theme ?? "Dark";
                
                if (Enum.TryParse<ThemeType>(themeName, true, out var theme))
                {
                    ApplyTheme(theme);
                }
                else
                {
                    ApplyTheme(ThemeType.Dark);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[THEME] Erro ao carregar tema salvo: {ex.Message}");
                ApplyTheme(ThemeType.Dark);
            }
        }

        /// <summary>
        /// Alterna entre os temas
        /// </summary>
        public void ToggleTheme()
        {
            var newTheme = EffectiveTheme == ThemeType.Dark ? ThemeType.Light : ThemeType.Dark;
            ApplyTheme(newTheme);
        }

        /// <summary>
        /// Obtém o nome amigável do tema
        /// </summary>
        public static string GetThemeDisplayName(ThemeType theme)
        {
            return theme switch
            {
                ThemeType.Dark => "Escuro",
                ThemeType.Light => "Claro",
                ThemeType.Auto => "Automático (Windows)",
                _ => "Escuro"
            };
        }

        /// <summary>
        /// Obtém o tipo de tema a partir do nome amigável
        /// </summary>
        public static ThemeType GetThemeFromDisplayName(string displayName)
        {
            return displayName switch
            {
                "Escuro" or "Dark" => ThemeType.Dark,
                "Claro" or "Light" => ThemeType.Light,
                "Automático (Windows)" or "Auto" => ThemeType.Auto,
                _ => ThemeType.Dark
            };
        }

        /// <summary>
        /// Libera recursos
        /// </summary>
        public void Dispose()
        {
            SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        }
    }
}

