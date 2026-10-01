using System;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// [FIX:STRATEGY-3-STATES] A ESTRATÉGIA DE PERFORMANCE, COMO VALOR DE VERDADE.
    ///
    /// O QUE EXISTIA ANTES
    /// ==================
    /// A tela "Como prefere que o sistema se comporte?" oferece três opções, e o
    /// ViewModel grava o token escolhido em `UserAnswers.Priority`:
    ///
    ///     "Performance Extrema"  |  "Equilibrado"  |  "Máxima Retenção de Bateria"
    ///
    /// Havia UM ÚNICO consumidor desse campo em todo o código
    /// (`IntelligentCleanupEngine`), e ele fazia isto:
    ///
    ///     bool IsHighPerformancePriority(string? priority)
    ///         => priority.Contains("Extrema") || priority.Contains("Performance") ...
    ///
    /// Ou seja, a pergunta era SIM/NÃO, não "qual das três". Consequências, todas
    /// reais e nenhuma delas teóricas:
    ///
    ///   1. "Equilibrado" e "Máxima Retenção de Bateria" produziam EXATAMENTE o
    ///      mesmo resultado. Dois botões, um comportamento só.
    ///   2. A estratégia só era consultada no ramo `GeneralBalanced` do switch de
    ///      perfil. Nos outros cinco perfis (Gamer, Office, Vídeo, Dev,
    ///      Enterprise) o valor era lido e jogado no lixo.
    ///   3. O casamento era por SUBSTRING de um texto em português. "Performance"
    ///      dentro de qualquer string nova mudava o comportamento sozinho.
    ///
    /// Este enum é a correção: três estados, nomeados, sem substring, e sem
    /// depender do texto traduzido. A tela, a limpeza e a energia passam a ler
    /// este valor — uma fonte só.
    /// </summary>
    public enum PerformanceStrategy
    {
        /// <summary>Economiza energia: o sistema segura o pico de desempenho.</summary>
        BatterySaver = 0,

        /// <summary>Equilíbrio entre resposta e consumo.</summary>
        Balanced = 1,

        /// <summary>Desempenho máximo: EPP zerado e plano de alto desempenho.</summary>
        MaxPerformance = 2
    }

    /// <summary>
    /// [FIX:STRATEGY-3-STATES] Tabela que liga o token da tela ao valor real.
    ///
    /// Cada estratégia tem TRÊS saídas concretas e verificáveis:
    ///   - <see cref="GetEppValue"/>  → valor gravado no Energy Performance Preference
    ///   - <see cref="GetPowerPlan"/> → plano de energia do Windows
    ///   - <see cref="AllowsAggressiveCleanup"/> → se a limpeza pode ser agressiva
    ///
    /// É essa tabela que garante que as três opções façam coisas DIFERENTES. Um
    /// teste automatizado (ver `PerformanceStrategySelfTest`) grava as três
    /// escolhas e falha se alguma produzir o mesmo resultado — para que ninguém
    /// consiga reintroduzir o "marcar e não acontecer" sem o build quebrar.
    /// </summary>
    public static class PerformanceStrategyMap
    {
        // Tokens da tela (estáveis em português, como os tokens de perfil).
        public const string TokenMaxPerformance = "Performance Extrema";
        public const string TokenBalanced = "Equilibrado";
        public const string TokenBatterySaver = "Máxima Retenção de Bateria";

        // Canônicos, em inglês, usados por telas internas e telemetria.
        public const string CanonicalMaxPerformance = "Performance";
        public const string CanonicalBalanced = "Balanced";
        public const string CanonicalBatterySaver = "Battery";

        /// <summary>
        /// Converte qualquer token para um dos três estados.
        ///
        /// DE VOLTA ao "Equilibrado" quando não reconhece o token, e isso é
        /// intencional: Balanced é o estado que não faz promessas extremas. Um
        /// token desconhecido NUNCA deve virar "Performance Extrema".
        /// </summary>
        public static PerformanceStrategy ToStrategy(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return PerformanceStrategy.Balanced;
            }

            string t = token.Trim();

            // Tokens exatos da tela.
            if (string.Equals(t, TokenMaxPerformance, StringComparison.OrdinalIgnoreCase)) return PerformanceStrategy.MaxPerformance;
            if (string.Equals(t, TokenBatterySaver, StringComparison.OrdinalIgnoreCase)) return PerformanceStrategy.BatterySaver;
            if (string.Equals(t, TokenBalanced, StringComparison.OrdinalIgnoreCase)) return PerformanceStrategy.Balanced;

            // Canônicos em inglês, gravados por telas internas (ex.: Apply All).
            if (string.Equals(t, CanonicalMaxPerformance, StringComparison.OrdinalIgnoreCase)) return PerformanceStrategy.MaxPerformance;
            if (string.Equals(t, CanonicalBatterySaver, StringComparison.OrdinalIgnoreCase)) return PerformanceStrategy.BatterySaver;
            if (string.Equals(t, CanonicalBalanced, StringComparison.OrdinalIgnoreCase)) return PerformanceStrategy.Balanced;

            // Canônicos em português, para o valor não se perder se um dia o
            // questionário gravar direto o canônico.
            if (string.Equals(t, "Equilibrado", StringComparison.OrdinalIgnoreCase)) return PerformanceStrategy.Balanced;
            if (string.Equals(t, "Desempenho Máximo", StringComparison.OrdinalIgnoreCase)) return PerformanceStrategy.MaxPerformance;
            if (string.Equals(t, "Economia de Bateria", StringComparison.OrdinalIgnoreCase)) return PerformanceStrategy.BatterySaver;

            return PerformanceStrategy.Balanced;
        }

        /// <summary>Converte de volta para o token estável da tela.</summary>
        public static string ToToken(PerformanceStrategy strategy) => strategy switch
        {
            PerformanceStrategy.MaxPerformance => TokenMaxPerformance,
            PerformanceStrategy.BatterySaver => TokenBatterySaver,
            _ => TokenBalanced
        };

        /// <summary>Converte para o canônico em inglês (logs e telemetria).</summary>
        public static string ToCanonical(PerformanceStrategy strategy) => strategy switch
        {
            PerformanceStrategy.MaxPerformance => CanonicalMaxPerformance,
            PerformanceStrategy.BatterySaver => CanonicalBatterySaver,
            _ => CanonicalBalanced
        };

        /// <summary>
        /// Energy Performance Preference: 0 = desempenho máximo, 255 = economia
        /// máxima. Os três valores são distintos de propósito — é o que a
        /// verificação mede no sistema.
        /// </summary>
        public static int GetEppValue(PerformanceStrategy strategy) => strategy switch
        {
            PerformanceStrategy.MaxPerformance => 0,   // 0% economy = performance
            PerformanceStrategy.BatterySaver => 255,   // 100% economy
            _ => 128                                    // meio-termo
        };

        /// <summary>GUID do plano de energia do Windows para a estratégia.</summary>
        public static Guid GetPowerPlan(PerformanceStrategy strategy) => strategy switch
        {
            PerformanceStrategy.MaxPerformance => new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61"), // Ultimate Performance
            PerformanceStrategy.BatterySaver => new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"), // Economia de energia
            _ => new Guid("381b4222-f694-41f0-9685-ff5bb260df2e")                                  // Equilibrado
        };

        /// <summary>
        /// Se a limpeza pode ser agressiva. Agora vale para TODOS os perfis:
        /// antes isso só era consultado no ramo "Uso Geral".
        /// </summary>
        public static bool AllowsAggressiveCleanup(PerformanceStrategy strategy)
            => strategy == PerformanceStrategy.MaxPerformance;

        /// <summary>Se a estratégia permite trocar o plano de energia do sistema.</summary>
        public static bool AllowsPowerPlanChanges(PerformanceStrategy strategy) => strategy switch
        {
            // Economia é justamente sobre não gastar; não trocamos plano do usuário.
            PerformanceStrategy.BatterySaver => false,
            _ => true
        };

        /// <summary>
        /// Assinatura comparável das três estratégias. Usada pelo self-test para
        /// provar, por diferença, que as três opções produzem resultados
        /// distintos em TODOS os eixos que importam.
        /// </summary>
        public static string Signature(PerformanceStrategy strategy) =>
            $"epp={GetEppValue(strategy)};plan={GetPowerPlan(strategy)};aggressive={AllowsAggressiveCleanup(strategy)};planChange={AllowsPowerPlanChanges(strategy)}";
    }
}
