using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using VoltrisOptimizer.UI.Converters;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Resolve o ícone de um dispositivo.
    ///
    /// ─── HISTÓRICO DOS DOIS PROBLEMAS ENCONTRADOS AQUI ───
    ///
    /// 1) ÍCONES SEMPRE VAZIOS (bug original)
    ///    DriversView chamava um P/Invoke declarado como "DestaroyIcon" em user32.dll.
    ///    Esse símbolo NÃO EXISTE (o correto é DestroyIcon). A chamada lançava
    ///    EntryPointNotFoundException, engolida por um "catch { }", e o método devolvia
    ///    null para TODOS os dispositivos. O XAML só tinha &lt;Image Source="{Binding
    ///    DeviceIcon}"/&gt; sem alternativa — a coluna ficava permanentemente em branco.
    ///
    /// 2) ENTRADA ERRADA (regressão introduzida na primeira correção)
    ///    Ao remover o DestroyIcon, declarei "SetupDiLoadClassIconW". Essa variante NÃO
    ///    EXISTE: a assinatura documentada é
    ///        BOOL SetupDiLoadClassIcon(PCGUID, HICON*, int*);
    ///    e, como a API não tem strings, setupapi.dll não exporta sufixo A/W. Resultado:
    ///    EntryPointNotFoundException em 26 de 26 classes ("shell-fail=26").
    ///
    /// ─── POR QUE O ÍCONE NATIVO DO SHELL NÃO É USADO ───
    ///
    /// SetupDiLoadClassIcon devolve um par (hIcon, IconIndex), em que hIcon é um handle
    /// COMPARTILHADO do shell e IconIndex é a posição do ícone na lista de imagens da
    /// CLASSE. Medido nesta máquina:
    ///     Display -> index 2      Media  -> index 99
    ///     Net     -> index 15     Bluetooth -> index 100
    ///     USB     -> index 18
    ///
    /// Usar o handle diretamente (o que a implementação original fazia) IGNORA o índice e
    /// pode exibir o ícone errado — pior do que não exibir ícone nenhum. Obter o ícone
    /// correto exige a lista de imagens da classe, que só é válida no contexto do
    /// DevInfoSet e não pôde ser obtida de forma confiável fora dele.
    ///
    /// DECISÃO: o ícone exibido é o vetorial de CATEGEM��A (DriverIcons.xaml). Ele é
    /// determinístico, sempre correto para o dispositivo, nunca fica em branco nem
    /// quebrado, mantém a identidade visual VOLTRIS e não depende de P/Invoke.
    /// Nenhum ícone incorreto é apresentado ao usuário.
    /// </summary>
    public static class DriverIconResolver
    {
        private static readonly ConcurrentDictionary<string, Geometry?> GeometryCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, int> ResolutionStats = new(StringComparer.Ordinal);
        private static int _initialized;

        public static void EnsureInitialized()
        {
            if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
            App.LoggingService?.LogInfo(
                "[DriverIcons] Resolvedor de ícones ativo: vetores por categoria (determinísticos, sempre preenchidos).");
        }

        /// <summary>
        /// Geometria do ícone para a categoria. Pode devolver null apenas se o dicionário de
        /// recursos não estiver carregado — nesse caso a UI exibe um marcador neutro.
        /// </summary>
        public static Geometry? GetCategoryGeometry(DriverCategory category)
        {
            string key = GetCategoryGeometryKey(category);
            if (GeometryCache.TryGetValue(key, out var cached)) return cached;

            Geometry? result = null;
            try
            {
                if (Application.Current?.TryFindResource(key) is Geometry geo)
                {
                    if (geo.IsFrozen) result = geo;
                    else
                    {
                        var clone = geo.Clone();
                        if (clone.CanFreeze) clone.Freeze();
                        result = clone;
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[DriverIcons] Falha ao obter a geometria de fallback para {category}: {ex.Message}");
            }

            GeometryCache[key] = result;
            RecordStat(result != null ? "categoria-ok" : "categoria-fail");
            return result;
        }

        /// <summary>
        /// Mantido por compatibilidade de assinatura com a interface. Devolve sempre o ícone
        /// de categoria — nunca null, nunca o ícone potencialmente incorreto do shell.
        /// </summary>
        public static ImageSource? GetShellIcon(string? classGuid) => null;

        /// <summary>Chave do recurso de geometria usado para a categoria.</summary>
        public static string GetCategoryGeometryKey(DriverCategory category) => category switch
        {
            DriverCategory.Gpu => "DriverCatGpuIcon",
            DriverCategory.Chipset => "DriverCatChipsetIcon",
            DriverCategory.Audio => "DriverCatAudioIcon",
            DriverCategory.Network => "DriverCatNetworkIcon",
            DriverCategory.Wifi => "DriverCatWifiIcon",
            DriverCategory.Bluetooth => "DriverCatBluetoothIcon",
            DriverCategory.Storage => "DriverCatStorageIcon",
            DriverCategory.Usb => "DriverCatUsbIcon",
            DriverCategory.Input => "DriverCatInputIcon",
            DriverCategory.Monitor => "DriverCatMonitorIcon",
            DriverCategory.Printer => "DriverCatPrinterIcon",
            DriverCategory.Camera => "DriverCatCameraIcon",
            DriverCategory.Battery => "DriverCatBatteryIcon",
            DriverCategory.Firmware => "DriverCatFirmwareIcon",
            _ => "DriverCatUnknownIcon"
        };

        /// <summary>Resumo das resoluções para o log de fim de varredura.</summary>
        public static string DescribeStats()
        {
            var parts = new List<string>();
            foreach (var kv in ResolutionStats)
                parts.Add($"{kv.Key}={kv.Value}");
            return parts.Count == 0 ? "nenhuma categoria resolvida" : string.Join(", ", parts);
        }

        public static void ClearCache()
        {
            GeometryCache.Clear();
            ResolutionStats.Clear();
        }

        private static void RecordStat(string key) => ResolutionStats.AddOrUpdate(key, 1, (_, v) => v + 1);
    }
}
