using System;
using System.IO;
using System.Text.Json;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    public class ProfileStore
    {
        private readonly string _dir;
        private readonly string _stateFile;

        public ProfileStore()
        {
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _dir = Path.Combine(appdata, "Voltris", "Profiler");
            _stateFile = Path.Combine(_dir, "state.json");
            if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);
        }

        public ProfilerState Load()
        {
            try
            {
                if (File.Exists(_stateFile))
                {
                    var json = File.ReadAllText(_stateFile);
                    var state = JsonSerializer.Deserialize<ProfilerState>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    return state ?? new ProfilerState();
                }
            }
            catch (Exception ex) 
            {
                try { App.LoggingService?.LogError($"[ProfileStore] Erro ao carregar {_stateFile}: {ex.Message}", ex); } catch { }
            }
            return new ProfilerState();
        }

        public void Save(ProfilerState state)
        {
            try
            {
                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                File.WriteAllText(_stateFile, json);
                try { App.LoggingService?.LogInfo($"[ProfileStore] Estado salvo com sucesso em: {_stateFile}"); } catch { }
            }
            catch (Exception ex)
            {
                try { App.LoggingService?.LogError($"[ProfileStore] Erro ao salvar estado em {_stateFile}: {ex.Message}", ex); } catch { }
            }
        }
    }

    public class ProfilerState
    {
        public bool QuestionnaireCompleted { get; set; }
        public bool TutorialCompleted { get; set; }
        public UserAnswers Answers { get; set; } = new UserAnswers();

        /// <summary>
        /// True quando o pacote de otimizações que morava na página
        /// "Relatório DNA" já foi aplicado. É o que faz o botão circular
        /// aplicar o pacote apenas uma única vez.
        /// </summary>
        public bool DnaOptimizationsApplied { get; set; }

        public DateTime DnaOptimizationsAppliedUtc { get; set; }

        public int DnaOptimizationsAppliedCount { get; set; }

        /// <summary>Score real medido na aplicação (0-100).</summary>
        public double DnaRealScore { get; set; }
    }
}

