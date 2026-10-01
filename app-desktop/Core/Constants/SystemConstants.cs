using System;

namespace VoltrisOptimizer.Core.Constants
{
    /// <summary>
    /// Constantes de sistema para evitar "magic numbers" espalhados pelo código
    /// </summary>
    public static class SystemConstants
    {
        /// <summary>
        /// Constantes de planos de energia do Windows
        /// </summary>
        public static class PowerPlans
        {
            /// <summary>
            /// GUID do plano de Alto Desempenho
            /// </summary>
            public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

            /// <summary>
            /// GUID do plano Equilibrado
            /// </summary>
            public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";

            /// <summary>
            /// GUID do plano Equilibrado como Guid para API nativa
            /// </summary>
            public static readonly Guid BalancedGuid = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");

            /// <summary>
            /// GUID do plano Economia de Energia
            /// </summary>
            public const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";

            /// <summary>
            /// GUID do plano Ultimate Performance
            /// </summary>
            public const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        }

        /// <summary>
        /// Constantes de prioridade de processos
        /// </summary>
        public static class ProcessPriority
        {
            /// <summary>
            /// Win32PrioritySeparation para foreground boost máximo
            /// Valor 38 = 0x26 = Programas em primeiro plano priorizados
            /// </summary>
            public const int ForegroundBoostMax = 38;

            /// <summary>
            /// Win32PrioritySeparation para sistemas com hardware limitado (Segurança)
            /// Valor 22 = 0x16 
            /// </summary>
            public const int SafePerformance = 22;

            /// <summary>
            /// Win32PrioritySeparation equilibrado (Notebooks ou CPUs intermediários)
            /// Valor 36 = 0x24
            /// </summary>
            public const int BalancedPerformance = 36;

            /// <summary>
            /// Win32PrioritySeparation padrão do Windows
            /// </summary>
            public const int Default = 2;

            /// <summary>
            /// Win32PrioritySeparation para jogos em sistemas com RAM limitada
            /// </summary>
            public const int LowRamGaming = 0x20;

            /// <summary>
            /// Win32PrioritySeparation para jogos em sistemas com RAM adequada
            /// </summary>
            public const int HighRamGaming = 0x26;
        }

        /// <summary>
        /// Constantes de TDR (Timeout Detection and Recovery) da GPU
        /// </summary>
        public static class TdrSettings
        {
            /// <summary>
            /// Delay em segundos antes de considerar a GPU travada (padrão: 2)
            /// </summary>
            public const int DefaultDelay = 2;

            /// <summary>
            /// Delay em segundos para jogos pesados (aumentado para evitar falsos positivos)
            /// </summary>
            public const int GamingDelay = 10;

            /// <summary>
            /// Nível TDR: 0 = desabilitado, 1 = log only, 2 = recover, 3 = recover and restart
            /// </summary>
            public const int LevelDisabled = 0;
            public const int LevelLogOnly = 1;
            public const int LevelRecover = 2;
            public const int LevelRecoverAndRestart = 3;

            /// <summary>
            /// DDI Delay padrão (20 segundos)
            /// </summary>
            public const int DdiDelayDefault = 20;
        }

        /// <summary>
        /// Constantes de HAGS (Hardware Accelerated GPU Scheduling)
        /// </summary>
        public static class HagsSettings
        {
            /// <summary>
            /// HwSchMode: 1 = Desabilitado, 2 = Habilitado
            /// </summary>
            public const int Disabled = 1;
            public const int Enabled = 2;
        }

        /// <summary>
        /// Constantes de QoS (Quality of Service)
        /// </summary>
        public static class QosSettings
        {
            /// <summary>
            /// DSCP para tráfego de jogos (Expedited Forwarding)
            /// </summary>
            public const int GamingDscp = 46;

            /// <summary>
            /// DSCP para tráfego prioritário (Assured Forwarding)
            /// </summary>
            public const int PriorityDscp = 34;

            /// <summary>
            /// DSCP padrão (Best Effort)
            /// </summary>
            public const int DefaultDscp = 0;
        }

        /// <summary>
        /// Constantes de rede
        /// </summary>
        public static class NetworkSettings
        {
            /// <summary>
            /// Frequência de ACKs TCP para baixa latência
            /// </summary>
            public const int TcpAckFrequencyLowLatency = 1;

