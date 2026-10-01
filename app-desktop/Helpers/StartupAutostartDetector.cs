using System;
using System.Linq;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Identifica se a execução atual veio do auto-start do Windows ou se o
    /// usuário abriu o aplicativo na mão.
    ///
    /// POR QUE UM ARGUMENTO, E NÃO INTUIR
    /// ===================================
    /// A primeira tentativa de resolver isso foi comparar o caminho do
    /// executável com a entrada de auto-start no registro. Isso não funciona:
    /// a entrada existe independentemente de quem lançou o processo, então
    /// um clique duplo do usuário produzia exatamente o mesmo comando de linha
    /// que o logon do Windows — e o app escondia a janela de um lançamento que
    /// o usuário tinha acabado de fazer de propósito. Era o mesmo defeito que
    /// já existia antes, só com outra roupa.
    ///
    /// Não existe informação no processo que distinga "o shell me iniciou" de
    /// "o usuário me iniciou" quando os dois partem do mesmo executável. Por
    /// isso a distinção é feita na origem: quem instala o auto-start é o
    /// próprio app, e ele passa um argumento-sinalizador. Se o argumento está
    /// na linha de comando, o auto-start iniciou; se não está, o usuário
    /// iniciou. Não há heurística nem palpites.
    ///
    /// A política, pedida explicitamente pelo usuário:
    ///
    ///   · Auto-start do Windows      -> sempre sobe para a bandeja, sem
    ///                                   janela aparecendo na tela.
    ///   · Clique manual do usuário   -> mostra a janela normalmente, a não
    ///                                   ser que ele tenha marcado a opção
    ///                                   "Iniciar minimizado".
    /// </summary>
    internal static class StartupAutostartDetector
    {
        /// <summary>
        /// Argumento que o app grava no próprio registro/tarefa de auto-start.
        /// Nenhum caminho manual gera este argumento, por isso ele é a prova
        /// de origem.
        /// </summary>
        public const string AutostartFlag = "--autostart";

        /// <summary>
        /// <c>true</c> somente quando o processo foi iniciado pelo
        /// auto-start configurado pelo próprio app.
        /// </summary>
        public static bool IsAutoStart()
        {
            try
            {
                return HasAutostartFlag();
            }
            catch
            {
                // Sem como ler a linha de comando: trata como manual, porque
                // esconder a janela de um clique manual é o pior erro possível.
                return false;
            }
        }

        /// <summary>
        /// Procura o argumento-sinalizador entre os argumentos reais do
        /// processo (pulando o caminho do executável em [0]).
        /// </summary>
        public static bool HasAutostartFlag()
        {
            var userArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
            return userArgs.Any(a =>
                a != null && a.Equals(AutostartFlag, StringComparison.OrdinalIgnoreCase));
        }
    }
}
