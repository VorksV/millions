using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace VoltrisOptimizer.Services.Hardware
{
    /// <summary>
    /// LEITURA REAL DE TEMPERATURA DE GPU VIA API OFICIAL DO FABRICANTE.
    ///
    /// POR QUE EXISTE
    /// Temperatura de GPU nao exige driver de kernel: cada fabricante publica
    /// uma biblioteca que ja vem instalada com o driver da placa. Cobrimos:
    ///
    ///   NVIDIA -> nvml.dll         (nvmlDeviceGetTemperature)
    ///   AMD    -> amdadlxy64.dll   (ADL OverdriveN, funcoes planas estaveis)
    ///   Intel  -> IGCL             (somente GPU Intel DISCRETA; ver nota)
    ///
    /// TUDO CARREGADO DINAMICAMENTE (LoadLibrary/GetProcAddress): se a
    /// biblioteca nao existir na maquina, o metodo devolve "indisponivel".
    /// Sem dependencia dura, sem falha de carga de assembly, sem risco em
    /// maquina sem GPU da marca.
    ///
    /// Diferenca em relacao a invocar nvidia-smi.exe: aqui usamos a DLL
    /// direto, sem criar processo, evitando ~200 ms por consulta.
    /// </summary>
    public static class GpuVendorTemperature
    {
        private const int NvmlSensorGpu = 0;

        // ── NVIDIA (NVML) ──

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlDevice
        {
            public IntPtr Handle;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlInit_t();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlShutdown_t();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlGetCount_t(ref uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlGetHandle_t(uint index, ref NvmlDevice device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlGetTemp_t(NvmlDevice device, int sensorType, ref uint temp);

        // ── AMD (ADL OverdriveN) ──

        // Tipo 0 = controlador 1, endpoint EDN (placa fisica).
        private const int AdlControllerThermal = 0;
        private const int AdlTemperatureTypeEdge = 0;
        private const int AdlTemperatureTypeJunction = 1;
        private const int AdlTemperatureTypeMem = 2;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlTempControllerGet_t(int adapterIndex, int controllerType);

        /// <summary>
        /// <c>ADL_OverdriveN_Temperature_Get(int iAdapterIndex,
        /// int isTemperatureEndpoint, ADL_ODNTemperatureType temperatureType,
        /// int *lpTemperature)</c> — o ultimo parametro e' ponteiro de saida.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlTempGet_t(int adapterIndex, int isTemperatureEndpoint, int temperatureType, ref int lpTemperature);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpLibFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        /// <summary>
        /// Tenta ler temperatura REAL da GPU. Tenta NVIDIA, depois AMD.
        /// </summary>
        public static ThermalReading TryReadRealTemperature()
        {
            ThermalReading nvidia = TryNvidia();
            if (nvidia.IsValid) return nvidia;

            ThermalReading amd = TryAmd();
            if (amd.IsValid) return amd;

            // Intel: ver TryIntel(). Nao ha entry point estavel publicado.
            return ThermalReading.Unavailable;
        }

        // ═══════════════════════════════════════════════════════════
        // NVIDIA — NVML
        // ═══════════════════════════════════════════════════════════

        private static ThermalReading TryNvidia()
        {
            try
            {
                string? path = FindLibrary("nvml.dll");
                if (path == null) return ThermalReading.Unavailable;

                IntPtr lib = LoadLibraryW(path);
                if (lib == IntPtr.Zero) return ThermalReading.Unavailable;

                try
                {
                    IntPtr pInit = GetProcAddress(lib, "nvmlInit_v2");
                    if (pInit == IntPtr.Zero) pInit = GetProcAddress(lib, "nvmlInit");
                    if (pInit == IntPtr.Zero) return ThermalReading.Unavailable;

                    if (Marshal.GetDelegateForFunctionPointer<NvmlInit_t>(pInit)() != 0)
                        return ThermalReading.Unavailable;

                    IntPtr pCount = GetProcAddress(lib, "nvmlDeviceGetCount");
                    if (pCount == IntPtr.Zero) return ThermalReading.Unavailable;

                    uint count = 0;
                    if (Marshal.GetDelegateForFunctionPointer<NvmlGetCount_t>(pCount)(ref count) != 0 || count == 0)
                        return ThermalReading.Unavailable;

                    IntPtr pHandle = GetProcAddress(lib, "nvmlDeviceGetHandleByIndex_v2");
                    if (pHandle == IntPtr.Zero) pHandle = GetProcAddress(lib, "nvmlDeviceGetHandleByIndex");

                    IntPtr pTemp = GetProcAddress(lib, "nvmlDeviceGetTemperature");
                    if (pHandle == IntPtr.Zero || pTemp == IntPtr.Zero) return ThermalReading.Unavailable;

                    var delHandle = Marshal.GetDelegateForFunctionPointer<NvmlGetHandle_t>(pHandle);
                    var delTemp = Marshal.GetDelegateForFunctionPointer<NvmlGetTemp_t>(pTemp);

                    double hottest = double.NaN;
                    for (uint i = 0; i < count; i++)
                    {
                        var dev = new NvmlDevice();
                        if (delHandle(i, ref dev) != 0) continue;

                        uint temp = 0;
                        if (delTemp(dev, NvmlSensorGpu, ref temp) != 0) continue;
                        if (temp == 0 || temp > 150) continue;   // 0 = leitura invalida

                        if (double.IsNaN(hottest) || temp > hottest) hottest = temp;
                    }

                    TryNvmlShutdown(lib);
                    return double.IsNaN(hottest)
                        ? ThermalReading.Unavailable
                        : new ThermalReading(hottest, ThermalReadingSource.VendorApi);
                }
                finally
                {
                    FreeLibrary(lib);
                }
            }
            catch
            {
                return ThermalReading.Unavailable;
            }
        }

        private static void TryNvmlShutdown(IntPtr lib)
        {
            try
            {
                IntPtr p = GetProcAddress(lib, "nvmlShutdown");
                if (p != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<NvmlShutdown_t>(p)();
            }
            catch { /* shutdown e' opcional */ }
        }

        // ═══════════════════════════════════════════════════════════
        // AMD — ADL OverdriveN
        // ═══════════════════════════════════════════════════════════

        private static ThermalReading TryAmd()
        {
            try
            {
                string? path = FindLibrary("amdadlxy64.dll")
                            ?? FindLibrary("amdadl64.dll");
                if (path == null) return ThermalReading.Unavailable;

                IntPtr lib = LoadLibraryW(path);
                if (lib == IntPtr.Zero) return ThermalReading.Unavailable;

                try
                {
                    IntPtr pCtrl = GetProcAddress(lib, "ADL_OverdriveN_Temperature_Controller_Get");
                    IntPtr pGet = GetProcAddress(lib, "ADL_OverdriveN_Temperature_Get");
                    if (pCtrl == IntPtr.Zero || pGet == IntPtr.Zero)
                        return ThermalReading.Unavailable;

                    var delCtrl = Marshal.GetDelegateForFunctionPointer<AdlTempControllerGet_t>(pCtrl);
                    var delGet = Marshal.GetDelegateForFunctionPointer<AdlTempGet_t>(pGet);

                    // AdapterIndex -1 consulta todas as placas; a funcao de
                    // controller devolve quantas responderam.
                    int controllers = 0;
                    if (delCtrl(-1, AdlControllerThermal) < 1) return ThermalReading.Unavailable;

                    double hottest = double.NaN;
                    for (int adapter = 0; adapter < controllers && adapter < 16; adapter++)
                    {
                        // Junction e a leitura mais representativa; edge e o
                        // sensor da placa. Preferimos junction, com edge como
                        // alternativa.
                        foreach (int type in new[] { AdlTemperatureTypeJunction, AdlTemperatureTypeEdge })
                        {
                            int temp = 0;
                            if (delGet(adapter, 0, type, ref temp) != 0) continue;
                            if (temp <= 0 || temp > 150) continue;

                            if (double.IsNaN(hottest) || temp > hottest) hottest = temp;
                            break;   // junction basta para esta placa
                        }
                    }

                    return double.IsNaN(hottest)
                        ? ThermalReading.Unavailable
                        : new ThermalReading(hottest, ThermalReadingSource.VendorApi);
                }
                finally
                {
                    FreeLibrary(lib);
                }
            }
            catch
            {
                return ThermalReading.Unavailable;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Intel — IGCL
        // ═══════════════════════════════════════════════════════════
        //
        // NOTA HONESTA: a IGCL (Intel Graphics Compute Library) e' um wrapper
        // based em C++ cuja ABI muda entre versoes; nao existe entry point
        // plano e documentado para temperatura. Pior: a grande maioria das
        // GPUs Intel e' INTEGRADA, e iGPU nao expoe sensor de temperatura
        // para nenhum software — nem HWiNFO, nem IGCL. Ou seja, mesmo com a
        // implementacao pronta, a iGPU (como a Iris Xe) continuaria sem
        // leitura real.
        //
        // O caminho honesto para Intel e a estimativa ancorada na temperatura
        // real da CPU, porque o die compartilhado emite calor em conjunto.
        private static ThermalReading TryIntel() => ThermalReading.Unavailable;

        // ── Utilitarios ──

        private static string? FindLibrary(string fileName)
        {
            string root = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            string full = System.IO.Path.Combine(root, "System32", fileName);
            return System.IO.File.Exists(full) ? full : null;
        }

        /// <summary>
        /// Heuristica auxiliar: GPU integrada costuma compartilhar o
        /// encapsulamento com a CPU. Usada so como ancora da estimativa,
        /// nunca como leitura real.
        /// </summary>
        public static bool LooksIntegrated(string? adapterName)
        {
            if (string.IsNullOrWhiteSpace(adapterName)) return false;

            return Regex.IsMatch(
                adapterName,
                @"HD\s|UHD|Iris|Iris Xe|Iris Plus|Iris Pro|Lumina|Apple GPU|llvmpipe",
                RegexOptions.IgnoreCase);
        }
    }
}