            /// <summary>
            /// Frequência de ACKs TCP padrão
            /// </summary>
            public const int TcpAckFrequencyDefault = 2;

            /// <summary>
            /// TCPNoDelay: 0 = Nagle habilitado, 1 = Nagle desabilitado
            /// </summary>
            public const int TcpNoDelayEnabled = 1;
            public const int TcpNoDelayDisabled = 0;
        }

        /// <summary>
        /// Constantes de perfil multimídia
        /// </summary>
        public static class MultimediaProfile
        {
            /// <summary>
            /// SystemResponsiveness: 0 = máxima prioridade para jogos
            /// </summary>
            public const int MaxGamingPriority = 0;

            /// <summary>
            /// SystemResponsiveness padrão
            /// </summary>
            public const int Default = 20;

            /// <summary>
            /// NetworkThrottlingIndex: 10 = desabilitado
            /// </summary>
            public const int ThrottlingDisabled = 10;

            /// <summary>
            /// GPU Priority para jogos
            /// </summary>
            public const int GpuPriorityHigh = 8;

            /// <summary>
            /// Process Priority para jogos (1-8)
            /// </summary>
            public const int ProcessPriorityHigh = 6;
        }

        /// <summary>
        /// Constantes de Input (teclado/mouse)
        /// </summary>
        public static class InputSettings
        {
            /// <summary>
            /// Tamanho da fila de dados do teclado (padrão: 100)
            /// </summary>
            public const int KeyboardDataQueueSize = 100;

            /// <summary>
            /// Tamanho da fila de dados do mouse (padrão: 100)
            /// </summary>
            public const int MouseDataQueueSize = 100;
        }

        /// <summary>
        /// Constantes de timer
        /// </summary>
        public static class TimerSettings
        {
            /// <summary>
            /// Resolução máxima do timer em 100ns units (0.5ms)
            /// </summary>
            public const int MaxResolution100Ns = 5000;

            /// <summary>
            /// Resolução padrão do timer em 100ns units (15.6ms)
            /// </summary>
            public const int DefaultResolution100Ns = 156250;
        }

        /// <summary>
        /// Caminhos de registro comuns
        /// </summary>
        public static class RegistryPaths
        {
            // HKLM paths
            public const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
            public const string GraphicsDriversDci = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\DCI";
            public const string PriorityControl = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
            public const string SessionManagerKernel = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
            public const string MultimediaSystemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
            public const string MultimediaGames = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
            public const string TcpipParameters = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
            public const string KeyboardParameters = @"SYSTEM\CurrentControlSet\Services\Kbdclass\Parameters";
            public const string MouseParameters = @"SYSTEM\CurrentControlSet\Services\Mouclass\Parameters";
            public const string SessionManagerMemory = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
            public const string MemoryPrefetch = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters";
            public const string FileSystem = @"SYSTEM\CurrentControlSet\Control\FileSystem";
            public const string DfrgBootOptimize = @"SOFTWARE\Microsoft\Dfrg\BootOptimizeFunction";
            public const string PowerControl = @"SYSTEM\CurrentControlSet\Control\Power";
            public const string NvidiaGlobal = @"SOFTWARE\NVIDIA Corporation\Global\Stereo3D";
            public const string BitLocker = @"SYSTEM\CurrentControlSet\Control\BitLocker";
            public const string WindowsCopilot = @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot";
            public const string DeviceMetadata = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata";
            public const string RemoteAssistance = @"SYSTEM\CurrentControlSet\Control\Remote Assistance";
            public const string CloudContent = @"Software\Policies\Microsoft\Windows\CloudContent";
            public const string WorkplaceJoin = @"SOFTWARE\Policies\Microsoft\Windows\WorkplaceJoin";
            public const string WiFiSenseHotspot = @"Software\Microsoft\PolicyManager\default\WiFi\AllowWiFiHotSpotReporting";
            public const string WiFiSenseAutoConnect = @"Software\Microsoft\PolicyManager\default\WiFi\AllowAutoConnectToWiFiSenseHotspots";
            public const string DirectXUserGpu = @"Software\Microsoft\DirectX\UserGpuPreferences";
            public const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
            public const string InputSettings = @"Software\Microsoft\input";
            public const string InputAppPreload = @"Software\Microsoft\input\Settings";
            public const string Dsh = @"Software\Microsoft\Windows\CurrentVersion\Dsh";
            public const string CabinetState = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState";
            public const string PowerThrottling = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
            public const string CoreParking = @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583";
            public const string PrinterLegacy = @"Software\Microsoft\Windows NT\CurrentVersion\Windows";

