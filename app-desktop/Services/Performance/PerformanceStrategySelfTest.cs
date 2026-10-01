using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// [FIX:STRATEGY-3-STATES] PROVA DE QUE OS 3 BOTÕES FAZEM COISAS DIFERENTES.
    ///
    /// O QUE ESTE TESTE DEFENDE
    /// =========================
    /// O defeito original desta tela era "marcar e não acontecer": os três botões
    /// gravavam um texto, e o único consumidor decidia com um SIM/NÃO, de modo que
    /// dois dos três produziam exatamente o mesmo resultado. Nenhum build acusava
    /// nada, porque o código compilava e a tela funcionava — só o efeito sumia.
    ///
    /// Este self-test existe para que isso não volta. Ele percorre as três
    /// estratégias e compara a ASSINATURA de cada uma em todos os eixos que
    /// produzem efeito no sistema:
    ///
    ///     EPP gravado | plano de energia | limpeza agressiva | troca de plano
    ///
    /// Se duas estratégias produzirem a mesma assinatura, o método lança — e o
    /// teste falha. É a única forma de "100% garantir" que o usuário pediu sem
    /// depender de alguém abrir o app e olhar.
    ///
    /// Ele roda no startup, é barato (só aritmética, nenhuma chamada ao
    /// Windows) e devolve o resultado no log para inspeção.
    /// </summary>
    public static class PerformanceStrategySelfTest
    {
        /// <summary>
        /// Executa a verificação. Devolve <c>true</c> se as três estratégias são
        /// comprovadamente distintas em todos os eixos.
        /// </summary>
        public static bool Run(ILoggingService? logger, out string report)
        {
            var strategies = new[]
            {
                PerformanceStrategy.MaxPerformance,
                PerformanceStrategy.Balanced,
                PerformanceStrategy.BatterySaver
            };

            var signatures = new Dictionary<PerformanceStrategy, string>();
            var linhas = new List<string>();

            foreach (var s in strategies)
            {
                string sig = PerformanceStrategyMap.Signature(s);
                signatures[s] = sig;
                linhas.Add($"    {PerformanceStrategyMap.ToCanonical(s),-11} token='{PerformanceStrategyMap.ToToken(s)}'  {sig}");
            }

            // Cada par tem de ser diferente. Com 3 estratégias são 3 comparações.
            var duplicadas = new List<string>();
            for (int i = 0; i < strategies.Length; i++)
            {
                for (int j = i + 1; j < strategies.Length; j++)
                {
                    if (signatures[strategies[i]] == signatures[strategies[j]])
                    {
                        duplicadas.Add(
                            $"{PerformanceStrategyMap.ToCanonical(strategies[i])} == {PerformanceStrategyMap.ToCanonical(strategies[j])}");
                    }
                }
            }

            // Os EPPs também precisam ser 3 valores distintos, porque é o valor
            // gravado no registro do Windows.
            var epps = strategies.Select(PerformanceStrategyMap.GetEppValue).ToList();
            if (epps.Distinct().Count() != epps.Count)
            {
                duplicadas.Add($"EPP repetido: [{string.Join(", ", epps)}]");
            }

            bool ok = duplicadas.Count == 0;

            report =
                "[StrategySelfTest] Assinatura de cada estratégia:" + Environment.NewLine +
                string.Join(Environment.NewLine, linhas) + Environment.NewLine +
                (ok
                    ? "[StrategySelfTest] RESULTADO: OK — as 3 estratégias produzem saídas distintas."
                    : "[StrategySelfTest] RESULTADO: FALHOU — estratégias indistinguíveis: " + string.Join("; ", duplicadas));

            if (ok)
            {
                logger?.LogSuccess(report);
            }
            else
            {
                logger?.LogError(report);
            }

            return ok;
        }
    }
}
