using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Registro de um driver no catálogo local, indexado por Hardware ID.
    ///
    /// ARQUITETURA: este é o modelo que gerenciadores comerciais de driver (Driver Booster,
    /// Snappy Driver Installer, etc.) usam, e a diferença é a razão pela qual eles
    /// encontram atualizações de Intel e o scraping não encontra.
    ///
    ///   Gerenciador comercial: CATÁLOGO PRÓPRIO (HWID -> pacote) + servidor próprio.
    ///   Scraping:              lê o site do fabricante em tempo real -> HTTP 403 / JS / layout.
    ///
    /// O catálogo é alimentado apenas por fontes legítimas. Enquanto não houver entrada para
    /// um Hardware ID, a resposta é "sem informação" — nunca uma versão inventada.
    /// </summary>
    public sealed class DriverCatalogEntry
    {
        /// <summary>Hardware ID completo, ex.: PCI\VEN_8086&DEV_A0F0&SUBSYS_02A48086&REV_20</summary>
        [JsonPropertyName("hwid")] public string HardwareId { get; set; } = string.Empty;

        /// <summary>HWID sem revisão, para casar revisões diferentes do mesmo dispositivo.</summary>
        [JsonPropertyName("hwidNoRev")] public string HardwareIdNoRevision { get; set; } = string.Empty;

        [JsonPropertyName("vendor")] public string Vendor { get; set; } = string.Empty;
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;

        /// <summary>Data de lançamento real, preenchida pela fonte. Vazio = desconhecida.</summary>
        [JsonPropertyName("releaseDate")] public string? ReleaseDate { get; set; }

        /// <summary>URL direta do artefato (.zip/.cab/.exe/.msi). Nunca uma página de consulta.</summary>
        [JsonPropertyName("downloadUrl")] public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
        [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
        [JsonPropertyName("infName")] public string? InfName { get; set; }

        /// <summary>Fonte que alimentou esta entrada — obrigatória para auditoria.</summary>
        [JsonPropertyName("source")] public DriverUpdateProvenance Source { get; set; } = DriverUpdateProvenance.None;

        /// <summary>Identificador da entrada na fonte (UpdateID do catálogo, id do DSA, etc.).</summary>
        [JsonPropertyName("sourceRef")] public string? SourceReference { get; set; }

        [JsonPropertyName("catalogVersion")] public int CatalogVersion { get; set; } = 1;

        public DateTime? ParsedReleaseDate =>
            DateTime.TryParse(ReleaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>
    /// Catálogo local de drivers, indexado por Hardware ID.
    ///
    /// É a fonte primária de verificação, exatamente como nos gerenciadores comerciais. O
    /// scraping de site de fabricante é apenas um SECUNDÁRIO e nunca decides sozinho se há
    /// atualização.
    ///
    /// O arquivo é <c>driver_catalog.json</c> ao lado do executável. Ele começa vazio e o
    /// aplicativo informa honestamente o estado do catálogo na interface — nunca exibe uma
    /// versão que não veio de uma entrada verificada.
    /// </summary>
    public sealed class DriverCatalog
    {
        private static readonly Lazy<DriverCatalog> _instance = new(() => new DriverCatalog());

        public static DriverCatalog Instance => _instance.Value;

        private readonly string _path;
        private Dictionary<string, DriverCatalogEntry> _byFullHwid;
        private Dictionary<string, DriverCatalogEntry> _byHwidNoRevision;
        private DateTime _lastLoadedUtc;
        private int _loadFailures;

        private DriverCatalog()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DriverData");
            _path = Path.Combine(dir, "driver_catalog.json");
            Reload();
        }

        public int Count => _byFullHwid.Count;
        public int CountWithoutRevision => _byHwidNoRevision.Count;
        public DateTime LastLoadedUtc => _lastLoadedUtc;
        public int LoadFailures => _loadFailures;
        public string CatalogPath => _path;

        /// <summary>
        /// Resumo auditável do estado do catálogo, exibido na interface para que o usuário
        /// saiba exatamente por que uma atualização aparece ou não.
        /// </summary>
        public string DescribeState()
        {
            if (_loadFailures > 0)
                return $"Catálogo indisponível ({_loadFailures} falha(s) de leitura em {_path})";

            if (Count == 0)
                return $"Catálogo vazio — nenhum driver cadastrado. Arquivo esperado em: {_path}";

            return $"Catálogo: {Count} entrada(s) por HWID exato, {CountWithoutRevision} por HWID sem revisão. Atualizado em {_lastLoadedUtc:yyyy-MM-dd HH:mm} UTC";
        }

        public void Reload()
        {
            _byFullHwid = new Dictionary<string, DriverCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            _byHwidNoRevision = new Dictionary<string, DriverCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            _lastLoadedUtc = DateTime.UtcNow;

            try
            {
                if (!File.Exists(_path)) return;

                string json = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(json)) return;

                var entries = JsonSerializer.Deserialize<List<DriverCatalogEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (entries == null) return;

                foreach (var entry in entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.HardwareId)) continue;

                    string key = Normalize(entry.HardwareId);
                    if (key.Length == 0) continue;

                    _byFullHwid[key] = entry;

                    string noRev = string.IsNullOrWhiteSpace(entry.HardwareIdNoRevision)
                        ? StripRevision(key)
                        : Normalize(entry.HardwareIdNoRevision);

                    if (noRev.Length > 0 && !_byHwidNoRevision.ContainsKey(noRev))
                        _byHwidNoRevision[noRev] = entry;
                }
            }
            catch (Exception ex)
            {
                _loadFailures++;
                App.LoggingService?.LogError(
                    $"[DriverCatalog] Falha ao carregar '{_path}'. O VOLTRIS operará sem catálogo. {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Procura a entrada do catálogo para um dispositivo, com precedência:
        ///   1) HWID exato (inclui SUBSYS)  — correspondência mais precisa
        ///   2) HWID sem revisão            — cobre diferenças de revisão do firmware
        /// Nunca há correspondência aproximada por nome: isso é o que evita oferecer um driver
        /// de Wi-Fi para um dispositivo de Bluetooth.
        /// </summary>
        public DriverCatalogEntry? Find(DeviceInfo device)
        {
            foreach (var id in device.HardwareIdList)
            {
                string key = Normalize(id);
                if (key.Length == 0) continue;

                if (_byFullHwid.TryGetValue(key, out var exact)) return exact;

                string noRev = StripRevision(key);
                if (noRev.Length > 0 && _byHwidNoRevision.TryGetValue(noRev, out var fuzzy)) return fuzzy;
            }
            return null;
        }

        /// <summary>
        /// Converte uma entrada do catálogo em um pacote oferecível ao usuário.
        /// Devolve null se a entrada não passar nas garantias mínimas: fonte registrada,
        /// versão preenchida e URL que é de fato um artefato de driver.
        /// </summary>
        public DriverPackage? ToPackage(DriverCatalogEntry entry, DeviceInfo device)
        {
            if (entry.Version is not { Length: > 0 } || entry.DownloadUrl is not { Length: > 0 })
                return null;

            var package = new DriverPackage
            {
                HardwareId = device.HardwareIdList.FirstOrDefault() ?? string.Empty,
                Vendor = entry.Vendor,
                Version = entry.Version,
                ReleaseDate = entry.ParsedReleaseDate ?? default,
                DownloadUrl = entry.DownloadUrl,
                SourceUrl = entry.DownloadUrl,
                Sha256 = entry.Sha256 ?? string.Empty,
                FileSize = entry.SizeBytes,
                InfFile = entry.InfName ?? string.Empty,
                Title = string.IsNullOrWhiteSpace(entry.Title)
                    ? $"{entry.Vendor} {entry.Version}"
                    : entry.Title,
                Provenance = entry.Source,
                SourceReference = entry.SourceReference ?? string.Empty
            };

            // A entrada do catálogo só é oferecida se o próprio pacote for verificável.
            return package.IsVerified ? package : null;
        }

        // ------------------------------------------------------------------ normalização

        private static string Normalize(string hardwareId) =>
            hardwareId.Replace('/', '\\').Trim().TrimEnd('\\').ToUpperInvariant();

        /// <summary>
        /// Remove REV_xx e MI_xx do HWID. PCI\VEN_8086&DEV_A0F0&SUBSYS_02A48086&REV_20
        /// vira PCI\VEN_8086&DEV_A0F0&SUBSYS_02A48086 — casando revisões diferentes.
        /// </summary>
        private static string StripRevision(string normalizedHwid)
        {
            int rev = normalizedHwid.IndexOf("&REV_", StringComparison.Ordinal);
            int mi = normalizedHwid.IndexOf("&MI_", StringComparison.Ordinal);

            int cut = -1;
            if (rev >= 0 && mi >= 0) cut = Math.Min(rev, mi);
            else if (rev >= 0) cut = rev;
            else if (mi >= 0) cut = mi;

            string result = cut >= 0 ? normalizedHwid[..cut] : normalizedHwid;
            return result.TrimEnd('&', '\\');
        }
    }
}
