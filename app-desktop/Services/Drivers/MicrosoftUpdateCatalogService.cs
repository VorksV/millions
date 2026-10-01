using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;

namespace VoltrisOptimizer.Services.Drivers
{
    public class MicrosoftUpdateCatalogService
    {

        public async Task<List<DriverUpdate>> SearchCatalogAsync(DeviceInfo device, System.Threading.CancellationToken cancellationToken = default)
        {
            var results = new List<DriverUpdate>();
            if (string.IsNullOrEmpty(device.HardwareIds)) return results;

            // Tentamos do mais específico para o menos específico (Truncamento inteligente)
            string[] ids = device.HardwareIds.Split(';');
            var searchQueries = new List<string>();
            
            foreach (var id in ids.Take(5))
            {
                searchQueries.Add(id);
                // Truncamento: Se o ID contém "&SUBSYS_", tentamos a versão sem subsys
                if (id.Contains("&SUBSYS_"))
                {
                    int subsysIndex = id.IndexOf("&SUBSYS_");
                    searchQueries.Add(id.Substring(0, subsysIndex));
                }
            }

            foreach (var query in searchQueries.Distinct())
            {
                try
                {
                    App.LoggingService?.LogInfo($"[Catalog] Pesquisando Query no Microsoft Update Catalog: {query}");
                    string url = $"https://www.catalog.update.microsoft.com/Search.aspx?q={Uri.EscapeDataString(query)}";
                    
                    using var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(15));
                    
                    string html = await (await DriverHttpClient.Instance.GetAsync(url, linkedCts.Token)).Content.ReadAsStringAsync();

                    var matches = Regex.Matches(html, @"goToDetails\('([^']+)'\).*?td.*?title=""([^""]+)"".*?td.*?title=""([^""]+)"".*?td.*?title=""([^""]+)""", RegexOptions.Singleline);

                    if (matches.Count > 0)
                    {
                        foreach (Match m in matches.Cast<Match>().Take(3))
                        {
                            string updateId = m.Groups[1].Value;
                            string title = m.Groups[2].Value;
                            string manufacturer = m.Groups[3].Value;
                            string version = m.Groups[4].Value;

                            App.LoggingService?.LogInfo($"[Catalog] Candidato encontrado: {title} ({version})");
                            
                            string downloadUrl = await GetDownloadUrlAsync(updateId, cancellationToken);

                            if (!string.IsNullOrEmpty(downloadUrl))
                            {
                                results.Add(new DriverUpdate
                                {
                                    Device = device,
            NewDriver = new DriverPackage
            {
                HardwareId = query, // Fix: query instead of id
                Title = title,
                Vendor = manufacturer,
                Version = version,
                DownloadUrl = downloadUrl,
                ProductName = "Microsoft Update Catalog",
                InfFile = "catalog.inf",
                // Fonte auditável: o pacote é servido e assinado pela Microsoft, e a URL é
                // obtida do próprio diálogo de download do catálogo.
                Provenance = DriverUpdateProvenance.MicrosoftUpdateCatalog,
                SourceReference = updateId
            },
                                    UpdateReason = UpdateReason.HardwareRepair
                                });
                                break;
                            }
                        }
                    }

                    if (results.Any()) break; // Para se achou algo funcional
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError($"[Catalog] Erro na busca por {query}", ex);
                }
            }

            return results;
        }

        private async Task<string?> GetDownloadUrlAsync(string updateId, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                // O catálogo usa uma página de download separada que gera links dinâmicos
                // Simulação simplificada de extração do link direto do .cab
                string downloadPageUrl = "https://www.catalog.update.microsoft.com/DownloadDialog.aspx";
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("updateIDs", $"[{{\"uid\":\"{updateId}\",\"clcl\":\"\"}}]")
                });

                using var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(10));

                var response = await DriverHttpClient.Instance.PostAsync(downloadPageUrl, content, linkedCts.Token);
                string html = await response.Content.ReadAsStringAsync();

                // Regex para pegar o link direto (geralmente termina em .cab ou .exe)
                var match = Regex.Match(html, @"downloadUrl\[\d+\] = '(http[^']+)';");
                if (match.Success) return match.Groups[1].Value;
                
                // Fallback regex se o formato variar
                var matchAlt = Regex.Match(html, @"(http[s]?://download\.microsoft\.com/[^'""\s]+\.(?:cab|exe|msi|zip))", RegexOptions.IgnoreCase);
                return matchAlt.Success ? matchAlt.Value : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
