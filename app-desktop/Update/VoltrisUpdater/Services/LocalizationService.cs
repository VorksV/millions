using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace VoltrisUpdater.Services
{
    public class LocalizationService
    {
        private static LocalizationService _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private Language _currentLanguage;
        private Dictionary<string, string> _currentDictionary;

        public event Action LanguageChanged;

        private LocalizationService()
        {
            string systemLanguage = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.ToLower();

            if (systemLanguage == "pt")
                SetLanguage(Language.Portuguese);
            else if (systemLanguage == "es")
                SetLanguage(Language.Spanish);
            else
                SetLanguage(Language.English);
        }

        public void SetLanguage(Language language)
        {
            _currentLanguage = language;
            LoadDictionary();
            LanguageChanged?.Invoke();
        }

        public Language CurrentLanguage => _currentLanguage;

        public string GetString(string key)
        {
            if (_currentDictionary != null && _currentDictionary.TryGetValue(key, out string value))
            {
                return value;
            }
            return key;
        }

        private void LoadDictionary()
        {
            _currentDictionary = new Dictionary<string, string>();

            switch (_currentLanguage)
            {
                case Language.Portuguese:
                    _currentDictionary.Add("UpdaterTitle", "Instalando Nova Atualização");
                    _currentDictionary.Add("Preparing", "Preparando...");
                    _currentDictionary.Add("WaitingAppClose", "Aguardando encerramento do aplicativo...");
                    _currentDictionary.Add("ExtractingFiles", "Extraindo arquivos...");
                    _currentDictionary.Add("ReadingPackage", "Lendo pacote de atualização...");
                    _currentDictionary.Add("NoPayload", "Nenhum payload externo detectado. Tentando ler recurso embutido...");
                    _currentDictionary.Add("EmbeddedExtracted", "Payload embutido extraído para: ");
                    _currentDictionary.Add("Installing", "Instalando...");
                    _currentDictionary.Add("ReplacingFiles", "Substituindo arquivos antigos...");
                    _currentDictionary.Add("Copying", "Copiando: {0}");
                    _currentDictionary.Add("ErrorExtracting", "Erro extraindo arquivo: ");
                    _currentDictionary.Add("Finalizing", "Finalizando...");
                    _currentDictionary.Add("StartingApp", "Iniciando aplicativo...");
                    _currentDictionary.Add("ExeNotFound", "Executável não encontrado para reiniciar: ");
                    _currentDictionary.Add("FatalError", "ERRO FATAL: ");
                    _currentDictionary.Add("ErrorTitle", "Erro");
                    _currentDictionary.Add("ErrorMessage", "Erro durante a atualização:\n{0}");
                    _currentDictionary.Add("InvalidPayload", "Caminho do payload de atualização inválido ou não encontrado, e não há payload embutido no atualizador.");
                    break;

                case Language.English:
                    _currentDictionary.Add("UpdaterTitle", "Installing New Update");
                    _currentDictionary.Add("Preparing", "Preparing...");
                    _currentDictionary.Add("WaitingAppClose", "Waiting for application to close...");
                    _currentDictionary.Add("ExtractingFiles", "Extracting files...");
                    _currentDictionary.Add("ReadingPackage", "Reading update package...");
                    _currentDictionary.Add("NoPayload", "No external payload detected. Attempting to read embedded resource...");
                    _currentDictionary.Add("EmbeddedExtracted", "Embedded payload extracted to: ");
                    _currentDictionary.Add("Installing", "Installing...");
                    _currentDictionary.Add("ReplacingFiles", "Replacing old files...");
                    _currentDictionary.Add("Copying", "Copying: {0}");
                    _currentDictionary.Add("ErrorExtracting", "Error extracting file: ");
                    _currentDictionary.Add("Finalizing", "Finalizing...");
                    _currentDictionary.Add("StartingApp", "Starting application...");
                    _currentDictionary.Add("ExeNotFound", "Executable not found for restart: ");
                    _currentDictionary.Add("FatalError", "FATAL ERROR: ");
                    _currentDictionary.Add("ErrorTitle", "Error");
                    _currentDictionary.Add("ErrorMessage", "Error during update:\n{0}");
                    _currentDictionary.Add("InvalidPayload", "Invalid update payload path or not found, and no embedded payload in the updater.");
                    break;

                case Language.Spanish:
                    _currentDictionary.Add("UpdaterTitle", "Instalando Nueva Actualización");
                    _currentDictionary.Add("Preparing", "Preparando...");
                    _currentDictionary.Add("WaitingAppClose", "Esperando a que se cierre la aplicación...");
                    _currentDictionary.Add("ExtractingFiles", "Extrayendo archivos...");
                    _currentDictionary.Add("ReadingPackage", "Leyendo paquete de actualización...");
                    _currentDictionary.Add("NoPayload", "No se detectó payload externo. Intentando leer recurso integrado...");
                    _currentDictionary.Add("EmbeddedExtracted", "Payload integrado extraído a: ");
                    _currentDictionary.Add("Installing", "Instalando...");
                    _currentDictionary.Add("ReplacingFiles", "Reemplazando archivos antiguos...");
                    _currentDictionary.Add("Copying", "Copiando: {0}");
                    _currentDictionary.Add("ErrorExtracting", "Error extrayendo archivo: ");
                    _currentDictionary.Add("Finalizing", "Finalizando...");
                    _currentDictionary.Add("StartingApp", "Iniciando aplicación...");
                    _currentDictionary.Add("ExeNotFound", "Ejecutable no encontrado para reiniciar: ");
                    _currentDictionary.Add("FatalError", "ERROR FATAL: ");
                    _currentDictionary.Add("ErrorTitle", "Error");
                    _currentDictionary.Add("ErrorMessage", "Error durante la actualización:\n{0}");
                    _currentDictionary.Add("InvalidPayload", "Ruta de payload de actualización no válida o no encontrada, y no hay payload integrado en el actualizador.");
                    break;
            }
        }
    }
}
