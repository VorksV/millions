using System;
using System.IO;
using System.Linq;
using System.Text;

namespace VoltrisOptimizer.Core.Brain.V2;

public static class BrainActionAuditLog
{
	private static readonly object Gate = new object();

	private const int MaxLinesToKeep = 400;

	public static void Append(string line)
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris");
			Directory.CreateDirectory(text);
			string path = Path.Combine(text, "brain_action_audit.log");
			lock (Gate)
			{
				File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
				TrimFileIfNeeded(path);
			}
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError($"[BrainActionAuditLog] {ex.Message}", ex);
		}
	}

	private static void TrimFileIfNeeded(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				string[] array = File.ReadAllLines(path);
				if (array.Length > 400)
				{
					string[] contents = array.Skip(array.Length - 400).ToArray();
					File.WriteAllLines(path, contents, Encoding.UTF8);
				}
			}
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError($"[BrainActionAuditLog] {ex.Message}", ex);
		}
	}
}
