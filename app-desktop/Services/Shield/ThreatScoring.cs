using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>
    /// Catálogo de regras de detecção.
    ///
    /// POR QUE EXISTIR: um antivírus profissional não pode ter regras anônimas.
    /// Cada regra precisa de um identificador estável para (a) telemetria — saber
    /// qual regra está gerando falso positivo em campo, (b) kill switch — desabilitar
    /// uma regra quebrada sem recompilar, e (c) teste de regressão — fixar o
    /// comportamento esperado por ID num corpus de arquivos limpos conhecidos.
    ///
    /// A versão anterior do Shield era um `if/return` monolithico: a "regra" era
    /// `contains(@"\temp\")` e o nome do arquivo era o único vestígio do que
    /// disparou. Isso é o que produziu a lista crescente de whitelists por nome
    /// de arquivo (DISM, extensões duplas, artefatos de build) — remendo sobre
    /// remendo, porque nenhuma regra podia ser medida nem desligada sozinha.
    /// </summary>
    public static class DetectionRule
    {
        // --- Execução em diretório de staging ---
        public const string ExecutableInStagingDir = "SHIELD-1001";
        public const string ExecutableInDownloads = "SHIELD-1002";
        public const string ScriptInDownloads = "SHIELD-1003";

        // --- Apariência do arquivo ---
        public const string DeceptiveDoubleExtension = "SHIELD-1010";
        public const string MalwareKeywordInName = "SHIELD-1011";
        public const string HiddenExecutableExtension = "SHIELD-1012";
        public const string AlternateDataStream = "SHIELD-1013";
        public const string DisguisedExtension = "SHIELD-1014";

        // --- Assinatura digital ---
        public const string UnsignedBinary = "SHIELD-1020";
        public const string InvalidSignature = "SHIELD-1021";
        public const string TrustedPublisherInStaging = "SHIELD-1022";

        // --- Análise estática profunda ---
        public const string MetasploitIndicator = "SHIELD-1030";
        public const string SuspiciousImports = "SHIELD-1031";
        public const string PackedBinary = "SHIELD-1032";
        public const string HighEntropy = "SHIELD-1033";
        public const string TinyExecutable = "SHIELD-1034";

        // --- Comportamento ---
        public const string PersistentRwxMemory = "SHIELD-1040";
        public const string SuspiciousPortConnection = "SHIELD-1041";

        public static IReadOnlyCollection<string> All { get; } = new[]
        {
            ExecutableInStagingDir, ExecutableInDownloads, ScriptInDownloads,
            DeceptiveDoubleExtension, MalwareKeywordInName, HiddenExecutableExtension,
            AlternateDataStream, DisguisedExtension,
            UnsignedBinary, InvalidSignature, TrustedPublisherInStaging,
            MetasploitIndicator, SuspiciousImports, PackedBinary, HighEntropy, TinyExecutable,
            PersistentRwxMemory, SuspiciousPortConnection
        };
    }

    /// <summary>
    /// Política de escalonamento do Shield.
    ///
    /// CONCEITO CENTRAL: as evidências mais baratas de coletar (extensão, caminho,
    /// nome) são as MENOS confiáveis. Elas devem poder SOMAR pontos, nunca
    /// condemnar sozinhas. A versão anterior tratava "DLL em %TEMP%" como veredito
    /// Medium — daí 15 alertas em 2 segundos para arquivos Microsoft e Windscribe
    /// assinados. Aqui, %TEMP% vale 10 pontos, e a assinatura válida de um
    /// publisher confiável vale -45: o resultado final é "limpo".
    ///
    /// Os limiares são a política de notificação. Abaixo de LogThreshold o arquivo
    /// nem entra no log de segurança (ruído puro). Abaixo de NotifyThreshold ele é
    /// registrado mas NÃO gera toast — toast para heurística estática é o que
    /// transforma um antivírus em máquina de spam.
    /// </summary>
    public static class ShieldPolicy
    {
        // --- Pontuação ---
        /// <summary>Abaixo disto o arquivo é considerado limpo e nem é registrado.</summary>
        public const int LogThreshold = 12;

        /// <summary>A partir daqui o evento alimenta a lista da UI e o log de segurança.</summary>
        public const int DetectionThreshold = 25;

        /// <summary>A partir daqui o usuário recebe notificação (ainta agregada em rajada).</summary>
        public const int NotifyThreshold = 60;

        /// <summary>A partir daqui a UI oferece quarentena automática.</summary>
        public const int QuarantineThreshold = 80;

        /// <summary>A partir daqui e ameaca confirmada: erro, nao aviso.</summary>
        public const int CriticalThreshold = 92;

        // --- Pontos por evidencia ---
        // As evidences tem sinal positivo E negativo. Negativo = absolvicao.
        public const int SignedByTrustedPublisher = -45;
        public const int InTrustedSystemRoot = -35;
        public const int InProgramFiles = -30;
        public const int VoltrisOwnedFile = -60;
        public const int InstallerStagingLayout = -12;
        public const int BuildArtifact = -20;
        public const int NonExecutableContent = -25;

        public const int InStagingDir = 10;
        public const int InDownloadsDir = 8;
        public const int ScriptInDownloads = 22;
        public const int DeceptiveDoubleExtension = 30;
        public const int MalwareKeyword = 35;
        public const int HiddenExecutableExtension = 45;
        public const int AlternateDataStream = 40;
        public const int DisguisedExtension = 50;
    }

    /// <summary>
    /// Uma evidência isolada que contribuiu para a pontuação.
    /// Guardada no veredito para que o log explique SEMPRE o porquê,
    /// e para que a detecção possa ser reproduzida e auditada.
    /// </summary>
    public sealed class ScoreContribution
    {
        public string RuleId { get; }
        public int Points { get; }
        public string Evidence { get; }

        public ScoreContribution(string ruleId, int points, string evidence)
        {
            RuleId = ruleId;
            Points = points;
            Evidence = evidence;
        }

        public override string ToString() =>
            Points >= 0 ? $"+{Points} {RuleId}: {Evidence}" : $"{Points} {RuleId}: {Evidence}";
    }

    /// <summary>
    /// Acumulador de pontos. Todo sinal observado vira uma ScoreContribution
    /// identificada por regra — nada de return antecipado. Isso é o que permite
    /// detectar "o que mais contribuiu" e_priorizar_ a lista de FPs.
    /// </summary>
    public sealed class ThreatScorecard
    {
        private readonly List<ScoreContribution> _contributions = new();

        public IReadOnlyList<ScoreContribution> Contributions => _contributions;

        /// <summary>
        /// Pontuacao final, com teto de absolvicao.
        ///
        /// PROBLEMA QUE O TETO RESOLVE. As evidencias negativas (local confiavel,
        /// staging de instalador, cache de build) sao todas sobre o MESMO eixo do
        /// arquivo, e somam entre si. Uma evidencia forte e independente de local
        /// — nome com indicador de malware, extensao dupla, stream oculto — pode
        /// ser-anulada por desconto de caminho. O resultado era
        /// "keygen-activator.exe" em %TEMP%\{GUID} marcando 58 e NAO notificando,
        /// quando o nome, sozinho, ja e indicio.
        ///
        /// Nenhum instalador legitimo extrai um arquivo chamado "keygen". O
        /// desconto de staging responde "aqui extrai-se executavel", e nao "o
        /// nome e inofensivo". Sao perguntas diferentes.
        ///
        /// REGRA: se alguma evidencia forte disparou, a soma das negativos e
        /// limitada. Um arquivo com nome de malware continua sendo nome de
        /// malware em qualquer diretorio.
        /// </summary>
        public int Score
        {
            get
            {
                var total = 0;
                var negative = 0;
                var hasStrong = false;

                foreach (var c in _contributions)
                {
                    total += c.Points;
                    if (c.Points < 0) negative += c.Points;
                    else if (c.Points > 0 && StrongEvidenceRules.Contains(c.RuleId)) hasStrong = true;
                }

                if (hasStrong && negative < -MaxExoneration)
                {
                    ExonerationCapped = true;
                    total += (-MaxExoneration) - negative;
                }

                return total;
            }
        }

        /// <summary>True quando o teto de absolvicao foi aplicado neste calculo.</summary>
        public bool ExonerationCapped { get; private set; }

        /// <summary>
        /// Teto de desconto total quando ha evidencia forte. -15 e o piso: um
        /// nome de malware nao e neutralizado por estar num staging de
        /// instalador, mas ainda sobra margem para a evidencia forte dominar.
        /// </summary>
        public const int MaxExoneration = -15;

        /// <summary>
        /// Regras cuja evidencia e independente de onde o arquivo esta. Only these
        /// bloqueiam a absolvicao por local.
        /// </summary>
        private static readonly HashSet<string> StrongEvidenceRules = new(StringComparer.Ordinal)
        {
            DetectionRule.MalwareKeywordInName,
            DetectionRule.DeceptiveDoubleExtension,
            DetectionRule.AlternateDataStream,
            DetectionRule.DisguisedExtension,
            DetectionRule.HiddenExecutableExtension,
            DetectionRule.MetasploitIndicator,
            DetectionRule.InvalidSignature
        };

        public void Add(string ruleId, int points, string evidence)
        {
            // Evidencia com 0 pontos NAO e descartada. "Assinado por publisher
            // desconhecido" nao move o score, mas e informação relevante para
            // auditoria — o log precisa mostrar que ela foi observada e pesou
            // zero, e nao que nunca foi vista.
            if (string.IsNullOrEmpty(ruleId) || string.IsNullOrEmpty(evidence)) return;
            _contributions.Add(new ScoreContribution(ruleId, points, evidence));
        }

        public void Add(DetectionRuleSet rules) => rules.Apply(this);

        public bool Has(string ruleId) =>
            _contributions.Any(c => string.Equals(c.RuleId, ruleId, StringComparison.Ordinal));

        public string Describe()
        {
            if (_contributions.Count == 0) return "nenhum indicador";
            var text = string.Join(" | ", _contributions.Select(c => c.ToString()));
            return ExonerationCapped
                ? text + " | [absolvicao limitada: evidencia forte independente de local]"
                : text;
        }
    }

    /// <summary>
    /// Conjunto de regras disparadas numa única detecção de arquivo, para que o
    /// chamador registre o "pacote" de evidência de uma vez.
    /// </summary>
    public sealed class DetectionRuleSet
    {
        private readonly List<(string RuleId, int Points, string Evidence)> _rules = new();

        public DetectionRuleSet Add(string ruleId, int points, string evidence)
        {
            if (string.IsNullOrEmpty(ruleId) || string.IsNullOrEmpty(evidence)) return this;
            _rules.Add((ruleId, points, evidence));
            return this;
        }

        public bool IsEmpty => _rules.Count == 0;

        /// <summary>SomaLiquida do conjunto. Pode ser negativa (absolvicao).</summary>
        public int Points => _rules.Sum(r => r.Points);

        public IReadOnlyList<ScoreContribution> ToContributions() =>
            _rules.Select(r => new ScoreContribution(r.RuleId, r.Points, r.Evidence)).ToArray();

        public void Apply(ThreatScorecard scorecard)
        {
            foreach (var (ruleId, points, evidence) in _rules)
                scorecard.Add(ruleId, points, evidence);
        }
    }

    /// <summary>
    /// Veredito final de um arquivo, com a trilha completa de evidências.
    /// É este objeto que trafega do pipeline até a UI e o log de segurança.
    /// </summary>
    public sealed class ThreatVerdict
    {
        public string FilePath { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public string FileHash { get; init; } = string.Empty;

        /// <summary>Pontuacao final, ja com as evidencias de absolvicao subtraidas.</summary>
        public int Score { get; init; }

        /// <summary>0-100. Escala independente do score: mede o grau de certeza da decisao.</summary>
        public int Confidence { get; init; }

        public ThreatSeverity Severity { get; init; }

        /// <summary>IDs de todas as regras que contribuíram.</summary>
        public IReadOnlyList<string> RuleIds { get; init; } = Array.Empty<string>();

        /// <summary>Descrição legível de cada evidência.</summary>
        public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

        /// <summary>Texto completo para o log — nunca vazio quando há detecção.</summary>
        public string Reason { get; init; } = string.Empty;

        /// <summary>Verdadeiro quando o arquivo foi aceito como legítimo APÓS
        /// já ter acumulado pontos — o caminho de absolvição.</summary>
        public bool IsExonerated { get; init; }

        public string ExonerationReason { get; init; } = string.Empty;

        /// <summary>Verdadeiro quando a pontuação atingiu DetectionThreshold.</summary>
        public bool IsDetection => Score >= ShieldPolicy.DetectionThreshold;

        /// <summary>Verdadeiro quando a pontuação atingiu NotifyThreshold. Toast só aqui.</summary>
        public bool ShouldNotify => Score >= ShieldPolicy.NotifyThreshold && !IsExonerated;

        public bool ShouldOfferQuarantine => Score >= ShieldPolicy.QuarantineThreshold && !IsExonerated;

        /// <summary>Decisao de exibicao: o arquivo entra na lista de ameacas da UI.</summary>
        public bool ShouldRecord => Score >= ShieldPolicy.DetectionThreshold && !IsExonerated;

        public static ThreatVerdict Clean(string path, string reason, int score = 0) => new()
        {
            FilePath = path,
            FileName = System.IO.Path.GetFileName(path),
            Score = score,
            Confidence = 100,
            Severity = ThreatSeverity.Low,
            Reason = reason,
            Evidence = new[] { reason },
            IsExonerated = score < ShieldPolicy.DetectionThreshold && !string.IsNullOrEmpty(reason)
        };

        public static ThreatVerdict FromScorecard(string path, string hash, ThreatScorecard card, SignatureResult? signature)
        {
            var score = Math.Clamp(card.Score, -100, 100);
            var evidence = card.Contributions.Select(c => c.Evidence).ToArray();
            var ruleIds = card.Contributions.Select(c => c.RuleId).Distinct().ToArray();

            // Absolvição: score caiu abaixo do limiar de detecção porque uma
            // evidência negativa (assinatura válida, caminho confiável) pesou
            // mais que as positivas. Este é o caminho que a versão anterior
            // não possuía — e sua ausência é a causa raiz da inundação de alertas.
            var exonerated = score < ShieldPolicy.DetectionThreshold;

            var reason = card.Contributions.Count == 0
                ? "nenhum indicador de risco"
                : string.Join(" | ", card.Contributions.Select(c => c.ToString()));

            var confidence = ComputeConfidence(card, signature);

            return new ThreatVerdict
            {
                FilePath = path,
                FileName = System.IO.Path.GetFileName(path),
                FileHash = hash,
                Score = score,
                Confidence = confidence,
                Severity = MapSeverity(score, exonerated),
                RuleIds = ruleIds,
                Evidence = evidence,
                Reason = reason,
                IsExonerated = exonerated,
                ExonerationReason = exonerated ? reason : string.Empty
            };
        }

        /// <summary>
        /// Confiança reflete a qualidade da evidência, não o score. Um malware
        /// confirmado por Indicador de Metasploit é confiança 100 mesmo com score
        /// moderado; um .dll em %TEMP% sem assinatura é confiança baixa, porque
        /// a única coisa que sabemos é o caminho.
        /// </summary>
        private static int ComputeConfidence(ThreatScorecard card, SignatureResult? signature)
        {
            const int Metasploit = 100;
            const int PackedOrEntropy = 80;
            const int SignatureProblem = 75;
            const int NameOrExtension = 55;
            const int LocationOnly = 25;

            if (card.Has(DetectionRule.MetasploitIndicator)) return Metasploit;
            if (card.Has(DetectionRule.InvalidSignature) || card.Has(DetectionRule.UnsignedBinary)) return SignatureProblem;
            if (card.Has(DetectionRule.PackedBinary) || card.Has(DetectionRule.HighEntropy)) return PackedOrEntropy;
            if (card.Has(DetectionRule.HiddenExecutableExtension) || card.Has(DetectionRule.AlternateDataStream)) return Metasploit - 10;
            if (card.Has(DetectionRule.MalwareKeywordInName) || card.Has(DetectionRule.DeceptiveDoubleExtension)) return NameOrExtension;
            if (signature is { IsTrustedPublisher: true, IsValid: true }) return 100;
            if (card.Has(DetectionRule.ExecutableInStagingDir)) return LocationOnly;
            return 40;
        }

        private static ThreatSeverity MapSeverity(int score, bool exonerated)
        {
            if (exonerated) return ThreatSeverity.Low;
            if (score >= ShieldPolicy.CriticalThreshold) return ThreatSeverity.Critical;
            if (score >= 70) return ThreatSeverity.High;
            if (score >= ShieldPolicy.DetectionThreshold) return ThreatSeverity.Medium;
            return ThreatSeverity.Low;
        }

        public override string ToString() =>
            $"{FileName} score={Score} conf={Confidence} sev={Severity} rules=[{string.Join(",", RuleIds)}]";
    }
}
