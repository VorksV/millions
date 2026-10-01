using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Detector primário: consulta o CATÁLOGO LOCAL por Hardware ID.
    ///
    /// Este é o detector que realmente funciona, e a razão é arquitetural. Gerenciadores
    /// comerciais (Driver Booster e similares) NÃO raspam o site do fabricante: eles mantêm
    /// um banco de dados próprio, indexado por Hardware ID, e o consultam localmente. Quando a
    /// Intel publica um driver novo, o processo de curadoria deles atualiza o banco; o
    /// aplicativo do usuário apenas faz a consulta.
    ///
    /// O VOLTRIS antes fazia o oposto — raspar intel.com em tempo real — e recebia HTTP 403 em
    /// 100% das requisições (39/39 no log), porque o intel.com tem proteção anti-bot. Nenhuma
    /// quantidade de ajuste de HTTP Client resolve isso: o problema é o modelo, não o header.
    ///
    /// Quando o catálogo não tem entrada para o dispositivo, este detector devolve null e a
    /// interface informa o motivo. NÃO há versão padrão, NÃO há data inventada, NÃO há URL de
    /// página de consulta.
    /// </summary>
    public sealed class CatalogDriverDetector : IDriverDetector
    {
        public Task<DriverPackage> GetLatestDriverAsync(CurrentDriverInfo currentInfo, CancellationToken cancellationToken = default)
        {
            var catalog = DriverCatalog.Instance;

            if (catalog.Count == 0)
            {
                App.LoggingService?.LogDebug(
                    $"[CatalogDetector] Catálogo vazio — nenhuma informação para '{currentInfo.DeviceName}'.");
                return Task.FromResult<DriverPackage>(null!);
            }

            // CurrentDriverInfo só carrega o primeiro Hardware ID; reconstruímos o DeviceInfo
            // mínimo para que o casamento considere todos os IDs do dispositivo.
            var device = new DeviceInfo
            {
                FriendlyName = currentInfo.DeviceName,
                HardwareIds = currentInfo.HardwareId,
                Vendor = currentInfo.Vendor,
                ClassGuid = currentInfo.ClassGuid,
                DriverVersion = currentInfo.Version
            };

            var entry = catalog.Find(device);
            if (entry == null)
            {
                App.LoggingService?.LogDebug(
                    $"[CatalogDetector] Sem entrada de catálogo para '{currentInfo.DeviceName}' (HWID '{currentInfo.HardwareId}').");
                return Task.FromResult<DriverPackage>(null!);
            }

            var package = catalog.ToPackage(entry, device);
            if (package == null)
            {
                App.LoggingService?.LogWarning(
                    $"[CatalogDetector] Entrada de catálogo para '{currentInfo.DeviceName}' REJEITADA: " +
                    "fonte, versão ou URL do artefato ausentes. A entrada provavelmente foi mal cadastrada.");
                return Task.FromResult<DriverPackage>(null!);
            }

            // Só oferece se for realmente mais novo que o instalado.
            if (!string.IsNullOrWhiteSpace(currentInfo.Version) && package.Version != null)
            {
                if (Version.TryParse(NormalizeVersion(package.Version), out var candidate) &&
                    Version.TryParse(NormalizeVersion(currentInfo.Version), out var installed) &&
                    candidate <= installed)
                {
                    App.LoggingService?.LogDebug(
                        $"[CatalogDetector] '{currentInfo.DeviceName}': catálogo tem {candidate}, instalado é {installed}. Nada a fazer.");
                    return Task.FromResult<DriverPackage>(null!);
                }
            }

            App.LoggingService?.LogSuccess(
                $"[CatalogDetector] '{currentInfo.DeviceName}': catálogo indica {package.Version} " +
                $"(fonte: {package.ProvenanceLabel}, instalado: {currentInfo.Version}).");

            return Task.FromResult<DriverPackage>(package);
        }

        private static string NormalizeVersion(string raw)
        {
            var parts = raw.Split('.', StringSplitOptions.RemoveEmptyEntries)
                           .Where(p => p.All(char.IsDigit))
                           .Take(4)
                           .ToList();
            while (parts.Count < 2) parts.Add("0");
            return string.Join('.', parts);
        }
    }
}
