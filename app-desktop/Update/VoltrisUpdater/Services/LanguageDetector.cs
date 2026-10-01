using System;
using System.Globalization;

namespace VoltrisUpdater.Services
{
    public enum Language
    {
        Portuguese,
        Spanish,
        English
    }

    public static class LanguageDetector
    {
        private static readonly string LogFilePath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "Voltris", "Uninstaller", "language.log");

        private static void Log(string message)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                
                System.IO.File.AppendAllText(LogFilePath, 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [LANG_DETECTOR] {message}{Environment.NewLine}");
            }
            catch { }
        }

        public static Language DetectWindowsLanguage()
        {
            try
            {
                var uiCulture = CultureInfo.CurrentUICulture;
                var installedCulture = CultureInfo.InstalledUICulture;

                Log("==================== DETECÇÃO AUTOMÁTICA DE IDIOMA DO WINDOWS ====================");
                Log($"Cultura de UI Atual (CultureInfo.CurrentUICulture): Name='{uiCulture.Name}', DisplayName='{uiCulture.DisplayName}', TwoLetterISOLanguageName='{uiCulture.TwoLetterISOLanguageName}'");
                Log($"Cultura Instalada (CultureInfo.InstalledUICulture): Name='{installedCulture.Name}', DisplayName='{installedCulture.DisplayName}', TwoLetterISOLanguageName='{installedCulture.TwoLetterISOLanguageName}'");

                string cultureName = uiCulture.Name.ToLowerInvariant();
                string isoName = uiCulture.TwoLetterISOLanguageName.ToLowerInvariant();

                Log($"Iniciando mapeamento inteligente para a cultura: Name='{cultureName}', ISO='{isoName}'");

                if (cultureName.StartsWith("pt") || isoName == "pt")
                {
                    Log("Cultura mapeada com sucesso para: Language.Portuguese (PT-BR)");
                    return Language.Portuguese;
                }

                if (cultureName.StartsWith("es") || isoName == "es")
                {
                    Log("Cultura mapeada com sucesso para: Language.Spanish (ES)");
                    return Language.Spanish;
                }

                if (cultureName.StartsWith("en") || isoName == "en")
                {
                    Log("Cultura mapeada com sucesso para: Language.English (EN-US)");
                    return Language.English;
                }

                Log($"Cultura '{cultureName}' não possui suporte nativo direto na aplicação. Aplicando Fallback Principal: Language.English (EN-US)");
                return Language.English;
            }
            catch (Exception ex)
            {
                Log($"Erro durante a execução da detecção de idioma: {ex.Message}. Aplicando Fallback de Segurança: Language.English (EN-US)");
                return Language.English;
            }
        }
    }
}
