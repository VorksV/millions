using System;
using System.IO;

namespace VoltrisOptimizer.Services.Logging
{
    /// <summary>
    /// Resolve, em UM lugar so, o diretorio de logs da aplicacao.
    ///
    /// POR QUE ISTO EXISTE (bug corrigido):
    /// O <c>App.xaml.cs</c> criava os loggers com
    /// <c>Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs")</c>,
    /// ou seja, gravava DENTRO da pasta do executavel. Consequencias reais:
    ///   1. Os logs sumiam a cada atualizacao/reinstalacao do app.
    ///   2. Se o app for instalado em "Program Files" (leitura para o usuario),
    ///      a escrita so funciona porque o processo e elevado — e falha silenciosa
    ///      em qualquer contexto nao-elevado.
    ///   3. O caminho "oficial" <c>%APPDATA%\Voltris\Logs</c> configurado em
    ///      <c>Core\ServiceCollectionExtensions.cs</c> NUNCA era criado, porque
    ///      o <c>ILoggingService</c> ja vem registrado por quem chama
    ///      <c>Bootstrapper.ConfigureServices</c>, e o registro e condicional
    ///      (<c>if (!services.Any(...))</c>). Esse ramo era codigo morto.
    ///   4. Nenhuma auditoria de suporte encontrava os logs, porque ninguem
    ///      procurava dentro da pasta do .exe.
    ///
    /// O caminho adotado e <c>%LOCALAPPDATA%\Voltris\Logs</c>, que ja e o
    /// destino real do log da pagina de perfil (<c>profiler_view_debug.log</c>)
    /// e do log de seguranca (<c>security_YYYY-MM-DD.log</c>). Assim todos os
    /// logs do app passam a viver em um unico lugar previsivel, que sobrevive a
    /// atualizacoes e independe de onde o app foi instalado.
    /// </summary>
    internal static class LogDirectoryResolver
    {
        private const string VendorFolder = "Voltris";
        private const string LogsFolder = "Logs";

        private static string? _cached;

        /// <summary>
        /// Diretorio canonico de logs. Cria a pasta se ainda nao existir.
        /// Nao lanca excecao: se o caminho primario falhar, cai para a pasta do
        /// executavel (comportamento antigo) para nunca perder o diagnostico.
        /// </summary>
        public static string Resolve()
        {
            if (_cached != null)
                return _cached;

            string? primary = null;

            try
            {
                string localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

                if (!string.IsNullOrWhiteSpace(localAppData))
                {
                    primary = Path.Combine(localAppData, VendorFolder, LogsFolder);
                    Directory.CreateDirectory(primary);
                }
            }
            catch (Exception ex)
            {
                // Sem LOCALAPPDATA utilizavel (perfil roaming quebrado, ACL
                // negada, container). Seguimos para o fallback.
                System.Diagnostics.Debug.WriteLine(
                    $"[LogDirectoryResolver] Caminho primario indisponivel: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(primary))
            {
                try
                {
                    primary = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogsFolder);
                    Directory.CreateDirectory(primary);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[LogDirectoryResolver] Fallback tambem falhou: {ex.Message}");

                    // Ultimo recurso: temp do usuario. Melhor um log em local
                    // inesperado do que nenhum log.
                    primary = Path.Combine(Path.GetTempPath(), LogsFolder);
                    Directory.CreateDirectory(primary);
                }
            }

            _cached = primary!;
            return _cached!;
        }

        /// <summary>
        /// Usado em testes/diagnostico: limpa o cache para reavaliar o caminho.
        /// </summary>
        public static void ResetCache() => _cached = null;
    }
}
