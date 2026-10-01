using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace VoltrisUninstaller.Services
{
    public class LocalizationService : INotifyPropertyChanged
    {
        private static LocalizationService? _instance;
        private Language _currentLanguage;
        private Dictionary<string, Dictionary<Language, string>> _strings = null!;

        public static LocalizationService Instance => _instance ??= new LocalizationService();

        public event EventHandler? LanguageChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        public Language CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    LoadStrings();
                    LanguageChanged?.Invoke(this, EventArgs.Empty);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
                }
            }
        }

        private LocalizationService()
        {
            _currentLanguage = Language.Portuguese;
            InitializeStrings();
            LoadStrings();
        }

        private void InitializeStrings()
        {
            _strings = new Dictionary<string, Dictionary<Language, string>>
            {
                // Header
                ["UninstallerTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "CENTRO DE REMOÇÃO TÁTICA",
                    [Language.Spanish] = "CENTRO DE DESINSTALACIÓN TÁCTICA",
                    [Language.English] = "TACTICAL REMOVAL CENTER"
                },

                                ["UninstallerCenterTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "CENTRO DE REMOÇÃO TÁTICA",
                    [Language.Spanish] = "CENTRO DE ELIMINACIÓN TÁCTICA",
                    [Language.English] = "TACTICAL REMOVAL CENTER"
                },

                // Intro Panel
                                ["InstallerSelectLanguageTooltip"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Selecionar Idioma (ALT + 2)",
                    [Language.Spanish] = "Seleccionar Idioma (ALT + 2)",
                    [Language.English] = "Select Language (ALT + 2)"
                },

                ["UninstallTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "DESINSTALAR VOLTRIS?",
                    [Language.Spanish] = "¿DESINSTALAR VOLTRIS?",
                    [Language.English] = "UNINSTALL VOLTRIS?"
                },
                ["UninstallWarning"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Ao prosseguir, todas as otimizações de Kernel e ajustes finos serão revertidos para o padrão do Windows, podendo resultar em perda de performance e aumento de latência.",
                    [Language.Spanish] = "Al continuar, todas las optimizaciones de Kernel y ajustes finos se revertirán a los valores predeterminados de Windows, lo que puede resultar en pérdida de rendimiento y aumento de latencia.",
                    [Language.English] = "By proceeding, all Kernel optimizations and fine-tuned adjustments will be reverted to Windows defaults, which may result in performance loss and increased latency."
                },
                                ["UninstallerUninstallQuestion"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "DESINSTALAR VOLTRIS?",
                    [Language.Spanish] = "¿DESINSTALAR VOLTRIS?",
                    [Language.English] = "UNINSTALL VOLTRIS?"
                },

                ["UninstallerUninstallWarning"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Ao prosseguir, todas as otimizações de Kernel e ajustes finos serão revertidos para o padrão do Windows, podendo resultar em perda de performance e aumento de latência.",
                    [Language.Spanish] = "Al continuar, todas las optimizaciones de Kernel y ajustes finos se revertirán a los valores predeterminados de Windows, lo que puede resultar en pérdida de rendimiento y aumento de latencia.",
                    [Language.English] = "By proceeding, all Kernel optimizations and fine-tuned adjustments will be reverted to Windows defaults, which may result in performance loss and increased latency."
                },

                ["KeepUserData"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Preservar metadados e logs de otimização (Recomendado)",
                    [Language.Spanish] = "Preservar metadatos y logs de optimización (Recomendado)",
                    [Language.English] = "Preserve optimization metadata and logs (Recommended)"
                },

                                ["UninstallerPreserveLogs"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Preservar metadados e logs de otimização",
                    [Language.Spanish] = "Preservar metadatos y registros de optimización",
                    [Language.English] = "Preserve optimization metadata and logs"
                },

                // Survey
                ["SurveyTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Por que você decidiu nos deixar?",
                    [Language.Spanish] = "¿Por qué decidiste dejarnos?",
                    [Language.English] = "Why did you decide to leave us?"
                },
                ["Reason1"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Não entendi como extrair performance do software",
                    [Language.Spanish] = "No entendí cómo extraer rendimiento del software",
                    [Language.English] = "I didn't understand how to extract performance from the software"
                },
                ["Reason2"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Encontrei instabilidade em alguns processos",
                    [Language.Spanish] = "Encontré inestabilidad en algunos procesos",
                    [Language.English] = "I found instability in some processes"
                },
                ["Reason3"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "O custo da licença não cabe no meu orçamento",
                    [Language.Spanish] = "El costo de la licencia no cabe en mi presupuesto",
                    [Language.English] = "The license cost doesn't fit my budget"
                },
                ["Reason4"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Vou realizar uma formatação limpa no Windows",
                    [Language.Spanish] = "Voy a realizar una formateo limpio en Windows",
                    [Language.English] = "I'm going to do a clean Windows format"
                },
                ["Reason5"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Outros motivos / Curiosidade técnica",
                    [Language.Spanish] = "Otros motivos / Curiosidad técnica",
                    [Language.English] = "Other reasons / Technical curiosity"
                },

                // Progress
                ["ExecutingUninstall"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "EXECUTANDO REMOÇÃO",
                    [Language.Spanish] = "EJECUTANDO DESINSTALACIÓN",
                    [Language.English] = "EXECUTING REMOVAL"
                },
                ["StatusOptimizing"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Otimizando saída...",
                    [Language.Spanish] = "Optimizando salida...",
                    [Language.English] = "Optimizing exit..."
                },

                // Complete
                ["UninstallComplete"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Desinstalação concluída com sucesso!",
                    [Language.Spanish] = "¡Desinstalación completada con éxito!",
                    [Language.English] = "Uninstallation completed successfully!"
                },

                // Footer Buttons
                ["Back"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Voltar",
                    [Language.Spanish] = "Atrás",
                    [Language.English] = "Back"
                },
                ["Next"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Continuar",
                    [Language.Spanish] = "Continuar",
                    [Language.English] = "Continue"
                },
                ["Cancel"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Cancelar",
                    [Language.Spanish] = "Cancelar",
                    [Language.English] = "Cancel"
                },
                ["Close"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Fechar",
                    [Language.Spanish] = "Cerrar",
                    [Language.English] = "Close"
                },
                ["Uninstall"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Desinstalar",
                    [Language.Spanish] = "Desinstalar",
                    [Language.English] = "Uninstall"
                },

                // Language Selection
                ["SelectLanguage"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Selecionar Idioma",
                    [Language.Spanish] = "Seleccionar Idioma",
                    [Language.English] = "Select Language"
                },
                ["Portuguese"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Português",
                    [Language.Spanish] = "Portugués",
                    [Language.English] = "Portuguese"
                },
                ["Spanish"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Espanhol",
                    [Language.Spanish] = "Español",
                    [Language.English] = "Spanish"
                },
                ["English"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Inglês",
                    [Language.Spanish] = "Inglés",
                    [Language.English] = "English"
                },

                // Additional
                ["Warning"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Aviso",
                    [Language.Spanish] = "Advertencia",
                    [Language.English] = "Warning"
                },
                ["UninstallCompleteMessage"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "O Voltris Optimizer foi removido com sucesso.",
                    [Language.Spanish] = "Voltris Optimizer ha sido eliminado exitosamente.",
                    [Language.English] = "Voltris Optimizer has been successfully removed."
                },
                // Custom Message Box
                ["CustomMessageBoxTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Voltris Optimizer",
                    [Language.Spanish] = "Voltris Optimizer",
                    [Language.English] = "Voltris Optimizer"
                },
                ["CustomMessageBoxHeader"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Informação",
                    [Language.Spanish] = "Información",
                    [Language.English] = "Information"
                },
                ["ProgressDetecting"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Detectando instalação...",
                    [Language.Spanish] = "Detectando instalación...",
                    [Language.English] = "Detecting installation..."
                },
                ["ProgressTerminatingProcesses"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Encerrando processos...",
                    [Language.Spanish] = "Finalizando procesos...",
                    [Language.English] = "Terminating processes..."
                },
                ["ProgressRemovingServices"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Removendo serviços...",
                    [Language.Spanish] = "Eliminando servicios...",
                    [Language.English] = "Removing services..."
                },
                ["ProgressRemovingTasks"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Removendo tarefas agendadas...",
                    [Language.Spanish] = "Eliminando tareas programadas...",
                    [Language.English] = "Removing scheduled tasks..."
                },
                ["ProgressRemovingShortcuts"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Removendo atalhos...",
                    [Language.Spanish] = "Eliminando accesos directos...",
                    [Language.English] = "Removing shortcuts..."
                },
                ["ProgressRevertingIcons"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Revertendo ícones do Windows 11...",
                    [Language.Spanish] = "Revirtiendo íconos de Windows 11...",
                    [Language.English] = "Reverting Windows 11 icons..."
                },
                ["ProgressRemovingVisualIntegration"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Removendo integração visual...",
                    [Language.Spanish] = "Eliminando integración visual...",
                    [Language.English] = "Removing visual integration..."
                },
                ["ProgressRemovingRegistry"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Removendo entradas de registro...",
                    [Language.Spanish] = "Eliminando entradas de registro...",
                    [Language.English] = "Removing registry entries..."
                },
                ["ProgressRemovingFiles"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Removendo arquivos e pastas de instalação...",
                    [Language.Spanish] = "Eliminando archivos y carpetas de instalación...",
                    [Language.English] = "Removing installation files and folders..."
                },
                ["ProgressRemovingSystemData"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Removendo dados de sistema...",
                    [Language.Spanish] = "Eliminando datos del sistema...",
                    [Language.English] = "Removing system data..."
                },
                ["ProgressRemovingUserData"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Removendo dados do usuário...",
                    [Language.Spanish] = "Eliminando datos del usuario...",
                    [Language.English] = "Removing user data..."
                },
                ["ProgressCreatingCleanupScript"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Criando script de limpeza final...",
                    [Language.Spanish] = "Creando script de limpieza final...",
                    [Language.English] = "Creating final cleanup script..."
                },
                ["ProgressFinalizing"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Finalizando...",
                    [Language.Spanish] = "Finalizando...",
                    [Language.English] = "Finalizing..."
                },
                ["ProgressDone"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Desinstalação concluída!",
                    [Language.Spanish] = "¡Desinstalación completa!",
                    [Language.English] = "Uninstallation complete!"
                },
                ["ProgressSearchingFolders"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Buscando pastas de instalação...",
                    [Language.Spanish] = "Buscando carpetas de instalación...",
                    [Language.English] = "Searching for installation folders..."
                },
                ["ProgressRemovingFolder"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Removendo pasta de instalação ({0}/{1})...",
                    [Language.Spanish] = "Eliminando carpeta de instalación ({0}/{1})...",
                    [Language.English] = "Removing installation folder ({0}/{1})..."
                },
                ["ProgressRemovingMainFolder"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Removendo pasta principal...",
                    [Language.Spanish] = "Eliminando carpeta principal...",
                    [Language.English] = "Removing main folder..."
                },
                ["CustomMessageBoxOk"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "OK",
                    [Language.Spanish] = "OK",
                    [Language.English] = "OK"
                },
                ["SelectReasonWarning"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Por favor, selecione um motivo para continuar.",
                    [Language.Spanish] = "Por favor, seleccione un motivo para continuar.",
                    [Language.English] = "Please select a reason to continue."
                }
            };
        }

        private void LoadStrings()
        {
        }

        public string this[string key] => GetString(key);
        public string GetString(string key)
        {
            if (_strings.TryGetValue(key, out var languageDict))
            {
                if (languageDict.TryGetValue(_currentLanguage, out var value))
                {
                    return value;
                }
            }
            return key;
        }

        public void SetLanguage(Language language)
        {
            CurrentLanguage = language;
        }

        public string GetLanguageName(Language language)
        {
            return language switch
            {
                Language.Portuguese => "Português",
                Language.Spanish => "Español",
                Language.English => "English",
                _ => language.ToString()
            };
        }
    }
}
