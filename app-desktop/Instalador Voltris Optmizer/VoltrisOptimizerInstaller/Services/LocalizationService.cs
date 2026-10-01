using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace VoltrisOptimizerInstaller.Services
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
                ["InstallerTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "ASSISTENTE DE INSTALAÇÃO",
                    [Language.Spanish] = "ASISTENTE DE INSTALACIÓN",
                    [Language.English] = "INSTALLATION WIZARD"
                },
                ["UninstallerTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "CENTRO DE REMOÇÃO TÁTICA",
                    [Language.Spanish] = "CENTRO DE DESINSTALACIÓN TÁCTICA",
                    [Language.English] = "TACTICAL REMOVAL CENTER"
                },
                ["InstallerAppName"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Voltris Optimizer",
                    [Language.Spanish] = "Voltris Optimizer",
                    [Language.English] = "Voltris Optimizer"
                },
                ["InstallerAssistant"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Assistente de Instalação",
                    [Language.Spanish] = "Asistente de Instalación",
                    [Language.English] = "Installation Assistant"
                },

                // Welcome Step
                ["WelcomeTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Bem-vindo ao Instalador do Voltris Optimizer",
                    [Language.Spanish] = "Bienvenido al Instalador de Voltris Optimizer",
                    [Language.English] = "Welcome to Voltris Optimizer Installer"
                },
                ["WelcomeDescription"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Este assistente irá guiá-lo através da instalação do Voltris Optimizer no seu computador.\n\nO Voltris Optimizer é um sistema profissional de otimização para Windows 10/11 que ajuda a melhorar o desempenho do seu sistema.",
                    [Language.Spanish] = "Este asistente le guiará a través de la instalación de Voltris Optimizer en su computadora.\n\nVoltris Optimizer es un sistema profesional de optimización para Windows 10/11 que ayuda a mejorar el rendimiento de su sistema.",
                    [Language.English] = "This wizard will guide you through the installation of Voltris Optimizer on your computer.\n\nVoltris Optimizer is a professional optimization system for Windows 10/11 that helps improve your system performance."
                },

                // License Step
                ["LicenseTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Termos de Licença",
                    [Language.Spanish] = "Términos de Licencia",
                    [Language.English] = "License Terms"
                },
                ["LicenseText"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "TERMOS DE LICENÇA DO VOLTRIS OPTIMIZER\n\nCopyright © 2025 VOLTRIS. Todos os direitos reservados.\n\nAo instalar e usar este software, você concorda com os seguintes termos:\n\n1. Este software é fornecido 'como está', sem garantias de qualquer tipo.\n2. Você pode usar este software para fins pessoais e comerciais.\n3. Não é permitido modificar, distribuir ou revender este software sem autorização.\n4. O VOLTRIS não se responsabiliza por danos causados pelo uso deste software.\n\nPara mais informações, visite: https://voltris.com.br",
                    [Language.Spanish] = "TÉRMINOS DE LICENCIA DE VOLTRIS OPTIMIZER\n\nCopyright © 2025 VOLTRIS. Todos los derechos reservados.\n\nAl instalar y usar este software, usted acepta los siguientes términos:\n\n1. Este software se proporciona 'tal cual', sin garantías de ningún tipo.\n2. Puede usar este software para fines personales y comerciales.\n3. No está permitido modificar, distribuir o revender este software sin autorización.\n4. VOLTRIS no se responsabiliza por daños causados por el uso de este software.\n\nPara más información, visite: https://voltris.com.br",
                    [Language.English] = "VOLTRIS OPTIMIZER LICENSE TERMS\n\nCopyright © 2025 VOLTRIS. All rights reserved.\n\nBy installing and using this software, you agree to the following terms:\n\n1. This software is provided 'as is', without warranties of any kind.\n2. You may use this software for personal and commercial purposes.\n3. You may not modify, distribute, or resell this software without authorization.\n4. VOLTRIS is not responsible for damages caused by the use of this software.\n\nFor more information, visit: https://voltris.com.br"
                },
                ["IAcceptLicense"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Eu aceito os termos do acordo de licença",
                    [Language.Spanish] = "Acepto los términos del acuerdo de licencia",
                    [Language.English] = "I accept the terms of the license agreement"
                },

                // Destination Step
                ["DestinationTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Escolha o Local de Instalação",
                    [Language.Spanish] = "Elija la Ubicación de Instalación",
                    [Language.English] = "Choose Installation Location"
                },
                ["DestinationDescription"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Selecione a pasta onde o Voltris Optimizer será instalado.",
                    [Language.Spanish] = "Seleccione la carpeta donde se instalará Voltris Optimizer.",
                    [Language.English] = "Select the folder where Voltris Optimizer will be installed."
                },
                ["Browse"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Procurar...",
                    [Language.Spanish] = "Examinar...",
                    [Language.English] = "Browse..."
                },
                ["SpaceRequired"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Espaço necessário: ~160 MB",
                    [Language.Spanish] = "Espacio necesario: ~160 MB",
                    [Language.English] = "Space required: ~160 MB"
                },
                ["SpaceAvailable"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Espaço disponível: {0:N0} MB",
                    [Language.Spanish] = "Espacio disponible: {0:N0} MB",
                    [Language.English] = "Available space: {0:N0} MB"
                },

                // Options Step
                ["OptionsTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Opções de Instalação",
                    [Language.Spanish] = "Opciones de Instalación",
                    [Language.English] = "Installation Options"
                },
                ["OptionsDescription"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Selecione as opções adicionais de instalação.",
                    [Language.Spanish] = "Seleccione las opciones adicionales de instalación.",
                    [Language.English] = "Select additional installation options."
                },
                ["CreateDesktopShortcut"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Criar atalho na área de trabalho",
                    [Language.Spanish] = "Crear acceso directo en el escritorio",
                    [Language.English] = "Create desktop shortcut"
                },
                ["CreateStartMenuShortcut"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Criar atalho no Menu Iniciar",
                    [Language.Spanish] = "Crear acceso directo en el Menú Inicio",
                    [Language.English] = "Create Start Menu shortcut"
                },
                ["StartWithWindows"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Iniciar com o Windows",
                    [Language.Spanish] = "Iniciar con Windows",
                    [Language.English] = "Start with Windows"
                },
                ["StartMinimized"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Iniciar minimizado",
                    [Language.Spanish] = "Iniciar minimizado",
                    [Language.English] = "Start minimized"
                },
                ["LaunchAfterInstall"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Executar Voltris Optimizer agora",
                    [Language.Spanish] = "Ejecutar Voltris Optimizer ahora",
                    [Language.English] = "Launch Voltris Optimizer now"
                },

                // Installing Step
                ["InstallingTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Instalando...",
                    [Language.Spanish] = "Instalando...",
                    [Language.English] = "Installing..."
                },
                ["InstallingDescription"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Por favor, aguarde enquanto o Voltris Optimizer é instalado no seu computador.",
                    [Language.Spanish] = "Por favor espere mientras Voltris Optimizer se instala en su computadora.",
                    [Language.English] = "Please wait while Voltris Optimizer is installed on your computer."
                },

                // Complete Step
                ["CompleteTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Instalação Concluída!",
                    [Language.Spanish] = "¡Instalación Completada!",
                    [Language.English] = "Installation Complete!"
                },
                ["CompleteDescription"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "O Voltris Optimizer foi instalado com sucesso no seu computador.\n\nObrigado por escolher o Voltris Optimizer!",
                    [Language.Spanish] = "Voltris Optimizer se ha instalado correctamente en su computadora.\n\n¡Gracias por elegir Voltris Optimizer!",
                    [Language.English] = "Voltris Optimizer has been successfully installed on your computer.\n\nThank you for choosing Voltris Optimizer!"
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
                ["Install"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Instalar",
                    [Language.Spanish] = "Instalar",
                    [Language.English] = "Install"
                },

                // Language Selection
                ["InstallerSelectLanguageTooltip"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Selecionar idioma do instalador",
                    [Language.Spanish] = "Seleccionar idioma del instalador",
                    [Language.English] = "Select installer language"
                },
                ["SelectLanguage"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Selecionar Idioma",
                    [Language.Spanish] = "Seleccionar Idioma",
                    [Language.English] = "Select Language"
                },
                ["Language"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Idioma",
                    [Language.Spanish] = "Idioma",
                    [Language.English] = "Language"
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

                // Uninstaller
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
                ["KeepUserData"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Preservar metadados e logs de otimização (Recomendado)",
                    [Language.Spanish] = "Preservar metadatos y logs de optimización (Recomendado)",
                    [Language.English] = "Preserve optimization metadata and logs (Recommended)"
                },
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
                ["UninstallComplete"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Desinstalação concluída com sucesso!",
                    [Language.Spanish] = "¡Desinstalación completada con éxito!",
                    [Language.English] = "Uninstallation completed successfully!"
                },

                // Cancel Installation
                ["CancelInstallTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Cancelar Instalação",
                    [Language.Spanish] = "Cancelar Instalación",
                    [Language.English] = "Cancel Installation"
                },
                ["CancelInstallMessage"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Tem certeza que deseja cancelar a instalação? O processo será interrompido.",
                    [Language.Spanish] = "¿Está seguro de que desea cancelar la instalación? El proceso se interrumpirá.",
                    [Language.English] = "Are you sure you want to cancel the installation? The process will be interrupted."
                },

                // Error Messages
                ["ErrorTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Erro",
                    [Language.Spanish] = "Error",
                    [Language.English] = "Error"
                },
                ["FilesNotFound"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Não foi possível encontrar os arquivos do Voltris Optimizer.\n\nCertifique-se de que a pasta 'publish' está na mesma pasta do instalador ou que os pacotes estejam embutidos.",
                    [Language.Spanish] = "No se pudieron encontrar los archivos de Voltris Optimizer.\n\nAsegúrese de que la carpeta 'publish' está en la misma carpeta del instalador o que los paquetes estén integrados.",
                    [Language.English] = "Could not find Voltris Optimizer files.\n\nMake sure the 'publish' folder is in the same folder as the installer or that the packages are embedded."
                },
                ["AcceptLicenseFirst"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Por favor, role o texto da licença até o fim e marque a opção 'Eu aceito os termos do acordo de licença' para continuar.",
                    [Language.Spanish] = "Por favor, desplácese hasta el final del texto de licencia y marque la opción 'Acepto los términos del acuerdo de licencia' para continuar.",
                    [Language.English] = "Please scroll to the end of the license text and check 'I accept the terms of the license agreement' to continue."
                },
                ["InstallerInitError"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Erro ao inicializar o instalador:",
                    [Language.Spanish] = "Error al inicializar el instalador:",
                    [Language.English] = "Error initializing installer:"
                },

                // Folder Browser
                ["SelectFolder"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Selecione a pasta de instalação",
                    [Language.Spanish] = "Seleccione la carpeta de instalación",
                    [Language.English] = "Select installation folder"
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
                ["CustomMessageBoxOk"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "OK",
                    [Language.Spanish] = "OK",
                    [Language.English] = "OK"
                },
                ["AdminRequired"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Este instalador requer privilégios de administrador.",
                    [Language.Spanish] = "Este instalador requiere privilegios de administrador.",
                    [Language.English] = "This installer requires administrator privileges."
                },
                ["FatalError"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Erro fatal",
                    [Language.Spanish] = "Error fatal",
                    [Language.English] = "Fatal error"
                },
                ["FilesNotFoundMessage"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Não foi possível encontrar os arquivos do Voltris Optimizer.\n\nCertifique-se de que a pasta 'publish' está na mesma pasta do instalador ou que os pacotes estejam embutidos.",
                    [Language.Spanish] = "No se pudieron encontrar los archivos de Voltris Optimizer.\n\nAsegúrese de que la carpeta 'publish' esté en la misma carpeta que el instalador o que los paquetes estén integrados.",
                    [Language.English] = "Could not find Voltris Optimizer files.\n\nMake sure the 'publish' folder is in the same folder as the installer or that the packages are embedded."
                },
                ["FilesNotFoundTitle"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Arquivos não encontrados",
                    [Language.Spanish] = "Archivos no encontrados",
                    [Language.English] = "Files not found"
                },
                ["InitError"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Erro ao inicializar o instalador:",
                    [Language.Spanish] = "Error al inicializar el instalador:",
                    [Language.English] = "Error initializing the installer:"
                },
                ["AdminDirectoryWarning"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Este diretório exigirá permissões de administrador. Deseja continuar?",
                    [Language.Spanish] = "Este directorio requerirá permisos de administrador. ¿Desea continuar?",
                    [Language.English] = "This directory will require administrator permissions. Do you want to continue?"
                },
                ["InvalidDirectory"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Selecione ou digite um diretório válido para instalação.",
                    [Language.Spanish] = "Seleccione o ingrese un directorio válido para la instalación.",
                    [Language.English] = "Please select or enter a valid directory for installation."
                },
                ["CancelInstallQuestion"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Tem certeza que deseja cancelar a instalação?",
                    [Language.Spanish] = "¿Está seguro de que desea cancelar la instalación?",
                    [Language.English] = "Are you sure you want to cancel the installation?"
                },
                ["Warning"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Aviso",
                    [Language.Spanish] = "Advertencia",
                    [Language.English] = "Warning"
                },
                ["InstallError"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Erro durante a instalação:",
                    [Language.Spanish] = "Error durante la instalación:",
                    [Language.English] = "Error during installation:"
                },
                ["ProgressCreatingDirectories"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Criando diretórios...",
                    [Language.Spanish] = "Creando directorios...",
                    [Language.English] = "Creating directories..."
                },
                ["ProgressCopyingFiles"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Copiando arquivos...",
                    [Language.Spanish] = "Copiando archivos...",
                    [Language.English] = "Copying files..."
                },
                ["ProgressInstallingUninstaller"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Instalando desinstalador...",
                    [Language.Spanish] = "Instalando desinstalador...",
                    [Language.English] = "Installing uninstaller..."
                },
                ["ProgressRegisteringControlPanel"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Registrando no Painel de Controle...",
                    [Language.Spanish] = "Registrando en el Panel de Control...",
                    [Language.English] = "Registering in Control Panel..."
                },
                ["ProgressCreatingShortcuts"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Criando atalhos...",
                    [Language.Spanish] = "Creando accesos directos...",
                    [Language.English] = "Creating shortcuts..."
                },
                ["ProgressConfiguringRegistry"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Configurando registro...",
                    [Language.Spanish] = "Configurando registro...",
                    [Language.English] = "Configuring registry..."
                },
                ["ProgressInstallationComplete"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Instalação concluída!",
                    [Language.Spanish] = "¡Instalación completa!",
                    [Language.English] = "Installation complete!"
                },
                ["PinToQuickAccess"] = new Dictionary<Language, string>
                {
                    [Language.Portuguese] = "Fixar no Acesso Rápido (Home)",
                    [Language.Spanish] = "Anclar al Acceso rápido (Inicio)",
                    [Language.English] = "Pin to Quick Access (Home)"
                }
            };
        }

        private void LoadStrings()
        {
        }

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

        public string this[string key] => GetString(key);

        public string GetString(string key, params object[] args)
        {
            var format = GetString(key);
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
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