            // HKCU paths
            public const string GameBar = @"Software\Microsoft\GameBar";
            public const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
            public const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
            public const string VisualEffects = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
            public const string BackgroundApps = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
            public const string DesktopSettings = @"Control Panel\Desktop";
            public const string DesktopColors = @"Control Panel\Colors";
            public const string Taskband = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";
        }

        /// <summary>
        /// Serviços do Windows comumente otimizados
        /// </summary>
        public static class WindowsServices
        {
            /// <summary>
            /// Serviços que podem ser pausados durante jogos
            /// </summary>
            public static readonly string[] GamingPausable = 
            {
                "SysMain",      // Superfetch
                "DiagTrack",    // Connected User Experiences and Telemetry
                "WSearch",      // Windows Search
                "Spooler"       // Print Spooler
            };

            /// <summary>
            /// Serviços secundários que podem ser pausados
            /// </summary>
            public static readonly string[] SecondaryPausable =
            {
                "wuauserv",         // Windows Update
                "w32time",          // Windows Time
                "Dnscache",         // DNS Client (cache pode ser mantido)
                "BITS",             // Background Intelligent Transfer
                "XblAuthManager",   // Xbox Live Auth
                "XblGameSave",      // Xbox Live Game Save
                "XboxNetApiSvc"     // Xbox Live Networking
            };

            /// <summary>
            /// Serviços que NUNCA devem ser desabilitados
            /// </summary>
            public static readonly string[] Critical =
            {
                "RpcSs",            // Remote Procedure Call
                "DcomLaunch",       // DCOM Server Process Launcher
                "EventLog",         // Windows Event Log
                "PlugPlay",         // Plug and Play
                "Power",            // Power
                "Winmgmt",          // Windows Management Instrumentation
                "CryptSvc",         // Cryptographic Services
                "LanmanWorkstation" // Workstation
            };
        }

        /// <summary>
        /// Processos que não devem ser fechados durante otimização
        /// </summary>
        public static class ProtectedProcesses
        {
            public static readonly string[] System =
            {
                "System", "Idle", "csrss", "wininit", "services", "lsass",
                "svchost", "dwm", "explorer", "sihost", "fontdrvhost",
                "WmiPrvSE", "RuntimeBroker", "ApplicationFrameHost"
            };

            public static readonly string[] Voltris =
            {
                "VoltrisOptimizer"
            };

            public static readonly string[] Launchers =
            {
                "Steam", "EpicGamesLauncher", "GalaxyClient",
                "Origin", "EADesktop", "UbisoftGameLauncher",
                "Battle.net", "Uplay"
            };

            public static readonly string[] Development =
            {
                "antigravity", "git", "devhost", "csc", "dotnet", "node", "npm", 
                "code", "devenv", "msbuild", "vbc", "fsc", "java", "python",
                "language_server_windows_x64", "powershell", "cmd", "conhost"
            };

            public static readonly string[] Multimedia =
            {
                "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
                "spotify", "vlc", "mpc-hc", "itunes", "discord", "zoom", "teams", "webex",
                "obs64", "streamlabs desktop", "audiodg", "steamwebhelper"
            };
        }

        /// <summary>
        /// Limites de RAM para decisões de otimização
        /// </summary>
        public static class RamThresholds
        {
            public const int LowEndGB = 8;
            public const int MidEndGB = 16;
            public const int HighEndGB = 32;
        }

        /// <summary>
        /// Limites de CPU para decisões de otimização
        /// </summary>
        public static class CpuThresholds
        {
            public const int LowEndCores = 4;
            public const int MidEndCores = 8;
            public const int HighEndCores = 16;
        }
    }
}

