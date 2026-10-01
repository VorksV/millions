using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace VoltrisOptimizer.Utils
{
    /// <summary>
    /// [FIX:C-3] Acesso a processos tolerante a processo que morre no meio da leitura.
    ///
    /// BUG ORIGINAL: o projeto tem 143 chamadas a
    /// <see cref="Process.GetProcessById(int)"/>, e a imensa maioria está dentro
    /// de um laço que varre a lista de processos do sistema. Entre o momento em
    /// que a lista é montada e o momento em que cada PID é consultado, o
    /// processo naturalmente encerra — e <c>GetProcessById</c> lança
    /// <see cref="ArgumentException"/>.
    ///
    /// Isso não é exceção de erro, é consequência inevitável de varrer
    /// processos: qualquer programa que enumere e depois inspecione vai perder
    /// processos que terminam no meio do percurso. Um serviço de 10 s que
    /// varre os ~200 processos da máquina vai encontrar PIDs mortos uma dúzia de
    /// vezes por ciclo, só pela natureza da operação.
    ///
    /// A validação em runtime mediu 126 dessas exceções em 9 minutos de uso
    /// normal (2026-09-27). Nenhuma delas quebrou o app — todas eram capturadas
    /// por algum <c>catch</c> mais acima. Mas cada uma custa o preço de
    /// desenrolar e capturar uma exceção, e o mais importante: elas entulham o
    /// log e escondem problema real. O log do VOLTRIS chegou a ter 126
    /// ArgumentException — 94% de todo o ruído de exceção — e o que realmente
    /// merecia atenção ficou invisível no meio delas.
    ///
    /// Estas funções eliminam a exceção pela raiz, sem try/catch em cada
    /// call site.
    /// </summary>
    public static class SafeProcess
    {
        /// <summary>
        /// Obtém o processo pelo PID, ou <c>null</c> se ele não existir mais.
        /// Nunca lança por processo morto — que é o caso normal, não o erro.
        /// </summary>
        public static Process? TryGet(int pid)
        {
            if (pid <= 0) return null;
            try
            {
                return Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                // PID não existe mais: o processo terminou entre listar e consultar.
                return null;
            }
            catch (InvalidOperationException)
            {
                // Objeto Process já foi liberado / não associado a um processo.
                return null;
            }
        }

        /// <summary>
        /// O processo existe e ainda está em execução?
        /// </summary>
        public static bool IsRunning(int pid)
        {
            using var p = TryGet(pid);
            if (p == null) return false;
            try { return !p.HasExited; }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>
        /// Nome do processo pelo PID, ou string vazia se não existir.
        /// </summary>
        public static string NameOf(int pid)
        {
            using var p = TryGet(pid);
            if (p == null) return "";
            try { return p.ProcessName; }
            catch (InvalidOperationException) { return ""; }
        }

        /// <summary>
        /// Executa <paramref name="action"/> com o processo somente se ele
        /// existir, e devolve false caso contrário. Use quando a lógica é
        /// "ajusta o processo se ele estiver lá".
        /// </summary>
        public static bool WithProcess(int pid, Action<Process> action)
        {
            using var p = TryGet(pid);
            if (p == null) return false;
            try
            {
                if (p.HasExited) return false;
                action(p);
                return true;
            }
            catch (InvalidOperationException)
            {
                // O processo morreu entre a checagem e o uso.
                return false;
            }
        }

        /// <summary>
        /// Resolve vários PIDs de uma vez, descartando silenciosamente os que
        /// morreram. É o formato certo para varrer uma lista: em vez de
        /// 143 call sites com try/catch, um filtro no lugar certo.
        /// </summary>
        public static List<Process> ResolveAll(IEnumerable<int> pids)
        {
            var vivos = new List<Process>();
            if (pids == null) return vivos;

            foreach (var pid in pids)
            {
                var p = TryGet(pid);
                if (p == null) continue;
                try
                {
                    if (!p.HasExited) vivos.Add(p);
                    else p.Dispose();
                }
                catch (InvalidOperationException) { /* morreu agora */ }
            }
            return vivos;
        }
    }
}
