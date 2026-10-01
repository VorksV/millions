using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.StreamHub.Interfaces;
using VoltrisOptimizer.Services.StreamHub.Models;

namespace VoltrisOptimizer.Services.StreamHub.Implementation
{
    /// <summary>
    /// Assistente de engajamento inteligente — analisa comportamento do chat
    /// e sugere ações ao streamer em tempo real. Funciona 100% offline.
    /// Esta é a funcionalidade DIFERENCIAL: o "Copiloto do Streamer".
    /// </summary>
    public class EngagementAssistantService : IEngagementAssistantService
    {
        private readonly ILoggingService _logger;
        private readonly List<EngagementSuggestion> _suggestions = new();
        private readonly Queue<DateTime> _recentMessages = new();
        private readonly Dictionary<string, int> _userMessageCount = new();
        private DateTime _lastSuggestionTime = DateTime.MinValue;
        private DateTime _lastMessageTime = DateTime.Now;
        private int _previousViewerCount;
        private const int SuggestionCooldownSeconds = 45;

        // Banco de sugestões contextuais
        private static readonly string[] QuestionSuggestions =
        {
            "Faça uma pergunta para o chat! Ex: 'Qual é o jogo favorito de vocês?'",
            "Interaja com o chat: 'De onde vocês estão assistindo?'",
            "Engaje a audiência: 'O que vocês acham dessa situação?'",
            "Crie uma enquete mental: 'Time A ou Time B? Digitem no chat!'",
            "Peça opinião: 'Qual estratégia vocês usariam aqui?'"
        };

        private static readonly string[] IdleSuggestions =
        {
            "O chat está quieto. Tente fazer uma pergunta ou contar uma história!",
            "Momento de engajar! Compartilhe algo interessante com a audiência.",
            "Chat parado há um tempo. Que tal um desafio ou enquete rápida?",
            "Boa hora para agradecer os viewers e pedir para compartilhar a live!",
            "Interaja com os espectadores — pergunte o que eles querem ver!"
        };

        private static readonly string[] ViewerDropSuggestions =
        {
            "Viewers caindo. Tente mudar o ritmo ou fazer algo diferente!",
            "Hora de energizar a live! Mude de atividade ou interaja mais.",
            "Queda de audiência detectada. Considere anunciar o próximo conteúdo."};

        public event EventHandler<EngagementSuggestion>? SuggestionGenerated;

        public IReadOnlyList<EngagementSuggestion> ActiveSuggestions => _suggestions.AsReadOnly();

        public EngagementAssistantService(ILoggingService logger)
        {
            _logger = logger;
            _logger.Log(LogLevel.Debug, LogCategory.General, "[EngagementAssistant] Instanciado", source: "StreamHub");
        }

        public void OnChatMessage(ChatMessage message)
        {
            _lastMessageTime = DateTime.Now;
            _recentMessages.Enqueue(DateTime.Now);

            // Rastrear usuários frequentes
            var key = $"{message.Platform}:{message.Username}";
            _userMessageCount.TryGetValue(key, out int count);
            _userMessageCount[key] = count + 1;

            // Limpar fila de mensagens antigas (janela de 60s)
            var cutoff = DateTime.Now.AddSeconds(-60);
            while (_recentMessages.Count > 0 && _recentMessages.Peek() < cutoff)
                _recentMessages.Dequeue();

            // Detectar usuário frequente que merece resposta
            if (count + 1 >= 5 && count % 5 == 4) // A cada 5 mensagens do mesmo user
            {
                TrySuggest(new EngagementSuggestion
                {
                    Title = "Usuário Frequente",
                    Message = $"@{message.DisplayName} está muito ativo! Considere responder ou agradecer.",
                    Icon = "💬",
                    Priority = 3
                });
            }
        }

