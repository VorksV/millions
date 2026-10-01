using System;
using System.Text;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Delivery
{
    public class VpisNotificationHandler
    {
        private readonly bool _customToastEnabled;

        public VpisNotificationHandler(bool customToastEnabled)
        {
            _customToastEnabled = customToastEnabled;
        }

        public void DeliverReport(PerformanceHealthReport report)
        {
            if (report.OverallQualityScore == 10 && report.MainProblemTitle == "Nenhum gargalo detectado")
                return;

            if (_customToastEnabled)
                ShowCustomToast(report);
            else
                ShowCustomToast(report);
        }

        private static void ShowCustomToast(PerformanceHealthReport report)
        {
            try
            {
                var loc = LocalizationService.Instance;

                string title = loc.GetString("VpisDiagnosisTitle");

                var sb = new StringBuilder();

                string sessionLabel = loc.GetString("VpisSessionLabel") ?? "Sessão";
                sb.AppendLine($"📊 {sessionLabel}: {report.SessionName}");
                sb.AppendLine();

                string problemLabel = loc.GetString("VpisMainProblem") ?? "Problema principal";
                sb.AppendLine($"{problemLabel}: {report.MainProblemTitle}");

                string impactLabel = loc.GetString("VpisEstimatedImpact") ?? "Impacto estimado";
                sb.AppendLine($"{impactLabel}: {report.EstimatedImpact}");

                string confidenceLabel = loc.GetString("VpisConfidence") ?? "Confiança";
                sb.AppendLine($"{confidenceLabel}: {report.MainProblemConfidence}%");
                sb.AppendLine();

                if (report.KeyEvidences != null && report.KeyEvidences.Count > 0)
                {
                    string evidencesLabel = loc.GetString("VpisEvidences") ?? "Evidências";
                    sb.AppendLine($"📈 {evidencesLabel}:");
                    foreach (var ev in report.KeyEvidences)
                        sb.AppendLine($"  • {ev}");
                    sb.AppendLine();
                }

                if (report.Recommendations != null && report.Recommendations.Count > 0)
                {
                    string recsLabel = loc.GetString("VpisRecommendations") ?? "Recomendações";
                    sb.AppendLine($"🔧 {recsLabel}:");
                    int i = 1;
                    foreach (var rec in report.Recommendations)
                    {
                        sb.AppendLine($"  {i}. {rec}");
                        i++;
                    }
                }
                else
                {
                    string genericRec = loc.GetString("VpisGenericRecommendation") ?? "Nenhuma recomendação adicional.";
                    sb.AppendLine($"ℹ️ {genericRec}");
                }

                string message = sb.ToString().TrimEnd();

                if (report.OverallQualityScore < 5)
                    GlobalNotificationService.ShowError(title, message);
                else if (report.OverallQualityScore < 8)
                    GlobalNotificationService.ShowWarning(title, message);
                else
                    GlobalNotificationService.ShowInfo(title, message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VPIS-DELIVERY] [ERROR] {ex.Message}");
            }
        }
    }
}
