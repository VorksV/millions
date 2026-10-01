using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    public class QuestionaireManager
    {
        private readonly ProfileStore _store;
        public QuestionaireManager(ProfileStore store)
        {
            _store = store;
            App.LoggingService?.LogTrace("[PROFILER] Gerenciador de questionário inicializado");
        }

        public bool IsCompleted()
        {
            var s = _store.Load();
            return s.QuestionnaireCompleted;
        }

        public Task SaveAnswersAsync(UserAnswers answers)
        {
            App.LoggingService?.LogInfo("[PROFILER] Salvando respostas do usuário no questionário...");
            var s = _store.Load();
            s.Answers = answers;
            s.QuestionnaireCompleted = true;
            _store.Save(s);
            App.LoggingService?.LogSuccess("[PROFILER] Respostas salvas e questionário marcado como concluído");
            return Task.CompletedTask;
        }
    }
}

