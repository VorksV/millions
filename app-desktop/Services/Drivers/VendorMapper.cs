using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Drivers
{
    public static class VendorMapper
    {
        /// <summary>Fabricante quando o Hardware ID não corresponde a nenhum vendor documentado.</summary>
        public const string UnknownVendor = "Unknown";

        /// <summary>Fabricante para componentes enraizados do sistema (ROOT\, ACPI\, USB\ROOT).</summary>
        public const string MicrosoftVendor = "Microsoft / Sistema";

        private static readonly Dictionary<string, string> _vendorMap = new()
        {
            { "VEN_10DE", "NVIDIA" },
            { "VEN_1002", "AMD" },
            { "VEN_8086", "Intel" },
            { "VEN_8087", "Intel" },
            { "VEN_10EC", "Realtek" },
            { "VEN_14E4", "Broadcom" },
            { "VEN_168C", "Atheros/Qualcomm" },
            { "VEN_1969", "Qualcomm Atheros" },
            { "VEN_11AB", "Marvell" },
            { "VEN_144D", "Samsung" },
            { "VEN_C0A9", "Crucial/Micron" },
            { "VEN_1B21", "ASMedia" },
            { "VEN_1B73", "Fresco Logic" },
            { "VEN_1106", "VIA Technologies" },
            { "VEN_1022", "AMD" },
            { "VEN_152D", "JMicron" },
            { "VEN_197B", "JMicron" },
            { "VEN_104C", "Texas Instruments" },
            { "VEN_1274", "Creative Labs" },
            { "VEN_13F6", "C-Media" },
            { "VEN_100B", "National Semiconductor" },
            { "VEN_1011", "Digital Equipment" },
            { "VEN_1014", "IBM" },
            { "VEN_1028", "Dell" },
            { "VEN_103B", "HP" },
            { "VEN_103C", "HP" },
            { "VEN_17AA", "Lenovo" },
            { "VEN_0B05", "ASUS" },
            { "VEN_1414", "Microsoft" },
            { "VEN_2646", "Kingston" },
            { "VEN_1B1C", "Corsair" },
            { "VEN_1532", "Razer" },
            { "VEN_046D", "Logitech" },
            { "VEN_413C", "Dell" },
            { "VEN_1050", "Yubico" }
        };

        /// <summary>
        /// Resolve o fabricante a partir do primeiro Hardware ID.
        /// <paramref name="verbose"/> gera uma linha de log por dispositivo. A varredura da página
        /// de Drivers deve usar <c>false</c>: em hardware típico são 150+ dispositivos, e o modo
        /// verboso produzia 144 linhas INFO + 24 WARNING por varredura sem acrescentar informação
        /// (o resumo por categoria já é registrado no fim da enumeração).
        /// </summary>
        public static string GetVendor(string? hardwareId, bool verbose = false)
        {
            if (string.IsNullOrEmpty(hardwareId)) return UnknownVendor;

            if (hardwareId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) || 
                hardwareId.StartsWith("ACPI\\", StringComparison.OrdinalIgnoreCase) ||
                hardwareId.StartsWith("USB\\ROOT", StringComparison.OrdinalIgnoreCase))
            {
                if (verbose)
                    App.LoggingService?.LogInfo($"[VendorMapper] Componente do ecossistema Microsoft/Sistema: {hardwareId}");
                return MicrosoftVendor;
            }

            try
            {
                foreach (var kv in _vendorMap)
                {
                    // Busca por VEN_xxxx (PCI) ou VID_xxxx (USB)
                    string vid = kv.Key.Replace("VEN_", "VID_");
                    if (hardwareId.Contains(kv.Key, StringComparison.OrdinalIgnoreCase) || 
                        hardwareId.Contains(vid, StringComparison.OrdinalIgnoreCase))
                    {
                        if (verbose)
                            App.LoggingService?.LogInfo($"[VendorMapper] Hardware ID '{hardwareId}' pertence a {kv.Value}");
                        return kv.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[VendorMapper] Falha ao destrinchar Hardware ID '{hardwareId}'. {ex.Describe()}");
            }

            if (verbose)
                App.LoggingService?.LogWarning($"[VendorMapper] Hardware OEM nao documentado / Generico. ID: {hardwareId}");
            return UnknownVendor;
        }

        /// <summary>Resolução de fabricante para os principais IDs de chipset, sem varrer o dicionário inteiro.</summary>
        public static string ResolveFromHardwareIds(string? hardwareIds)
        {
            if (string.IsNullOrEmpty(hardwareIds)) return UnknownVendor;
            foreach (var id in hardwareIds.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var vendor = GetVendor(id);
                if (vendor != UnknownVendor) return vendor;
            }
            return UnknownVendor;
        }
    }
}
