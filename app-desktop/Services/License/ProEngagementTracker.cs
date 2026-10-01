using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Rastreia cliques em botões PRO vs conversão (licença paga ativada).
    /// Grava um log JSON local (modo line-delimited) para análise de funil:
    /// quantos usuários grátis clicam em recursos PRO e quantos convertem para pago.
    /// </summary>
    public static class ProEngagementTracker
    {
        private static readonly object _lock = new();
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoltrisOptimizer", "pro_engagement.jsonl");
        private static bool _conversionRecorded;

        /// <summary>Registra um clique em um recurso PRO (por usuário grátis/bloqueado).</summary>
        public static void RecordClick(string feature)
        {
            Append(new Dictionary<string, object>
            {
                ["type"] = "click",
                ["feature"] = feature,
                ["ts"] = DateTime.UtcNow.ToString("O"),
                ["build"] = System.Reflection.Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? "unknown"
            });
        }

        /// <summary>Registra uma conversão (licença paga detectada ativa) — uma vez por sessão.</summary>
        public static void RecordConversion()
        {
            lock (_lock)
            {
                if (_conversionRecorded) return;
                _conversionRecorded = true;
            }
            Append(new Dictionary<string, object>
            {
                ["type"] = "conversion",
                ["ts"] = DateTime.UtcNow.ToString("O"),
                ["build"] = System.Reflection.Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? "unknown"
            });
        }

        private static void Append(object entry)
        {
            try
            {
                var line = JsonSerializer.Serialize(entry, new System.Text.Json.JsonSerializerOptions
                {
                    // CORREÇÃO FORENSE #4: Previne JsonException por ciclo de referência
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(FilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.AppendAllText(FilePath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Analytics nunca deve quebrar o app.
            }
        }
    }
}
