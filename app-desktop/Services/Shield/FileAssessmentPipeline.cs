using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>
    /// Resultado da analise de CONTEUDO de um arquivo — a parte que depende
    /// apenas dos BYTES, nunca do caminho nem do nome.
    ///
    /// SEPARACAO CONTEUDO/CONEXTO. Isto e o que torna correto cachear por hash.
    /// "fatura.pdf.exe" e "cpu-z_2.11.exe" podem ter bytes identicos e NOTAOS
    /// radicalmente diferentes: o primeiro usa extensao dupla para enganar, o
    /// segundo e um nome com versao. Um veredito final carrega as duas coisas
    /// misturadas, e cachea-lo por hash faz o arquivo innocentado herdar a
    /// acusacao do outro (ou vice-versa).
    ///
    /// Entao o cache guarda SO o que os bytes provam: assinatura, editor, publisher,
    /// entropia, imports, empacotamento, indicadores de framework de offense, e a
    /// presenca de stream oculto. Nome, extensao e local sao reavaliados a cada
    /// ocorrencia, porque sao baratos e sao funcao do caminho.
    /// </summary>
    public sealed class FileContentAnalysis
    {
        public string Hash { get; init; } = string.Empty;
        public SignatureResult? Signature { get; init; }
        public double Entropy { get; init; }
        public PeAnalysisResult? Pe { get; init; }
        public bool IsPortableExecutable { get; init; }
        public bool IsPeLibrary { get; init; }
        public string? AlternateDataStream { get; init; }
        public long Length { get; init; }

        public bool IsTrustedSigned => Signature is { IsValid: true, IsTrustedPublisher: true };
    }

    /// <summary>
    /// Cache de analise de conteudo por hash.
    ///
    /// O que resolve: (1) a mesma DLL copiada para dez diretorios custa UMA
    /// analise profunda, nao dez; (2) arquivos legitimos repetidos em %TEMP%
    /// durante um scan nao multiplicam WinVerifyTrust + entropia + leitura de PE.
    ///
    /// O que NAO resolve, e nao deve: decisao de notificar. Isso e do
    /// AlertThrottleService, e depende de hash, severidade e tempo — nao do
    /// conteudo.
    /// </summary>
    public sealed class ThreatReputationCache
    {
        private sealed record Entry(FileContentAnalysis Analysis, DateTime ExpiresUtc, int HitCount);

        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly ILoggingService _logger;
        private readonly TimeSpan _ttl;

        private const int MaxEntries = 20_000;
        private long _hits;
        private long _misses;

        public ThreatReputationCache(ILoggingService logger, TimeSpan? ttl = null)
        {
            _logger = logger;
            _ttl = ttl ?? TimeSpan.FromHours(6);
        }

        public long Hits => Interlocked.Read(ref _hits);
        public long Misses => Interlocked.Read(ref _misses);
        public int Count => _entries.Count;

        public bool TryGet(string hash, out FileContentAnalysis analysis)
        {
            analysis = null!;
            if (string.IsNullOrEmpty(hash)) return false;

            if (!_entries.TryGetValue(hash, out var entry)) return false;

            if (DateTime.UtcNow > entry.ExpiresUtc)
            {
                _entries.TryRemove(hash, out _);
                return false;
            }

            _entries[hash] = entry with { HitCount = entry.HitCount + 1 };
            analysis = entry.Analysis;
            Interlocked.Increment(ref _hits);
            return true;
        }

        public void Store(FileContentAnalysis analysis)
        {
            if (string.IsNullOrEmpty(analysis?.Hash)) return;

            _entries[analysis.Hash] = new Entry(analysis, DateTime.UtcNow + _ttl, 0);
            Interlocked.Increment(ref _misses);

            if (_entries.Count > MaxEntries)
                Trim();
        }

        private void Trim()
        {
            var now = DateTime.UtcNow;
            var expired = _entries.Where(kvp => kvp.Value.ExpiresUtc < now).Select(kvp => kvp.Key).ToArray();
            foreach (var key in expired) _entries.TryRemove(key, out _);

            if (_entries.Count <= MaxEntries) return;

            // Descarta primeiro o que e trivial de recalcular: conteudo limpo e
            // pequeno. Conteudo suspeito tem prioridade de permanencia.
            var overflow = _entries.Count - MaxEntries;
            var removable = _entries
                .Where(kvp => kvp.Value.Analysis.IsTrustedSigned)
                .OrderBy(kvp => kvp.Value.HitCount)
                .Take(overflow)
                .Select(kvp => kvp.Key)
                .ToArray();

            foreach (var key in removable) _entries.TryRemove(key, out _);
        }

        public void Clear() => _entries.Clear();
    }

    /// <summary>
    /// Modo de operacao das deteccoes — o "shadow mode" da engenharia de deteccao.
    ///
    /// KILL SWITCH: regras novas entram em Shadow antes de poder notificar. Elas
    /// registram no log e aparecem na telemetria, mas nao perturbam o usuario. E o
    /// unico jeito seguro de descobrir o custo de falso positivo de uma regra nova
    /// em maquinas reais — medir em campo, e nao no equipamento do autor.
    /// </summary>
    public sealed class ShieldDetectionMode
    {
        private readonly HashSet<string> _disabledRules = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _shadowRules = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILoggingService _logger;

        public ShieldDetectionMode(ILoggingService logger)
        {
            _logger = logger;
        }

        public bool IsEnabled(string ruleId) => !_disabledRules.Contains(ruleId);
        public bool IsShadowed(string ruleId) => _shadowRules.Contains(ruleId);

        public void Disable(string ruleId)
        {
            if (_disabledRules.Add(ruleId))
                _logger.LogWarning($"[ShieldDetectionMode] Regra DESABILITADA (kill switch): {ruleId}");
        }

        public void Enable(string ruleId)
        {
            if (_disabledRules.Remove(ruleId))
                _logger.LogInfo($"[ShieldDetectionMode] Regra habilitada: {ruleId}");
        }

        public void SetShadow(string ruleId, bool shadow)
        {
            if (shadow) _shadowRules.Add(ruleId);
            else _shadowRules.Remove(ruleId);

            _logger.LogInfo($"[ShieldDetectionMode] Regra {ruleId} shadow={shadow}");
        }

        public IReadOnlyCollection<string> DisabledRules => _disabledRules;
        public IReadOnlyCollection<string> ShadowRules => _shadowRules;
    }

    /// <summary>
    /// Pipeline de avaliacao de arquivo.
    ///
    /// ORDEM DAS CAMADAS (barato primeiro, e o barato pode DESCARTAR):
    ///
    ///   A. Triagem sem I/O      — classe do arquivo, streams ocultos, nome
    ///   B. Evidencia de caminho  — categoria de diretorio: absolve ou pontua
    ///   C. ASSINATURA            — publisher confiavel ABSOLVE. Este e o passo
    ///                              que a versao anterior nao tinha: ela so
    ///                              consultava assinatura para TENTAR acusar
    ///                              mais, nunca para descartar.
    ///   D. Reputacao por hash    — conteudo ja visto? reutiliza o veredito
    ///   E. Estatica profunda     — so sobreviventes: entropia, imports, packing
    ///   F. Veredito              — limiar, severidade, confianca, regra
    ///
    /// O ponto C e a correcao estrutural. Anteriormente a deep analysis era
    /// condicionada a `isExecutable && !isSuspicious` (FileMonitorService.cs:251),
    /// ou seja, a assinatura so podia SOMAR suspeita. Um binario assinado por
    /// publisher da allowlist recebia Medium do mesmo jeito que um dropper — e o
    /// log desta sessao mostra exatamente isso: 15 alertas para DLLs do Windows e
    /// para a DLL assinada do Windscribe, cuja editorial ja constava na lista de
    /// publishers confiaveis e nunca foi consultada.
    /// </summary>
    public sealed class FileAssessmentPipeline
    {
        private readonly ILoggingService _logger;
        private readonly SignatureVerificationService _signatureService;
        private readonly TrustedLocationPolicy _locationPolicy;
        private readonly ThreatReputationCache _reputation;
        private readonly ShieldDetectionMode _mode;
        private volatile bool _lowActivityMode;

        // Palavras-chave de malware, separadas por nivel de especificidade.
        //
        // CRITERIO DE INGENHARIA DE DETECCAO: um termo so entra na lista de alta
        // confianca se for criptograficamente especifico ao dominio do malware.
        // Palavra inglesa comum nao entra. Esta distincao e o que separa uma regra
        // medivel de uma fonte de falso positivo.
        //
        // A versao anterior usava fileName.Contains(keyword) sobre uma lista com
        // "hack", "cheat", "loader", "patch" — quatro palavras inglesasextremely
        // comuns. Daí "Shack.dll" acusar hack e o HWMonitor ser sinalizado.

        /// <summary>Terminos que so indicam malware no contexto de nome de arquivo.</summary>
        private static readonly (string Token, string Label)[] HighConfidenceKeywords =
        {
            ("keygen", "keygen"),
            ("activator", "ativador de licenca pirata"),
            ("cheatengine", "Cheat Engine"),
            ("metasploit", "Metasploit"),
            ("meterpreter", "Meterpreter"),
            ("reversec2", "reverse shell C2"),
            ("bindshell", "bind shell"),
            ("cryptolocker", "Cryptolocker"),
            ("wannacry", "WannaCry"),
            ("ryuk", "Ryuk ransomware"),
            ("lockbit", "LockBit"),
            ("conti", "Conti"),
            ("emotet", "Emotet"),
            ("trickbot", "TrickBot"),
            ("agenttesla", "Agent Tesla"),
            ("remcosrat", "Remcos RAT"),
            ("njrat", "njRAT"),
            ("darkcomet", "DarkComet"),
            ("netwire", "NetWire"),
            ("vidar", "Vidar stealer"),
            ("raccoon", "Raccoon stealer"),
            ("smokeloader", "SmokeLoader"),
            ("formbook", "FormBook"),
            ("snakekeylogger", "Snake Keylogger"),
            ("keylogger", "keylogger"),
            ("stealer", "stealer"),
            ("botnet", "botnet"),
            ("cryptominer", "criptominer"),
            ("xmrig", "XMRig"),
            ("coinhive", "CoinHive"),
            ("undetectable", "alega ser indetectavel"),
            ("antivirusbypass", "bypass de antivirus")
        };

        /// <summary>
        /// Terminos ambiguos: exigem verificacao de composto benigno antes de
        /// contar. "cheat" em "cheetah", "crack" em "crackle", "brute" em
        /// "bruteshaper". Notas de deteccao sao mantidas, mas a severidade e
        /// reduzida porque a taxa de falso positivo e estruturalmente maior.
        /// </summary>
        private static readonly (string Token, string Label, int Points)[] AmbiguousKeywords =
        {
            ("cheat", "cheat", 22),
            ("crack", "crack", 24),
            ("brute", "brute force", 15),
            ("bypass", "bypass", 18),
            ("patch", "patch", 12)
        };

        // Compostos benignos conhecidos. A presence do termo dentro de um destes
        // neutraliza a regra.
        private static readonly string[] BenignNameCompounds =
        {
            "crackle", "cracked", "crackshot", "dispatch", "dispatcher", "patchwork",
            "patcher", "backloader", "loader32", "downloader", "uploader",
            "screensaver", "spooler", "cheetah", "cheesy", "bruteshaper",
            "bruteforce_net", "keyloggerpro", "stealerinfo", "unpatched"
        };

        public FileAssessmentPipeline(
            ILoggingService logger,
            SignatureVerificationService signatureService,
            TrustedLocationPolicy locationPolicy,
            ThreatReputationCache reputation,
            ShieldDetectionMode mode)
        {
            _logger = logger;
            _signatureService = signatureService;
            _locationPolicy = locationPolicy;
            _reputation = reputation;
            _mode = mode;
        }

        /// <summary>
        /// Modo de baixa atividade (Modo Gamer).
        ///
        /// ANTES: `if (_lowActivityMode) return (false, ...)` logo apos a checagem
        /// de palavra-chave — ou seja, o Modo Gamer DESLIGAVA a deteccao inteira,
        /// incluindo nome com indicador de malware, streams ocultos e conteudo
        /// cifrado. Era um interruptor, nao um perfil de sensibilidade.
        ///
        /// AGORA: o pipeline continua operando, mas as camadas de baixa confiança
        /// (local, assinatura, metadados) param de CONTRIBUIR pontos. As camadas
        /// de alta confiança — palavra-chave de malware, ADS, extensao
        /// disfarçada, PE sem assinatura valida — seguem ativas. O usuario
        /// continua protegido; o que deixa de acontecer e o ruido.
        /// </summary>
        public void SetLowActivityMode(bool enabled)
        {
            _lowActivityMode = enabled;
            var effect = enabled ? "desativadas" : "reativadas";
            _logger.LogInfo($"[FilePipeline] Modo baixa atividade: {enabled} (regras de baixa confianca {effect})");
        }

        /// <summary>
        /// Avalia um arquivo e devolve o veredito. Nunca lanca: falha de avaliacao
        /// degrada para "nao avaliado", jamais para "suspeito".
        /// </summary>
        public ThreatVerdict Assess(string rawPath)
        {
            var path = PathNormalizer.Normalize(rawPath, _logger);

            // ===================================================================
            // FASE 1 — CONTEXTO (barato, funcao do CAMINHO, nunca cacheado)
            // ===================================================================
            var context = ApplyContextEvidence(path);

            var card = new ThreatScorecard();
            context.Rules.Apply(card);

            // ===================================================================
            // FASE 2 — ASSINATURA (prova de legitimidade, com cache por arquivo)
            // ===================================================================
            var shouldVerifySignature = context.RequiresSignatureCheck ||
                                        card.Score >= ShieldPolicy.DetectionThreshold;

            SignatureResult? signature = null;
            if (shouldVerifySignature)
            {
                signature = _signatureService.VerifySignature(path.Canonical);
                ApplySignatureEvidence(card, signature);
            }

            // --- Absolvicao por EVIDENCIA POSITIVA ---
            //
            // A saida antecipada acontece em UM caso so: existe prova positiva de
            // legitimidade, que e a assinatura valida de publisher confiavel. E
            // absolvicao por FATO, nao por ausencia de prova.
            //
            // O que nao pode encurtar o caminho e um score baixo. Score baixo
            // significa apenas "as evidencias FRACAS ate aqui nao bastaram" — e as
            // evidencias fracas sao as primeiras a ser coletadas. Sair com score
            // 23 depois de somar "%TEMP%" e "nao assinado" equivaleria a declarar
            // limpo um arquivo cujo indicador de Metasploit, entropia cifrada ou
            // import de injecao nunca chegaram a ser lidos.
            //
            // Esse e o mesmo erro estrutural do motor que este pipeline substitui,
            // so que invertido: ali a evidencia fraca CONDENAVA sem absolver; aqui
            // ela absolveria sem condenar. Raiz comum: tratar evidencia barata
            // como veredito, em vez de como contribuicao.
            if (signature is { IsValid: true, IsTrustedPublisher: true })
            {
                _logger.LogDebug($"[FilePipeline] Absolvido por assinatura: {path.FileName} " +
                                 $"(publisher {signature.Publisher}, score {card.Score})");
                return ThreatVerdict.FromScorecard(path.Canonical, string.Empty, card, signature);
            }

            // ===================================================================
            // FASE 3 — CONTEUDO (caro, funcao dos BYTES, cacheado por hash)
            // ===================================================================
            // Arquivos estruturalmente inertes nao tem o que revelar.
            if (!RequiresContentInspection(path))
                return ThreatVerdict.FromScorecard(path.Canonical, string.Empty, card, signature);

            var content = ResolveContentAnalysis(path, signature);

            ApplyContentEvidence(card, content, path);

            var verdict = ThreatVerdict.FromScorecard(path.Canonical, content.Hash, card, signature);

            if (IsShadowed(verdict))
            {
                _logger.LogInfo($"[FilePipeline] SHADOW: {verdict} (regra em shadow mode, nao notificada)");
            }

            return verdict;
        }

        /// <summary>
        /// Resolve a analise de conteudo, usando o cache por hash quando possivel.
        /// E o unico lugar que faz leitura pesada do arquivo.
        /// </summary>
        private FileContentAnalysis ResolveContentAnalysis(NormalizedPath path, SignatureResult? signature)
        {
            var hash = ComputeSha256(path.Canonical);

            if (!string.IsNullOrEmpty(hash) && _reputation.TryGet(hash, out var cached))
            {
                _logger.LogDebug($"[FilePipeline] Conteudo em cache: {path.FileName} " +
                                 $"(hash {hash[..Math.Min(12, hash.Length)]})");
                return cached;
            }

            var deep = _signatureService.AssessFileRisk(path.Canonical);

            var analysis = new FileContentAnalysis
            {
                Hash = hash,
                Signature = signature ?? deep.Signature,
                Entropy = deep.Entropy,
                Pe = deep.PeAnalysis,
                IsPortableExecutable = path.IsPortableExecutable,
                IsPeLibrary = path.IsPeLibrary,
                AlternateDataStream = path.AlternateDataStream,
                Length = path.Length
            };

            if (!string.IsNullOrEmpty(hash))
                _reputation.Store(analysis);

            return analysis;
        }

        /// <summary>
        /// Vale a pena abrir o arquivo? True quando existe chance real de o
        /// CONTEUDO provar algo — PE, script, ou stream oculto. Falso apenas para
        /// dados que nao podem conter codigo executavel.
        /// </summary>
        private static bool RequiresContentInspection(NormalizedPath path)
        {
            if (path.HasHiddenStream) return true;
            if (path.IsPortableExecutable) return true;
            if (PathNormalizer.IsExecutableExtension(path.EffectiveExtension)) return true;
            if (PathNormalizer.IsScriptExtension(path.EffectiveExtension)) return true;
            if (PathNormalizer.IsScriptExtension(path.Extension)) return true;
            return false;
        }

        private readonly struct TriageOutcome
        {
            public DetectionRuleSet Rules { get; init; }
            public bool IsTriageReject { get; init; }
            public string RejectReason { get; init; }
            public bool RequiresSignatureCheck { get; init; }
        }

        /// <summary>
        /// FASE 1: evidencia de CONTEXTO — funcao do nome, extensao e local.
        /// Barata (sem I/O alem da normalizacao) e NUNCA cacheada: depende do
        /// caminho, e nao dos bytes.
        /// </summary>
        private TriageOutcome ApplyContextEvidence(NormalizedPath path)
        {
            var rules = new DetectionRuleSet();
            var requireSignature = false;

            // --- Stream de dados alternativo ---
            // "foto.jpg.exe:payload" nao aparece em listagem de diretorio e o
            // conteudo real vive depois do ':'. A versao anterior tratava isso
            // como JPEG e nao via indicador nenhum.
            if (path.HasHiddenStream)
            {
                rules.Add(DetectionRule.AlternateDataStream,
                    ShieldPolicy.AlternateDataStream,
                    $"Conteudo em alternate data stream (stream '{path.AlternateDataStream}')");
            }

            // --- PE com extensao nao-executavel ---
            if (path.IsPortableExecutable &&
                !PathNormalizer.IsExecutableExtension(path.Extension) &&
                !string.IsNullOrEmpty(path.Extension))
            {
                rules.Add(DetectionRule.DisguisedExtension,
                    ShieldPolicy.DisguisedExtension,
                    $"Arquivo PE (executavel real) com extensao nao-executavel '{path.Extension}'");
                requireSignature = true;
            }

            // --- Extensao executavel sem magic MZ ---
            // Nome afirma executavel, conteudo nao e PE. Falsificacao de tipo.
            if (!path.IsPortableExecutable &&
                PathNormalizer.IsExecutableExtension(path.Extension) &&
                path.Length > 0)
            {
                rules.Add(DetectionRule.HiddenExecutableExtension,
                    ShieldPolicy.HiddenExecutableExtension / 2,
                    $"Extensao '{path.Extension}' sem assinatura PE no cabecalho (tipo falsificado)");
            }

            // --- Extensao dupla enganosa ---
            if (PathNormalizer.IsDeceptiveDoubleExtension(path))
            {
                rules.Add(DetectionRule.DeceptiveDoubleExtension,
                    ShieldPolicy.DeceptiveDoubleExtension,
                    $"Extensao dupla: '{path.FileName}' tenta se passar por documento");
            }

            // --- Palavras-chave de malware ---
            foreach (var (token, label) in HighConfidenceKeywords)
            {
                if (!path.FileNameLower.Contains(token, StringComparison.Ordinal)) continue;
                if (IsBenignCompound(path.FileNameLower, token)) continue;

                rules.Add(DetectionRule.MalwareKeywordInName,
                    ShieldPolicy.MalwareKeyword,
                    $"Nome contem indicador de malware: '{token}' ({label})");
                goto KeywordScanComplete; // uma evidencia basta
            }

            foreach (var (token, label, points) in AmbiguousKeywords)
            {
                if (!path.FileNameLower.Contains(token, StringComparison.Ordinal)) continue;
                if (IsBenignCompound(path.FileNameLower, token)) continue;

                rules.Add(DetectionRule.MalwareKeywordInName, points,
                    $"Nome contem termo suspeito: '{token}' ({label})");
                break;
            }
            KeywordScanComplete:

            // --- Conteudo nao-executavel ---
            // Desconto aplicado so quando NAO ha stream alternativo: um stream
            // pode esconder o executavel mesmo quando o hospedeiro e uma imagem.
            // Sem esta guarda, "foto.jpg:payload.exe" recebia -25 por "conteudo
            // nao-executavel" ao mesmo tempo que +40 por stream oculto, e o
            // desconto acabava cancelando a evidencia forte.
            if (!path.HasHiddenStream &&
                !path.IsPortableExecutable &&
                !PathNormalizer.IsExecutableExtension(path.EffectiveExtension) &&
                !PathNormalizer.IsScriptExtension(path.Extension))
            {
                rules.Add(DetectionRule.ExecutableInStagingDir,
                    ShieldPolicy.NonExecutableContent,
                    "Conteudo nao-executavel (nada a inspecionar)");
            }

            // --- Local (FASE 1) ---
            // No modo de baixa atividade, local deixa de pontuar: um .dll em
            // %TEMP% durante sessao de jogo e esperado e nao e ameaca.
            if (!_lowActivityMode)
                _locationPolicy.ApplyLocationScore(path, rules);

            return new TriageOutcome
            {
                Rules = rules,
                RequiresSignatureCheck = requireSignature ||
                    PathNormalizer.IsExecutableExtension(path.EffectiveExtension) ||
                    PathNormalizer.IsScriptExtension(path.Extension)
            };
        }

        /// <summary>
        /// FASE 2: a evidencia que ABSOLVE — assinatura valida de publisher
        /// confiavel. E a unica prova POSITIVA de legitimidade do pipeline.
        /// </summary>
        private void ApplySignatureEvidence(ThreatScorecard card, SignatureResult signature)
        {
            if (signature.Status == SignatureStatus.NotApplicable ||
                signature.Status == SignatureStatus.FileNotFound)
                return;

            if (signature.IsValid && signature.IsTrustedPublisher)
            {
                card.Add(DetectionRule.TrustedPublisherInStaging,
                    ShieldPolicy.SignedByTrustedPublisher,
                    $"Assinatura valida de publisher confiavel: {signature.Publisher}");
                return;
            }

            if (signature.IsSigned && signature.IsValid)
            {
                // Assinado por publisher que NAO esta na allowlist. Vale pouco:
                // malware assinado por certificado roubado de terceiro e comum.
                // Nem absolve nem condena — 0 pontos, com evidencia registrada.
                card.Add(DetectionRule.TrustedPublisherInStaging, 0,
                    $"Assinado por publisher desconhecido: {signature.Publisher}");
                return;
            }

            if (signature.IsSigned)
            {
                card.Add(DetectionRule.InvalidSignature, 40,
                    $"Assinatura presente mas invalida (expirada ou adulterada): {signature.Publisher}");
                return;
            }

            card.Add(DetectionRule.UnsignedBinary, 25,
                "Executavel sem assinatura digital");
        }

        /// <summary>
        /// FASE 3: converte a analise de CONTEUDO em evidencias nomeadas.
        /// Tudo aqui e funcao dos bytes — logo, cacheavel por hash.
        /// </summary>
        private void ApplyContentEvidence(ThreatScorecard card, FileContentAnalysis content, NormalizedPath path)
        {
            if (content.Pe is { } pe)
            {
                if (pe.HasMetasploitIndicators)
                {
                    card.Add(DetectionRule.MetasploitIndicator, 50,
                        $"Indicadores de Metasploit/Meterpreter: {string.Join(", ", pe.MetasploitIndicators)}");
                }

                if (pe.HasSuspiciousImports)
                {
                    card.Add(DetectionRule.SuspiciousImports, 20,
                        $"Imports de injecao/evasao: {string.Join(", ", pe.SuspiciousImports)}");
                }

                if (pe.IsPacked)
                {
                    card.Add(DetectionRule.PackedBinary, 15,
                        $"Empacotado com {pe.PackerName}");
                }
            }

            if (content.Entropy > 7.5)
            {
                card.Add(DetectionRule.HighEntropy, 30,
                    $"Entropia muito alta ({content.Entropy:F2}) — empacotamento ou cifragem");
            }
            else if (content.Entropy > 7.0)
            {
                card.Add(DetectionRule.HighEntropy, 15,
                    $"Entropia elevada ({content.Entropy:F2})");
            }

            // Executavel minúsculo e PE completo ao mesmo tempo e raro de forma
            // legitima: droppers e loaders funcionam assim.
            if (content.IsPortableExecutable && content.Length > 0 && content.Length < 4096)
            {
                card.Add(DetectionRule.TinyExecutable, 12,
                    $"PE completo muito pequeno ({content.Length} bytes) — tipico de dropper/loader");
            }
        }

        /// <summary>
        /// Neutraliza uma palavra-chave quando ela aparece como parte de um termo
        /// benigno conhecido. Sem isso, "Shack.dll" casaria com "hack" e
        /// "chromeloader.js" casaria com "loader" — o bug de substring que a
        /// versao anterior tinha.
        /// </summary>
        private static bool IsBenignCompound(string fileNameLower, string token)
        {
            foreach (var compound in BenignNameCompounds)
            {
                if (compound.Contains(token, StringComparison.Ordinal) &&
                    fileNameLower.Contains(compound, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsShadowed(ThreatVerdict verdict)
        {
            foreach (var ruleId in verdict.RuleIds)
            {
                if (_mode.IsShadowed(ruleId)) return true;
            }
            return false;
        }

        private static string ComputeSha256(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return string.Empty;

                using var sha = SHA256.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
                var hash = sha.ComputeHash(stream);
                return Convert.ToHexString(hash);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
