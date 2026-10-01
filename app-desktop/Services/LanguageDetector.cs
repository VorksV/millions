using System;
using System.Globalization;

namespace VoltrisOptimizer.Services
{
    public static class LanguageDetector
    {
        public static Language DetectWindowsLanguage()
        {
            try
            {
                // Obter a cultura de UI atual e a cultura de UI instalada do Windows
                var uiCulture = CultureInfo.CurrentUICulture;
                var installedCulture = CultureInfo.InstalledUICulture;

                LanguageLogger.Log("==================== DETECÇÃO AUTOMÁTICA DE IDIOMA DO WINDOWS ====================");
                LanguageLogger.Log($"Cultura de UI Atual (CultureInfo.CurrentUICulture): Name='{uiCulture.Name}', DisplayName='{uiCulture.DisplayName}', TwoLetterISOLanguageName='{uiCulture.TwoLetterISOLanguageName}'");
                LanguageLogger.Log($"Cultura Instalada (CultureInfo.InstalledUICulture): Name='{installedCulture.Name}', DisplayName='{installedCulture.DisplayName}', TwoLetterISOLanguageName='{installedCulture.TwoLetterISOLanguageName}'");

                // Determinar o melhor candidato de cultura
                string cultureName = uiCulture.Name.ToLowerInvariant();
                string isoName = uiCulture.TwoLetterISOLanguageName.ToLowerInvariant();

                LanguageLogger.Log($"Iniciando mapeamento inteligente para a cultura: Name='{cultureName}', ISO='{isoName}'");

                // 1. Verificar variantes de Português (pt-BR, pt-PT, etc.)
                if (cultureName.StartsWith("pt") || isoName == "pt")
                {
                    LanguageLogger.Log("Cultura mapeada com sucesso para: Language.Portuguese (PT-BR)");
                    return Language.Portuguese;
                }

                // 2. Verificar variantes de Espanhol (es-ES, es-MX, es-AR, etc.)
                if (cultureName.StartsWith("es") || isoName == "es")
                {
                    LanguageLogger.Log("Cultura mapeada com sucesso para: Language.Spanish (ES)");
                    return Language.Spanish;
                }

                // 3. Verificar variantes de Inglês (en-US, en-GB, en-CA, etc.)
                if (cultureName.StartsWith("en") || isoName == "en")
                {
                    LanguageLogger.Log("Cultura mapeada com sucesso para: Language.English (EN-US)");
                    return Language.English;
                }

                // 4. Fallback principal para outros idiomas não suportados nativamente (Alemão, Italiano, Japonês, etc.)
                LanguageLogger.Log($"Cultura '{cultureName}' não possui suporte nativo direto na aplicação. Aplicando Fallback Principal: Language.English (EN-US)");
                return Language.English;
            }
            catch (Exception ex)
            {
                LanguageLogger.Log($"Erro durante a execução da detecção de idioma: {ex.Message}. Aplicando Fallback de Segurança: Language.English (EN-US)");
                return Language.English;
            }
        }
    }
}