        public void OnChatIdle(TimeSpan idleDuration)
        {
            if (idleDuration.TotalSeconds < 60) return; // Só alertar após 1 minuto de silêncio

            var severity = idleDuration.TotalSeconds > 180 ? 5 : 3;
            var rng = new Random();
            var suggestion = IdleSuggestions[rng.Next(IdleSuggestions.Length)];

            _logger.Log(LogLevel.Info, LogCategory.General,
                $"[EngagementAssistant] Chat inativo por {idleDuration.TotalSeconds:F0}s. Gerando sugestão.",
                source: "StreamHub");

            TrySuggest(new EngagementSuggestion
            {
                Title = $"Chat Parado ({(int)idleDuration.TotalSeconds}s)",
                Message = suggestion,
                Icon = "😴",
                Priority = severity
            });
        }

        public void OnViewerCountChange(int previous, int current)
        {
            _previousViewerCount = previous;

            if (previous <= 0) return;

            double changePercent = (double)(current - previous) / previous * 100;

            if (changePercent <= -20 && current > 0) // Queda de 20%+
            {
                _logger.Log(LogLevel.Warning, LogCategory.General,
                    $"[EngagementAssistant] Queda de viewers: {previous} → {current} ({changePercent:F0}%)",
                    source: "StreamHub");

                var rng = new Random();
                TrySuggest(new EngagementSuggestion
                {
                    Title = $"Queda de Viewers ({changePercent:F0}%)",
                    Message = ViewerDropSuggestions[rng.Next(ViewerDropSuggestions.Length)],
                    Icon = "📉",
                    Priority = 4
                });
            }
            else if (changePercent >= 30) // Crescimento de 30%+
            {
                _logger.Log(LogLevel.Info, LogCategory.General,
                    $"[EngagementAssistant] Crescimento de viewers: {previous} → {current} (+{changePercent:F0}%)",
                    source: "StreamHub");

                TrySuggest(new EngagementSuggestion
                {
                    Title = "Pico de Audiência!",
                    Message = $"Viewers crescendo! Aproveite para se apresentar e pedir para seguirem o canal.",
                    Icon = "📈",
                    Priority = 5
                });
            }
        }

        public void DismissSuggestion(string id)
        {
            var suggestion = _suggestions.FirstOrDefault(s => s.Id == id);
            if (suggestion != null)
            {
                suggestion.IsActioned = true;
                _suggestions.Remove(suggestion);
                _logger.Log(LogLevel.Debug, LogCategory.General,
                    $"[EngagementAssistant] Sugestão dispensada: {id}", source: "StreamHub");
            }
        }

        public void Reset()
        {
            _suggestions.Clear();
            _recentMessages.Clear();
            _userMessageCount.Clear();
            _lastSuggestionTime = DateTime.MinValue;
            _logger.Log(LogLevel.Info, LogCategory.General, "[EngagementAssistant] Reset completo", source: "StreamHub");
        }

        /// <summary>
        /// Gera sugestão periódica baseada no tempo sem interação.
        /// Deve ser chamado periodicamente pelo orquestrador.
        /// </summary>
        public void Tick()
        {
            var idleTime = DateTime.Now - _lastMessageTime;
            if (idleTime.TotalSeconds >= 90)
            {
                OnChatIdle(idleTime);
            }
        }

        private void TrySuggest(EngagementSuggestion suggestion)
        {
            // Cooldown entre sugestões
            if ((DateTime.Now - _lastSuggestionTime).TotalSeconds < SuggestionCooldownSeconds)
                return;

            // Não duplicar sugestões do mesmo tipo
            if (_suggestions.Any(s => s.Title == suggestion.Title))
                return;

            // Manter máximo de 5 sugestões ativas
            if (_suggestions.Count >= 5)
                _suggestions.RemoveAt(0);

            _suggestions.Add(suggestion);
            _lastSuggestionTime = DateTime.Now;

            _logger.Log(LogLevel.Info, LogCategory.General,
                $"[EngagementAssistant] Sugestão gerada [{suggestion.Priority}/5]: {suggestion.Title} - {suggestion.Message}",
                source: "StreamHub");

            SuggestionGenerated?.Invoke(this, suggestion);
        }
    }
}
