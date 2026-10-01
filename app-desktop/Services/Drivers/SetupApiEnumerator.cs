using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Drivers
{
    public class SetupApiEnumerator : IDisposable
    {
        private IntPtr _deviceInfoSet;
        private static int _propertyReadFailures;

        // PnP IDs e Constantes
        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_ALLCLASSES = 0x00000004;
        private const uint SPDRP_DEVICEDESC = 0x00000000;
        private const uint SPDRP_HARDWAREID = 0x00000001;
        private const uint SPDRP_COMPATIBLEIDS = 0x00000002;
        private const uint SPDRP_SERVICE = 0x00000004;
        private const uint SPDRP_CLASS = 0x00000007;
        private const uint SPDRP_CLASSGUID = 0x00000008;
        private const uint SPDRP_DRIVER = 0x00000009;
        private const uint SPDRP_CONFIGFLAGS = 0x00000023;
        private const uint SPDRP_INSTALL_STATE = 0x00000022;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(IntPtr ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, uint Property, out uint PropertyRegDataType, byte[] PropertyBuffer, uint PropertyBufferSize, out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, StringBuilder DeviceInstanceId, uint DeviceInstanceIdSize, out uint RequiredSize);

        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);

        public SetupApiEnumerator()
        {
            _deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
            if (_deviceInfoSet == new IntPtr(-1))
            {
                throw new Exception("Falha ao inicializar o handle SetupAPI do Windows.");
            }
        }

        // DN_STARTED = 0x00000008 — device node is started (driver loaded and running)
        // DN_HAS_PROBLEM = 0x00000400 — device has a problem
        // DN_DISABLEABLE = 0x00002000 — device can be disabled
        private const uint DN_STARTED = 0x00000008;
        private const uint DN_HAS_PROBLEM = 0x00000400;

        public List<DeviceInfo> EnumerateDevices()
        {
            var devices = new List<DeviceInfo>();
            uint index = 0;
            var devInfo = new SP_DEVINFO_DATA();
            devInfo.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
            
            var sw = System.Diagnostics.Stopwatch.StartNew();

            App.LoggingService?.LogInfo("[SetupApiEnum] ⏱ Iniciando enumeração de dispositivos PnP...");

            try
            {
                while (SetupDiEnumDeviceInfo(_deviceInfoSet, index, ref devInfo))
                {
                    uint devStatus = 0, problem = 0;
                    int cmResult = CM_Get_DevNode_Status(out devStatus, out problem, devInfo.DevInst, 0);

                    string? hwIds = GetDevicePropertyMultiSz(devInfo, SPDRP_HARDWAREID);
                    string? firstHwId = hwIds?.Split(';').FirstOrDefault();

                    bool isStarted = cmResult == 0 && (devStatus & DN_STARTED) != 0;
                    bool hasProblem = cmResult == 0 && (problem != 0 || (devStatus & DN_HAS_PROBLEM) != 0);

                    // SP_INSTALL_STATE é um REG_DWORD: ler como texto Unicode produz lixo e o
                    // int.TryParse sempre falhava, tornando a checagem inútil. Ler como DWORD.
                    int installState = GetInstallState(devInfo);

                    // Uma única leitura de registro por dispositivo (antes eram quatro).
                    var driverInfo = ReadInstalledDriverInfo(devInfo);

                    // 🔍 MÉTODO PROFISSIONAL: Obter DeviceInstanceId real via SetupDiGetDeviceInstanceId
                    string? deviceInstanceId = null;
                    StringBuilder sbId = new StringBuilder(2048);
                    if (SetupDiGetDeviceInstanceId(_deviceInfoSet, ref devInfo, sbId, (uint)sbId.Capacity, out _))
                    {
                        deviceInstanceId = sbId.ToString();
                    }

                    if (string.IsNullOrEmpty(deviceInstanceId) && index < 10) { // Log apenas primeiros 10 para não flood
                        App.LoggingService?.LogWarning($"[SetupApiEnum] ⚠️ SetupDiGetDeviceInstanceId falhou para índice {index}");
                        App.LoggingService?.LogInfo($"[SetupApiEnum] 📋 FirstHwId: '{firstHwId}'");
                        App.LoggingService?.LogInfo($"[SetupApiEnum] 📋 Usando fallback: '{firstHwId ?? $"DEV_{index}"}'");
                    }

                    var device = new DeviceInfo
                    {
                        DeviceInstanceId = deviceInstanceId ?? firstHwId ?? $"DEV_{index}", 
                        FriendlyName = GetDevicePropertyString(devInfo, 0x0000000C),
                        Description = GetDevicePropertyString(devInfo, SPDRP_DEVICEDESC),
                        Vendor = VendorMapper.GetVendor(firstHwId),
                        HardwareIds = hwIds,
                        CompatibleIds = GetDevicePropertyMultiSz(devInfo, SPDRP_COMPATIBLEIDS),
                        Service = GetDevicePropertyString(devInfo, SPDRP_SERVICE),
                        DriverInfPath = driverInfo.InfPath,
                        DriverVersion = driverInfo.Version,
                        DriverDate = driverInfo.Date,
                        DriverProvider = driverInfo.Provider,
                        ClassGuid = devInfo.ClassGuid.ToString("B"),
                        ClassName = GetDevicePropertyString(devInfo, SPDRP_CLASS),
                        IsRunning = isStarted,
                        IsProblem = hasProblem || installState != 0,
                        // Código real do problema (CM_PROB_*) — permite exibir a CAUSA na UI
                        // em vez de apenas um "erro detectado" genérico.
                        ProblemCode = hasProblem ? problem : 0u
                    };

                    // Classificação única e derivada de dados reais (ClassGuid/ClassName/HWIDs).
                    // Substitui as duas classificações divergentes que existiam antes.
                    device.CategoryKind = DriverCategoryClassifier.Classify(
                        device.ClassGuid, device.ClassName, device.HardwareIds, device.DeviceInstanceId);
                    device.Category = DriverCategoryClassifier.GetDisplayName(device.CategoryKind);
                           // O VendorMapper JÁ resolve VEN_8086→Intel, VEN_10DE→NVIDIA, VEN_10EC→Realtek etc.
                    // Este bloco duplicava essa lógica e ainda sobrescrevia o resultado com o
                    // ProviderName do driver. Mantemos apenas o enrichments que o mapper não faz.
                    if (device.Vendor == VendorMapper.UnknownVendor)
                    {
                        device.Vendor = VendorMapper.ResolveFromHardwareIds(device.HardwareIds);
                    }

                    if (device.Vendor == VendorMapper.UnknownVendor &&
                        !string.IsNullOrEmpty(device.DriverProvider) &&
                        !string.Equals(device.DriverProvider, VendorMapper.UnknownVendor, StringComparison.OrdinalIgnoreCase))
                    {
                        device.Vendor = device.DriverProvider;
                    }

                    // CPU e disco nunca reportam DN_STARTED de forma útil, mas estão sempre presentes.
                    if (device.CategoryKind is DriverCategory.Chipset or DriverCategory.Storage) device.IsRunning = true;

                    // LOG POR DEVICE REMOVIDO — causava 130+ escritas sincronizadas no log, degradando a UI
                    devices.Add(device);
                    index++;
                }
            }
            catch (Exception ex)
            {
                // Não esconder: a lista parcial é útil, mas a falha precisa ser diagnosticável
                // com a cadeia completa de exceções (o log anterior registrava só ex.Message).
                App.LoggingService?.LogError(
                    $"[SetupApiEnum] Enumeracao interrompida no indice {index} apos {devices.Count} dispositivos. " +
                    $"Cadeia de excecoes: {ex.Describe()}");
            }

            sw.Stop();

            // Propriedades ausentes são normais em PnP, mas antes falhavam em silêncio. O contador
            // torna a operação observável sem gerar uma linha de log por dispositivo.
            int propertyReadFailures = Interlocked.Exchange(ref _propertyReadFailures, 0);
            var problemGroups = devices
                .Where(d => d.IsProblem)
                .GroupBy(d => d.ProblemCode)
                .Select(g => $"CM_PROB_{g.Key}({g.Count()})");

            App.LoggingService?.LogInfo(
                $"[SetupApiEnum] Enumeracao concluida em {sw.ElapsedMilliseconds}ms: {devices.Count} dispositivos | " +
                $"Ativos: {devices.Count(d => d.IsRunning)} | Com problema: {devices.Count(d => d.IsProblem)}" +
                (problemGroups.Any() ? $" [{string.Join(", ", problemGroups)}]" : "") +
                $" | Leituras de propriedade falhas: {propertyReadFailures}");

            App.LoggingService?.LogInfo(
                $"[SetupApiEnum] Distribuicao por categoria: {string.Join(", ", devices.GroupBy(d => d.Category).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"))}");

            return devices;
        }

        /// <summary>
        /// Lê SP_INSTALL_STATE (REG_DWORD). A versão anterior decodificava os 4 bytes como
        /// texto UTF-16, o que produzia caracteres ilegíveis e fazia o parse falhar sempre —
        /// a checagem de estado de instalação era silenciosamente inoperante.
        /// </summary>
        private int GetInstallState(SP_DEVINFO_DATA devInfo)
        {
            try
            {
                uint regType, requiredSize;
                SetupDiGetDeviceRegistryProperty(_deviceInfoSet, ref devInfo, SPDRP_INSTALL_STATE, out regType, null, 0, out requiredSize);
                if (requiredSize < sizeof(uint)) return 0;

                var buffer = new byte[requiredSize];
                if (SetupDiGetDeviceRegistryProperty(_deviceInfoSet, ref devInfo, SPDRP_INSTALL_STATE, out regType, buffer, requiredSize, out requiredSize))
                {
                    return unchecked((int)BitConverter.ToUInt32(buffer, 0));
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SetupApiEnum] Falha ao ler SPDRP_INSTALL_STATE: {ex.GetType().Name}: {ex.Message}");
            }
            return 0;
        }

        private string GetDevicePropertyString(SP_DEVINFO_DATA devInfo, uint property)
        {
            try
            {
                uint regType;
                uint requiredSize;
                SetupDiGetDeviceRegistryProperty(_deviceInfoSet, ref devInfo, property, out regType, null, 0, out requiredSize);
                if (requiredSize == 0) return null;
                
                byte[] buffer = new byte[requiredSize];
                if (SetupDiGetDeviceRegistryProperty(_deviceInfoSet, ref devInfo, property, out regType, buffer, requiredSize, out requiredSize))
                {
                    return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
                }
            }
            catch (Exception ex)
            {
                // Antes era "catch { }" total: uma falha de leitura de propriedade DPDK nunca
                // aparecia em lugar nenhum. Agora é contada e reportada no resumo da varredura.
                Interlocked.Increment(ref _propertyReadFailures);
                App.LoggingService?.LogDebug($"[SetupApiEnum] Leitura de propriedade 0x{property:X} falhou: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        private string GetDevicePropertyMultiSz(SP_DEVINFO_DATA devInfo, uint property)
        {
            try
            {
                uint regType;
                uint requiredSize;
                SetupDiGetDeviceRegistryProperty(_deviceInfoSet, ref devInfo, property, out regType, null, 0, out requiredSize);
                if (requiredSize == 0) return null;
                
                byte[] buffer = new byte[requiredSize];
                if (SetupDiGetDeviceRegistryProperty(_deviceInfoSet, ref devInfo, property, out regType, buffer, requiredSize, out requiredSize))
                {
                    List<string> strings = new List<string>();
                    int start = 0;
                    for (int i = 0; i < buffer.Length - 1; i += 2)
                    {
                        if (buffer[i] == 0 && buffer[i + 1] == 0)
                        {
                            if (start < i)
                            {
                                string s = Encoding.Unicode.GetString(buffer, start, i - start);
                                if (!string.IsNullOrEmpty(s)) strings.Add(s);
                            }
                            start = i + 2;
                        }
                    }
                    return string.Join(";", strings);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _propertyReadFailures);
                App.LoggingService?.LogDebug($"[SetupApiEnum] Leitura MultiSz de propriedade 0x{property:X} falhou: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Dados do driver instalado, lidos do registro da classe de setup.
        /// </summary>
        private readonly struct InstalledDriverInfo
        {
            public string? InfPath { get; init; }
            public string Version { get; init; }
            public string Date { get; init; }
            public string Provider { get; init; }
            public string DriverKey { get; init; }
        }

        /// <summary>
        /// Lê TODOS os dados do driver instalado abrindo a chave de registro UMA única vez.
        /// A implementação anterior abria a mesma chave 4 vezes por dispositivo (InfPath,
        /// DriverVersion, DriverDate, ProviderName) — em um hardware típico com 150+ dispositivos
        /// isso significava 600 aberturas de registro por varredura, executadas na thread de UI.
        /// </summary>
        private InstalledDriverInfo ReadInstalledDriverInfo(SP_DEVINFO_DATA devInfo)
        {
            string driverKey = GetDevicePropertyString(devInfo, SPDRP_DRIVER);
            if (string.IsNullOrEmpty(driverKey))
                return new InstalledDriverInfo { Version = "0.0.0.0", Date = "", Provider = "Desconhecido", DriverKey = "" };

            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Control\Class\{driverKey}");

                if (key == null)
                    return new InstalledDriverInfo { Version = "0.0.0.0", Date = "", Provider = "Desconhecido", DriverKey = driverKey };

                return new InstalledDriverInfo
                {
                    InfPath = key.GetValue("InfPath") as string,
                    Version = key.GetValue("DriverVersion") as string ?? "0.0.0.0",
                    Date = key.GetValue("DriverDate") as string ?? "",
                    Provider = key.GetValue("ProviderName") as string ?? "Desconhecido",
                    DriverKey = driverKey
                };
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _propertyReadFailures);
                App.LoggingService?.LogDebug($"[SetupApiEnum] Leitura do registro do driver '{driverKey}' falhou: {ex.GetType().Name}: {ex.Message}");
                return new InstalledDriverInfo { Version = "0.0.0.0", Date = "", Provider = "Desconhecido", DriverKey = driverKey };
            }
        }

        public void Dispose()
        {
            if (_deviceInfoSet != IntPtr.Zero && _deviceInfoSet != new IntPtr(-1))
            {
                SetupDiDestroyDeviceInfoList(_deviceInfoSet);
                _deviceInfoSet = IntPtr.Zero;
            }
        }
    }
}
