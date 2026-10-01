using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Classifica um dispositivo em <see cref="DriverCategory"/> usando APENAS dados reais do
    /// Windows (ClassGuid do SetupAPI, ClassName da classe de setup e Hardware IDs).
    /// Não existe fallback inventado: um dispositivo sem informação consistente cai em
    /// <see cref="DriverCategory.Unknown"/>, que por sua vez tem ícone dedicado.
    ///
    /// Substitui as duas classificações divergentes que existiam antes
    /// (SetupApiEnumerator.CATEGORY por ClassName e DriversView.PredictCategory por ClassGuid),
    /// o que fazia o mesmo dispositivo aparecer com categorias diferentes em pontos diferentes.
    /// </summary>
    public static class DriverCategoryClassifier
    {
        // Device setup classes do Windows (GUIDs oficiais, ver MSDN "System-Defined Device Setup
        // Classes Available to Vendors"). Fontes de verdade, não palpite.
        private const string ClassDisplay = "{4d36e968-e325-11ce-bfc1-08002be10318}";
        private const string ClassMedia = "{4d36e96c-e325-11ce-bfc1-08002be10318}";
        private const string ClassNet = "{4d36e972-e325-11ce-bfc1-08002be10318}";
        private const string ClassSystem = "{4d36e97d-e325-11ce-bfc1-08002be10318}";
        private const string ClassUsb = "{4d36e97e-e325-11ce-bfc1-08002be10318}";
        private const string ClassHidClass = "{745a17a0-74d3-11d0-b6fe-00a0c90f57da}";
        private const string ClassKeyboard = "{4d36e96b-e325-11ce-bfc1-08002be10318}";
        private const string ClassMouse = "{4d36e96f-e325-11ce-bfc1-08002be10318}";
        private const string ClassDiskDrive = "{4d36e967-e325-11ce-bfc1-08002be10318}";
        private const string ClassHdc = "{4d36e96a-e325-11ce-bfc1-08002be10318}";
        private const string ClassScsiAdapter = "{4d36e97b-e325-11ce-bfc1-08002be10318}";
        private const string ClassProcessor = "{4d36e978-e325-11ce-bfc1-08002be10318}";
        private const string ClassBluetooth = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
        private const string ClassMonitor = "{4d36e96e-e325-11ce-bfc1-08002be10318}";
        private const string ClassAudioEndpoint = "{c166523c-fe0c-4a94-a586-f1a80cfbbf3e}";
        private const string ClassPrinter = "{1ed2bbf9-11f0-4084-b21f-9a0d8183d0ca}";
        private const string ClassCamera = "{ca3e7ab9-b4c3-4ae6-8251-58ef7346bd4b}";
        private const string ClassBattery = "{72631e54-78a4-11d0-bcf7-0080c73c8881}";
        private const string ClassFirmware = "{f12a7078-d8b5-4d6b-9b40-480b6d8fa8e0}";
        private const string ClassImageScanner = "{6bdd1fc6-810f-11d0-bec7-08002be2032f}";
        private const string ClassWia = "{6bdd1fc8-810f-11d0-bec7-08002be2032f}";

        /// <summary>
        /// Classifica o dispositivo. <paramref name="classGuid"/> tem precedência porque é o
        /// identificador canônico; ClassName e HardwareIds refinam quando o GUID é genérico.
        /// </summary>
        public static DriverCategory Classify(string? classGuid, string? className, string? hardwareIds, string? deviceInstanceId)
        {
            var guid = Normalize(classGuid);

            switch (guid)
            {
                case ClassDisplay: return DriverCategory.Gpu;
                case ClassProcessor: return DriverCategory.Chipset;
                case ClassMedia: return DriverCategory.Audio;
                case ClassAudioEndpoint: return DriverCategory.Audio;
                case ClassNet: return ClassifyNetworkOrWifi(hardwareIds, deviceInstanceId);
                case ClassBluetooth: return DriverCategory.Bluetooth;
                case ClassDiskDrive:
                case ClassHdc:
                case ClassScsiAdapter: return DriverCategory.Storage;
                case ClassUsb: return DriverCategory.Usb;
                case ClassHidClass:
                case ClassKeyboard:
                case ClassMouse: return DriverCategory.Input;
                case ClassMonitor: return DriverCategory.Monitor;
                case ClassPrinter:
                case ClassImageScanner:
                case ClassWia: return DriverCategory.Printer;
                case ClassCamera: return DriverCategory.Camera;
                case ClassBattery: return DriverCategory.Battery;
                case ClassFirmware:
                case ClassSystem: return ClassifySystemOrFirmware(hardwareIds, deviceInstanceId);
            }

            // GUID ausente/desconhecido: refinar pelo ClassName real da classe de setup.
            var name = (className ?? string.Empty).Trim();
            if (name.Length > 0)
            {
                switch (name.ToLowerInvariant())
                {
                    case "display": return DriverCategory.Gpu;
                    case "processor": return DriverCategory.Chipset;
                    case "media":
                    case "audioendpoint": return DriverCategory.Audio;
                    case "net": return ClassifyNetworkOrWifi(hardwareIds, deviceInstanceId);
                    case "bluetooth": return DriverCategory.Bluetooth;
                    case "diskdrive":
                    case "hdc":
                    case "scsiadapter": return DriverCategory.Storage;
                    case "usb": return DriverCategory.Usb;
                    case "hidclass":
                    case "keyboard":
                    case "mouse": return DriverCategory.Input;
                    case "monitor": return DriverCategory.Monitor;
                    case "printer": return DriverCategory.Printer;
                    case "camera": return DriverCategory.Camera;
                    case "battery": return DriverCategory.Battery;
                    case "firmware":
                    case "system": return ClassifySystemOrFirmware(hardwareIds, deviceInstanceId);
                }
            }

            return DriverCategory.Unknown;
        }

        /// <summary>Wi-Fi e rede com fio compartilham a classe Net; o Hardware ID desambigua.</summary>
        private static DriverCategory ClassifyNetworkOrWifi(string? hardwareIds, string? deviceInstanceId)
        {
            string probe = $"{hardwareIds} {deviceInstanceId}";
            if (Contains(probe, "WLAN") || Contains(probe, "WI-FI") || Contains(probe, "WIFI") ||
                Contains(probe, "802_11") || Contains(probe, "SWD\\WIFI") || Contains(probe, "vwifimp"))
                return DriverCategory.Wifi;
            return DriverCategory.Network;
        }

        /// <summary>
        /// A classe System hospeda firmware, ACPI, sensores e bridges. Só é CHIPSET quando há
        /// indício real de CPU/bridge; caso contrário é Firmware, que é semanticamente correto.
        /// </summary>
        private static DriverCategory ClassifySystemOrFirmware(string? hardwareIds, string? deviceInstanceId)
        {
            string probe = $"{hardwareIds} {deviceInstanceId}";
            if (Contains(probe, "ACPI\\PNP0C0F") ||    // CPU power management
                Contains(probe, "ACPI\\VEN_PNP&DEV_C") ||// barramentos ACPI genéricos
                Contains(probe, "PCI\\VEN_") && Contains(probe, "class_bridge"))
                return DriverCategory.Chipset;
            return DriverCategory.Firmware;
        }

        private static bool Contains(string? value, string token) =>
            !string.IsNullOrEmpty(value) && value.Contains(token, StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string? classGuid) =>
            (classGuid ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>
        /// Rótulo de agrupamento exibido no cabeçalho de grupo. Mantido em PT-BR para preservar
        /// a identidade visual atual da página.
        /// </summary>
        public static string GetDisplayName(DriverCategory category) => category switch
        {
            DriverCategory.Gpu => "Vídeo",
            DriverCategory.Chipset => "Chipset",
            DriverCategory.Audio => "Áudio",
            DriverCategory.Network => "Rede",
            DriverCategory.Wifi => "Wi-Fi",
            DriverCategory.Bluetooth => "Bluetooth",
            DriverCategory.Storage => "Armazenamento",
            DriverCategory.Usb => "USB",
            DriverCategory.Input => "Periféricos",
            DriverCategory.Monitor => "Monitor",
            DriverCategory.Printer => "Impressora",
            DriverCategory.Camera => "Câmera",
            DriverCategory.Battery => "Bateria",
            DriverCategory.Firmware => "Firmware",
            _ => "Outros"
        };

        /// <summary>
        /// Categorias cujo driver pode ser atualizado com segurança por um gerenciador.
        /// Controladores de armazenamento, chipset e barramentos são EXCLUÍDAS de propósito:
        /// um erro ali causa tela azul e o próprio SafeDriverInstaller já bloqueia.
        /// </summary>
        public static bool IsAutoUpdateEligible(DriverCategory category) => category switch
        {
            DriverCategory.Gpu => true,
            DriverCategory.Audio => true,
            DriverCategory.Network => true,
            DriverCategory.Wifi => true,
            DriverCategory.Bluetooth => true,
            DriverCategory.Usb => true,
            DriverCategory.Input => true,
            DriverCategory.Monitor => true,
            DriverCategory.Printer => true,
            DriverCategory.Camera => true,
            DriverCategory.Battery => true,
            // Storage/Chipset/Firmware/Unknown: apenas avaliação einformação, instalação manual.
            _ => false
        };
    }
}
