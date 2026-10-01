using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Exporta um relatório de diagnóstico completo e auditável do motor de drivers.
    /// Técnica padrão de ferramentas como Driver Booster, Snappy Driver Installer e CCleaner Driver Updater.
    /// </summary>
    public static class DriverDiagnosticsExporter
    {
        private static readonly string ReportDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoltrisOptimizer", "DriverDiagnostics");

        /// <summary>
        /// Gera um relatório completo em HTML e TXT para auditoria de engenharia.
        /// Exporta: hardware detectado, versões instaladas, versões online, decisões de update.
        /// </summary>
        public static string ExportFullReport(
            List<DeviceInfo> detectedDevices,
            List<DriverUpdate> foundUpdates,
            long scanDurationMs)
        {
            Directory.CreateDirectory(ReportDirectory);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string htmlPath = Path.Combine(ReportDirectory, $"DriverAudit_{timestamp}.html");
            string txtPath  = Path.Combine(ReportDirectory, $"DriverAudit_{timestamp}.txt");

            // === TXT REPORT (para leitura rápida nos logs) ===
            var txt = new StringBuilder();
            txt.AppendLine("╔══════════════════════════════════════════════════════════════════╗");
            txt.AppendLine($"║   VOLTRIS DRIVER ENGINE — AUDIT REPORT — {timestamp}   ║");
            txt.AppendLine("╠══════════════════════════════════════════════════════════════════╣");
            txt.AppendLine($"║  SO:      {Environment.OSVersion}");
            txt.AppendLine($"║  Máchina: {Environment.MachineName} ({Environment.UserName})");
            txt.AppendLine($"║  CPU:     {GetCpuInfo()}");
            txt.AppendLine($"║  Scan:    {scanDurationMs}ms | {detectedDevices?.Count ?? 0} devs | {foundUpdates?.Count ?? 0} updates");
            txt.AppendLine("╠══════════════════════════════════════════════════════════════════╣");
            txt.AppendLine("║                    DRIVERS INSTALADOS                           ║");
            txt.AppendLine("╠══════════════════════════════════════════════════════════════════╣");

            foreach (var dev in detectedDevices ?? new List<DeviceInfo>())
            {
                txt.AppendLine($"  [{dev.Vendor ?? "?":10}] {(dev.DeviceName ?? "?"),-45} v{dev.DriverVersion ?? "?.-"}");
                txt.AppendLine($"           HW-ID: {dev.HardwareIds?.Split(';').FirstOrDefault() ?? "—"}");
                txt.AppendLine($"           InstanceId: {dev.DeviceInstanceId ?? "—"}");
                txt.AppendLine($"           InfPath: {dev.DriverInfPath ?? "—"}");
                txt.AppendLine($"           Status: {(dev.IsRunning ? "✅ Ativo" : "⚠️ Inativo")} | Problema: {(dev.IsProblem ? "❌ Sim" : "OK")}");
                txt.AppendLine();
            }

            txt.AppendLine("╠══════════════════════════════════════════════════════════════════╣");
            txt.AppendLine("║                  ATUALIZAÇÕES ENCONTRADAS                       ║");
            txt.AppendLine("╠══════════════════════════════════════════════════════════════════╣");

            if (foundUpdates == null || foundUpdates.Count == 0)
            {
                txt.AppendLine("  Nenhuma atualização pendente detectada.");
            }
            else
            {
                foreach (var upd in foundUpdates)
                {
                    txt.AppendLine($"  🔼 {upd.Device?.DeviceName ?? "?"}");
                    txt.AppendLine($"     Instalado :  v{upd.CurrentVersion ?? "?"}");
                    txt.AppendLine($"     Disponível:  v{upd.NewDriver?.Version ?? "?"} ({upd.NewDriver?.ReleaseDate:yyyy-MM-dd})");
                    txt.AppendLine($"     Motivo    :  {upd.UpdateReason}");
                    txt.AppendLine($"     Fonte URL :  {upd.NewDriver?.SourceUrl ?? upd.NewDriver?.DownloadUrl ?? "—"}");
                    txt.AppendLine($"     SHA-256   :  {upd.NewDriver?.Sha256 ?? "—"}");
                    txt.AppendLine($"     HW-ID     :  {upd.Device?.HardwareIds?.Split(';').FirstOrDefault() ?? "—"}");
                    txt.AppendLine();
                }
            }

            txt.AppendLine("╚══════════════════════════════════════════════════════════════════╝");
            File.WriteAllText(txtPath, txt.ToString(), Encoding.UTF8);

            // === HTML REPORT (para análise visual profissional) ===
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html><html lang='pt-BR'><head>");
            html.AppendLine("<meta charset='UTF-8'>");
            html.AppendLine("<title>Voltris Driver Audit Report</title>");
            html.AppendLine("<style>");
            html.AppendLine("body{font-family:Consolas,monospace;background:#0d1117;color:#c9d1d9;margin:20px}");
            html.AppendLine("h1{color:#58a6ff}h2{color:#79c0ff;border-bottom:1px solid #30363d;padding-bottom:6px}");
            html.AppendLine("table{border-collapse:collapse;width:100%;font-size:13px}");
            html.AppendLine("th{background:#161b22;color:#58a6ff;text-align:left;padding:8px;border:1px solid #30363d}");
            html.AppendLine("td{padding:6px 8px;border:1px solid #21262d;vertical-align:top}");
            html.AppendLine("tr:nth-child(even) td{background:#161b22}");
            html.AppendLine(".ok{color:#3fb950}.warn{color:#d29922}.err{color:#f85149}");
            html.AppendLine(".badge{border-radius:4px;padding:2px 6px;font-size:11px;font-weight:bold}");
            html.AppendLine(".b-intel{background:#0071c5;color:#fff}.b-nvidia{background:#76b900;color:#000}");
            html.AppendLine(".b-amd{background:#ed1c24;color:#fff}.b-realtek{background:#c00;color:#fff}");
            html.AppendLine(".b-ms{background:#0078d4;color:#fff}.b-gen{background:#444;color:#fff}");
            html.AppendLine("</style></head><body>");

            html.AppendLine($"<h1>🛡️ Voltris Driver Audit — {timestamp}</h1>");
            html.AppendLine($"<p>Máquina: <b>{Environment.MachineName}</b> | Usuário: <b>{Environment.UserName}</b> | SO: <b>{Environment.OSVersion}</b></p>");
            html.AppendLine($"<p>Duração do scan: <b>{scanDurationMs}ms</b> | Dispositivos: <b>{detectedDevices?.Count ?? 0}</b> | Updates: <b style='color:#3fb950'>{foundUpdates?.Count ?? 0}</b></p>");

            // Tabela de dispositivos
            html.AppendLine("<h2>📋 Todos os Dispositivos Detectados</h2><table>");
            html.AppendLine("<tr><th>Vendor</th><th>Dispositivo</th><th>Versão Instalada</th><th>Data</th><th>Hardware ID (1º)</th><th>INF</th><th>Status</th></tr>");
            foreach (var dev in detectedDevices ?? new List<DeviceInfo>())
            {
                string badgeClass = (dev.Vendor ?? "").ToLower() switch {
                    "intel"   => "b-intel",
                    "nvidia"  => "b-nvidia",
                    "amd"     => "b-amd",
                    "realtek" => "b-realtek",
                    _         => "b-gen"
                };
                string status = dev.IsProblem ? "<span class='err'>❌ Erro</span>" : dev.IsRunning ? "<span class='ok'>✅ OK</span>" : "<span class='warn'>⚠️ Inativo</span>";
                string firstHwId = dev.HardwareIds?.Split(';').FirstOrDefault() ?? "—";
                html.AppendLine($"<tr>");
                html.AppendLine($"<td><span class='badge {badgeClass}'>{dev.Vendor ?? "?"}</span></td>");
                html.AppendLine($"<td>{HtmlEncode(dev.DeviceName)}<br><small style='color:#8b949e'>{HtmlEncode(dev.DeviceInstanceId)}</small></td>");
                html.AppendLine($"<td>{HtmlEncode(dev.DriverVersion ?? "—")}</td>");
                html.AppendLine($"<td>{dev.DriverDate ?? "—"}</td>");
                html.AppendLine($"<td style='font-size:11px'>{HtmlEncode(firstHwId)}</td>");
                html.AppendLine($"<td style='font-size:11px'>{HtmlEncode(dev.DriverInfPath ?? "—")}</td>");
                html.AppendLine($"<td>{status}</td>");
                html.AppendLine($"</tr>");
            }
            html.AppendLine("</table>");

            // Tabela de updates
            html.AppendLine("<h2>🔼 Atualizações Encontradas</h2>");
            if (foundUpdates == null || foundUpdates.Count == 0)
            {
                html.AppendLine("<p class='ok'>✅ Nenhuma atualização pendente. Todos os drivers estão atualizados.</p>");
            }
            else
            {
                html.AppendLine("<table>");
                html.AppendLine("<tr><th>Dispositivo</th><th>Versão Instalada</th><th>Versão Disponível</th><th>Data</th><th>Motivo</th><th>URL da Fonte</th><th>SHA-256</th></tr>");
                foreach (var upd in foundUpdates)
                {
                    string sourceUrl = upd.NewDriver?.SourceUrl ?? upd.NewDriver?.DownloadUrl ?? "—";
                    string sourceLink = sourceUrl != "—" ? $"<a href='{sourceUrl}' style='color:#58a6ff' target='_blank'>{sourceUrl.Split('/').LastOrDefault()}</a>" : "—";
                    html.AppendLine("<tr>");
                    html.AppendLine($"<td><b>{HtmlEncode(upd.Device?.DeviceName)}</b><br><small style='color:#8b949e'>{HtmlEncode(upd.Device?.HardwareIds?.Split(';').FirstOrDefault())}</small></td>");
                    html.AppendLine($"<td class='warn'>{HtmlEncode(upd.CurrentVersion)}</td>");
                    html.AppendLine($"<td class='ok'>{HtmlEncode(upd.NewDriver?.Version)}</td>");
                    html.AppendLine($"<td>{upd.NewDriver?.ReleaseDate:yyyy-MM-dd}</td>");
                    html.AppendLine($"<td>{upd.UpdateReason}</td>");
                    html.AppendLine($"<td style='font-size:11px'>{sourceLink}</td>");
                    html.AppendLine($"<td style='font-size:11px'>{HtmlEncode(upd.NewDriver?.Sha256 ?? "—")}</td>");
                    html.AppendLine("</tr>");
                }
                html.AppendLine("</table>");
            }

            html.AppendLine("<br><hr style='border-color:#30363d'><p style='color:#8b949e;font-size:12px'>Voltris Optimizer — Driver Diagnostics Engine v4.0</p>");
            html.AppendLine("</body></html>");
            File.WriteAllText(htmlPath, html.ToString(), Encoding.UTF8);

            App.LoggingService?.LogInfo($"[DriverDiagnostics] 📊 Relatório de auditoria exportado:");
            App.LoggingService?.LogInfo($"[DriverDiagnostics]   HTML: {htmlPath}");
            App.LoggingService?.LogInfo($"[DriverDiagnostics]   TXT : {txtPath}");

            return htmlPath; // Retorna o caminho do HTML para o UI poder abrir
        }

        /// <summary>
        /// Abre o relatório mais recente no navegador padrão para análise imediata.
        /// </summary>
        public static void OpenLatestReport()
        {
            if (!Directory.Exists(ReportDirectory)) return;

            var latest = Directory.GetFiles(ReportDirectory, "*.html")
                                  .OrderByDescending(f => f)
                                  .FirstOrDefault();

            if (latest != null)
            {
                Process.Start(new ProcessStartInfo { FileName = latest, UseShellExecute = true });
            }
        }

        /// <summary>
        /// Registra no log uma comparação detalhada (GROUND TRUTH) entre o driver instalado
        /// e a versão oficial da fonte — técnica de "Source Validation" usada em auditorias.
        /// </summary>
        public static void LogUpdateDecision(string deviceName, string installedVersion, string onlineVersion,
                                             string onlineSource, bool isUpdateDecided, string reason)
        {
            string arrow = isUpdateDecided ? "🔼 UPDATE" : "✅ OK    ";
            App.LoggingService?.LogInfo($"[DriverDecision] {arrow} | {deviceName,-40} | Instalado: {installedVersion,-18} | Online: {onlineVersion,-18} | Fonte: {onlineSource} | Motivo: {reason}");
        }

        private static string GetCpuInfo()
        {
            try {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("ProcessorNameString") as string ?? "N/A";
            } catch { return "N/A"; }
        }

        private static string HtmlEncode(string? s) =>
            (s ?? "—").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
