using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço para integração com a API do Google Gemini
    /// </summary>
    public class GeminiService
    {
        private readonly ILoggingService _logger;
        private readonly HttpClient _httpClient;
        private readonly SettingsService _settingsService;
        // Lista de modelos para tentar em ordem de preferência
        // Baseado no formato usado no site Voltris
        private readonly string[] _modelUrls = new[]
        {
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash-latest:generateContent",
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-pro-latest:generateContent",
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent",
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-pro:generateContent",
            "https://generativelanguage.googleapis.com/v1/models/gemini-1.5-pro:generateContent",
            "https://generativelanguage.googleapis.com/v1/models/gemini-pro:generateContent"
        };

        public GeminiService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsService = SettingsService.Instance;
            
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(90);
        }

        /// <summary>
        /// Verifica se a API Key do Gemini está configurada
        /// </summary>
        public bool IsApiKeyConfigured()
        {
            return !string.IsNullOrWhiteSpace(_settingsService.Settings.GeminiApiKey);
        }

        /// <summary>
        /// Processa uma consulta do usuário usando o Google Gemini
        /// </summary>
        /// <param name="userQuery">Pergunta ou problema do usuário</param>
        /// <returns>Resposta formatada da Lyra</returns>
        public async Task<LyraResponse> ProcessQueryAsync(string userQuery)
        {
            try
            {
                _logger.LogInfo("[GEMINI] ===== INÍCIO DA CONSULTA =====");
                _logger.LogInfo($"[GEMINI] Query recebida: '{userQuery}'");

                // Verificar se a API Key está configurada
                if (!IsApiKeyConfigured())
                {
                    _logger.LogWarning("[GEMINI] API Key não configurada");
                    return new LyraResponse
                    {
                        Response = "⚠️ API Key do Google Gemini não configurada.\n\nPor favor, configure sua chave de API nas Configurações do aplicativo.",
                        Actions = new List<string> { "Configurar API Key nas Configurações" },
                        Confidence = 0.0
                    };
                }

                var apiKey = _settingsService.Settings.GeminiApiKey;
                _logger.LogInfo($"[GEMINI] API Key encontrada (Length: {apiKey?.Length ?? 0})");

                // Construir o prompt do sistema
                var systemPrompt = @"Você é a Lyra, uma assistente de IA especialista em otimização de Windows e resolução de problemas técnicos. 
Sua missão é ajudar usuários a otimizar seus computadores Windows de forma profissional e eficiente.

INSTRUÇÕES:
- Responda de forma clara, concisa e profissional
- Seja direto e objetivo
- Sugira ações específicas que o usuário pode executar no aplicativo Voltris Optimizer
- Use uma linguagem amigável mas técnica quando necessário
- Se o problema não for relacionado a Windows/otimização, oriente o usuário adequadamente

FORMATO DA RESPOSTA:
1. Uma resposta direta ao problema/pergunta do usuário
2. Sugestões de ações específicas (se aplicável)
3. Dicas adicionais (opcional)

IMPORTANTE: Mantenha a resposta em português brasileiro e seja profissional.";

                // Combinar prompt do sistema com a pergunta do usuário
                var fullPrompt = $"{systemPrompt}\n\nPERGUNTA DO USUÁRIO: {userQuery}\n\nRESPOSTA DA LYRA:";

                _logger.LogInfo("[GEMINI] Enviando requisição para a API...");

                // Preparar o corpo da requisição (usando camelCase como a API espera)
                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = fullPrompt }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.7,
                        topK = 40,
                        topP = 0.95,
                        maxOutputTokens = 1024
                    }
                };

                // Serializar com camelCase (como a API do Gemini espera)
                var jsonOptions = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                };
                
                var jsonContent = JsonSerializer.Serialize(requestBody, jsonOptions);
                _logger.LogInfo($"[GEMINI] JSON Request (Length: {jsonContent.Length}): {jsonContent.Substring(0, Math.Min(500, jsonContent.Length))}...");
                
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Tentar diferentes modelos até encontrar um que funcione
                HttpResponseMessage? response = null;
                string? lastError = null;
                string? workingModel = null;

                foreach (var modelUrl in _modelUrls)
                {
                    try
                    {
                        var urlPreview = $"{modelUrl}?key={apiKey?.Substring(0, Math.Min(10, apiKey?.Length ?? 0))}...";
                        _logger.LogInfo($"[GEMINI] Tentando modelo: {modelUrl}");
                        _logger.LogInfo($"[GEMINI] URL completa (key truncada): {urlPreview}");
                        
                        var fullUrl = $"{modelUrl}?key={apiKey}";
                        response = await _httpClient.PostAsync(fullUrl, content);
                        _logger.LogInfo($"[GEMINI] Resposta recebida. Status: {response.StatusCode}");

                        if (response.IsSuccessStatusCode)
                        {
                            workingModel = modelUrl;
                            _logger.LogSuccess($"[GEMINI] Modelo funcionando: {modelUrl}");
                            break;
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            lastError = errorContent;
                            _logger.LogWarning($"[GEMINI] Modelo {modelUrl} falhou: {response.StatusCode} - {errorContent}");
                            response.Dispose();
                            response = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[GEMINI] Erro ao tentar modelo {modelUrl}: {ex.Message}");
                        response?.Dispose();
                        response = null;
                    }
                }

                if (response == null || !response.IsSuccessStatusCode)
                {
                    var errorContent = lastError ?? "Nenhum modelo disponível funcionou.";
                    _logger.LogError($"[GEMINI] Todos os modelos falharam. Último erro: {errorContent}");
                    
                    // Tentar extrair mensagem de erro mais específica
                    string errorMessage = "Erro ao conectar com a API do Google Gemini.";
                    try
                    {
                        if (!string.IsNullOrEmpty(errorContent))
                        {
                            using var errorDoc = JsonDocument.Parse(errorContent);
                            if (errorDoc.RootElement.TryGetProperty("error", out var errorObj))
                            {
                                if (errorObj.TryGetProperty("message", out var message))
                                {
                                    errorMessage = message.GetString() ?? errorMessage;
                                }
                            }
                        }
                    }
                    catch { }
                    
                    string detailedMessage = $"❌ {errorMessage}\n\n";
                    
                    if (errorContent.Contains("API key") || errorContent.Contains("PERMISSION_DENIED"))
                    {
                        detailedMessage += "🔑 A API Key está incorreta ou inválida.\n\n";
                        detailedMessage += "Como obter sua chave de API:\n";
                        detailedMessage += "1. Acesse: https://aistudio.google.com/app/apikey\n";
                        detailedMessage += "2. Faça login com sua conta Google\n";
                        detailedMessage += "3. Clique em 'Criar chave' ou 'Get API Key'\n";
                        detailedMessage += "4. Copie a chave gerada (começa com 'AIza...')\n";
                        detailedMessage += "5. Cole nas Configurações do aplicativo\n\n";
                        detailedMessage += "⚠️ Certifique-se de copiar a chave completa sem espaços";
                    }
                    else if (errorContent.Contains("not found") || errorContent.Contains("not supported"))
                    {
                        detailedMessage += "⚠️ Modelo não disponível. Isso pode ser um problema temporário da API.\n\n";
                        detailedMessage += "Por favor, tente novamente em alguns instantes ou verifique se sua API Key está ativa.";
                    }
                    else
                    {
                        detailedMessage += "Por favor, verifique:\n";
                        detailedMessage += "• Sua conexão com a internet\n";
                        detailedMessage += "• Se sua API Key está correta nas Configurações\n";
                        detailedMessage += "• Se a API Key está ativa no Google AI Studio";
                    }
                    
                    return new LyraResponse
                    {
                        Response = detailedMessage,
                        Actions = new List<string> { "Abrir Google AI Studio", "Verificar API Key nas Configurações", "Verificar conexão com internet" },
                        Confidence = 0.0
                    };
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInfo($"[GEMINI] Resposta JSON recebida (Length: {responseContent.Length})");

                // Parsear a resposta JSON
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                {
                    _logger.LogWarning("[GEMINI] Nenhum candidato na resposta");
                    return new LyraResponse
                    {
                        Response = "Desculpe, não consegui processar sua consulta. Por favor, tente novamente.",
                        Actions = new List<string>(),
                        Confidence = 0.0
                    };
                }

                var firstCandidate = candidates[0];
                if (!firstCandidate.TryGetProperty("content", out var contentElement) ||
                    !contentElement.TryGetProperty("parts", out var parts) ||
                    parts.GetArrayLength() == 0)
                {
                    _logger.LogWarning("[GEMINI] Estrutura de resposta inválida");
                    return new LyraResponse
                    {
                        Response = "Desculpe, a resposta da IA não está no formato esperado. Por favor, tente novamente.",
                        Actions = new List<string>(),
                        Confidence = 0.0
                    };
                }

                var textPart = parts[0];
                if (!textPart.TryGetProperty("text", out var textElement))
                {
                    _logger.LogWarning("[GEMINI] Texto não encontrado na resposta");
                    return new LyraResponse
                    {
                        Response = "Desculpe, não consegui extrair a resposta. Por favor, tente novamente.",
                        Actions = new List<string>(),
                        Confidence = 0.0
                    };
                }

                var generatedText = textElement.GetString() ?? "";
                _logger.LogInfo($"[GEMINI] Texto gerado (Length: {generatedText.Length})");

                // Processar a resposta e extrair ações sugeridas
                var processedResponse = ProcessResponse(generatedText);

                _logger.LogSuccess("[GEMINI] ===== CONSULTA FINALIZADA COM SUCESSO =====");
                return processedResponse;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError("[GEMINI] Timeout na requisição", ex);
                return new LyraResponse
                {
                    Response = "⏱️ A requisição demorou muito para responder.\n\nPor favor, verifique sua conexão com a internet e tente novamente.",
                    Actions = new List<string> { "Verificar conexão com internet" },
                    Confidence = 0.0
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("[GEMINI] Erro ao processar consulta", ex);
                return new LyraResponse
                {
                    Response = $"❌ Erro ao processar sua consulta: {ex.Message}\n\nPor favor, tente novamente ou verifique suas configurações.",
                    Actions = new List<string> { "Verificar Configurações" },
                    Confidence = 0.0
                };
            }
        }

        /// <summary>
        /// Processa a resposta do Gemini e extrai ações sugeridas
        /// </summary>
        private LyraResponse ProcessResponse(string rawResponse)
        {
            var response = new LyraResponse
            {
                Response = rawResponse.Trim(),
                Actions = new List<string>(),
                Confidence = 0.85 // Alta confiança para respostas do Gemini
            };

            // Tentar extrair ações sugeridas do texto
            // Procurar por padrões como "•", "-", "1.", etc.
            var lines = rawResponse.Split('\n');
            var actionsFound = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Procurar por linhas que parecem ações
                if (trimmedLine.StartsWith("•") || 
                    trimmedLine.StartsWith("-") || 
                    trimmedLine.StartsWith("*") ||
                    (trimmedLine.Length > 2 && char.IsDigit(trimmedLine[0]) && trimmedLine[1] == '.'))
                {
                    var action = trimmedLine.TrimStart('•', '-', '*', ' ', '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '.');
                    if (!string.IsNullOrWhiteSpace(action) && action.Length > 5)
                    {
                        response.Actions.Add(action);
                        actionsFound = true;
                    }
                }
            }

            // Se não encontrou ações explícitas, tentar sugerir baseado no contexto
            if (!actionsFound)
            {
                var lowerResponse = rawResponse.ToLower();
                if (lowerResponse.Contains("limpeza") || lowerResponse.Contains("limpar"))
                {
                    response.Actions.Add("Executar Limpeza Completa");
                }
                if (lowerResponse.Contains("desempenho") || lowerResponse.Contains("otimizar") || lowerResponse.Contains("lento"))
                {
                    response.Actions.Add("Otimizar Desempenho");
                }
                if (lowerResponse.Contains("rede") || lowerResponse.Contains("internet"))
                {
                    response.Actions.Add("Otimizar Rede");
                }
                if (lowerResponse.Contains("sistema") || lowerResponse.Contains("reparar"))
                {
                    response.Actions.Add("Reparar Sistema");
                }
            }

            return response;
        }
    }

    /// <summary>
    /// Resposta formatada da Lyra
    /// </summary>
    public class LyraResponse
    {
        public string Response { get; set; } = "";
        public List<string> Actions { get; set; } = new List<string>();
        public double Confidence { get; set; } = 0.0;
    }
}

