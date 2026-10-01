using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler.UI.Views
{
	/// <summary>
	/// Log de diagnóstico DIRETO em arquivo, com caminho fixo e conhecido.
	///
	/// POR QUE ISTO EXISTE: o <c>App.LoggingService</c> grava em um diretório
	/// que varies com a configuração e não estava sendo encontrado em disco —
	/// ou seja, na prática não havia visibilidade nenhuma. Aqui o caminho é
	/// fixo, a escrita é sincronizada com flush imediato, e o arquivo é criado
	/// na primeira linha. Se este arquivo não existir, o log nunca rodou.
	///
	/// Caminho: %LOCALAPPDATA%\Voltris\Logs\profiler_view_debug.log
	/// </summary>
	internal static class ProfilerViewDebug
	{
		private static readonly object Gate = new object();
		private static string? _path;
		private static bool _pathResolved;

		public static string ResolvePath()
		{
			if (_pathResolved)
			{
				return _path ?? "";
			}

			try
			{
				string dir = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					"Voltris", "Logs");
				Directory.CreateDirectory(dir);
				_path = Path.Combine(dir, "profiler_view_debug.log");
			}
			catch
			{
				_path = null;
			}

			_pathResolved = true;
			return _path ?? "";
		}

		public static void Log(string message)
		{
			try
			{
				string path = ResolvePath();
				if (string.IsNullOrEmpty(path))
				{
					return;
				}

				string line = string.Format(
					CultureInfo.InvariantCulture,
					"{0:HH:mm:ss.fff} [tid={1}] {2}{3}",
					DateTime.Now,
					Process.GetCurrentProcess().Id,
					message,
					Environment.NewLine);

				lock (Gate)
				{
					File.AppendAllText(path, line, Encoding.UTF8);
				}
			}
			catch
			{
				// Log nunca pode derrubar a view.
			}
		}

		/// <summary>
		/// Desembrulha toda a cadeia de InnerException.
		///
		/// O XamlParseException do WPF é famously inútil sozinho: a mensagem real
		/// ("O valor fornecido em 'TypeConverterMarkupExtension' iniciou uma
		/// exceção") só diz QUE UMA COISA DEIXOU FALHAR, não qual. A causa
		/// concreta — o tipo, a propriedade e a linha do XAML — está sempre em
		/// uma exceção interna, e sem esta caminhada a única pista é o nó do
		/// XAML culpado. Foi exatamente o que aconteceu na investigação da
		/// tela de Perfil Inteligente: a mensagem de topo não apontava nada.
		/// </summary>
		public static void LogExceptionChain(string prefix, Exception? ex)
		{
			if (ex == null)
			{
				Log(prefix + " (nenhuma excecao)");
				return;
			}

			int depth = 0;
			Exception? current = ex;
			while (current != null && depth < 12)
			{
				Log($"{prefix} EX[{depth}] {current.GetType().FullName}");
				Log($"{prefix}       MSG: {current.Message.Replace(Environment.NewLine, " | ")}");

				// Posição no XAML, quando houver.
				if (current is System.Windows.Markup.XamlParseException xpe && xpe.LinePosition > 0)
				{
					Log($"{prefix}       XAML linha {xpe.LinePosition}, linha base {xpe.LineNumber}");
				}

				current = current.InnerException;
				depth++;
			}

			// AggregateException traz as irrecuperáveis; elas não aparecem na cadeia.
			if (ex is AggregateException agg)
			{
				int i = 0;
				foreach (Exception inner in agg.InnerExceptions)
				{
					Log($"{prefix} --- Aggregate In[{i}] ---");
					LogExceptionChain($"{prefix}   ", inner);
					i++;
				}
			}
		}

		/// <summary>
		/// Registra o tamanho/visibilidade real de um elemento. É a informação
		/// que separa "a view não carregou" de "carregou, mas com tamanho zero".
		/// </summary>
		public static void LogElement(string label, System.Windows.FrameworkElement? element)
		{
			try
			{
				if (element == null)
				{
					Log($"  ELEM {label}: NULL");
					return;
				}

				Log($"  ELEM {label}: W={Safe(() => element.ActualWidth)} H={Safe(() => element.ActualHeight)} " +
					$"DesiredW={Safe(() => element.DesiredSize.Width)} DesiredH={Safe(() => element.DesiredSize.Height)} " +
					$"Vis={element.Visibility} Parent={element.Parent?.GetType().Name ?? "null"}");
			}
			catch (Exception ex)
			{
				Log($"  ELEM {label}: EXCEPTION {ex.GetType().Name}: {ex.Message}");
			}
		}

		private static string Safe(Func<double> get)
		{
			try
			{
				double v = get();
				if (double.IsNaN(v))
				{
					return "NaN";
				}
				return v.ToString("F1", CultureInfo.InvariantCulture);
			}
			catch
			{
				return "?";
			}
		}
	}
}
