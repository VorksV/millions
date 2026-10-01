namespace VoltrisOptimizer.Core.Body;

public enum ProcessPriorityClass
{
	Normal = 32,
	AboveNormal = 32768,
	High = 128,
	RealTime = 256
}

/// <summary>
/// Converte entre o enum do VOLTRIS e o enum do .NET.
///
/// POR QUE ISTO EXISTE (bug corrigido):
/// <see cref="ProcessPriorityClass"/> usa os flags NATIVOS do Win32
/// (NORMAL_PRIORITY_CLASS=32, ABOVE_NORMAL=32768, HIGH=128, REALTIME=256),
/// enquanto <c>System.Diagnostics.ProcessPriorityClass</c> e um enum
/// SEQUENCIAL (0..5). Casts cegos entre os dois produziam valores invalidos:
///   (System.Diagnostics.ProcessPriorityClass)(int)VoltrisPriorityClass.AboveNormal
///   => (ProcessPriorityClass)32768  => InvalidEnumArgumentException
/// O log da aplicacao mostrava isso repetidamente:
///   "[ARM-PROCESS] ERRO em SetProcessPriorityAsync: The value of argument
///    'value' (3) is invalid for Enum type 'ProcessPriorityClass'".
/// O caminho de retorno sofria do mesmo problema: ler o valor do .NET (0..5) e
/// converter de volta produzia flags sem sentido (por exemplo 2 => o flag
/// incorreto), corrompendo o snapshot usado no rollback.
/// </summary>
public static class ProcessPriorityClassMap
{
	/// <summary>Enum do Voltris (flags nativos) para o enum do .NET.</summary>
	public static System.Diagnostics.ProcessPriorityClass ToWindows(ProcessPriorityClass priority)
		=> priority switch
		{
			ProcessPriorityClass.AboveNormal => System.Diagnostics.ProcessPriorityClass.AboveNormal,
			ProcessPriorityClass.High => System.Diagnostics.ProcessPriorityClass.High,
			ProcessPriorityClass.RealTime => System.Diagnostics.ProcessPriorityClass.RealTime,
			ProcessPriorityClass.Normal => System.Diagnostics.ProcessPriorityClass.Normal,
			// Valor indefinido (ex.: BrainActionExecutorV2 fazia cast de um int
			// arbitrario para este enum). Antes isso virava excecao na hora de
			// aplicar; agora e normalizado para Normal, que e sempre aceito.
			_ => System.Diagnostics.ProcessPriorityClass.Normal
		};

	/// <summary>Enum do .NET para o enum do Voltris (flags nativos).</summary>
	public static ProcessPriorityClass FromWindows(System.Diagnostics.ProcessPriorityClass priority)
		=> priority switch
		{
			System.Diagnostics.ProcessPriorityClass.AboveNormal => ProcessPriorityClass.AboveNormal,
			System.Diagnostics.ProcessPriorityClass.High => ProcessPriorityClass.High,
			System.Diagnostics.ProcessPriorityClass.RealTime => ProcessPriorityClass.RealTime,
			// BelowNormal e Idle nao tem equivalente no enum do Voltris; o mais
			// proximo eNormal, e o que o proprio codigo ja assumia.
			_ => ProcessPriorityClass.Normal
		};

	/// <summary>
	/// Converte um valor inteiro bruto (flags nativos) para o enum do Voltris.
	/// Usado onde a origem e um int, evitando cast cego.
	/// </summary>
	public static ProcessPriorityClass FromNativeFlags(int nativeFlags)
		=> nativeFlags switch
		{
			32768 => ProcessPriorityClass.AboveNormal,
			128 => ProcessPriorityClass.High,
			256 => ProcessPriorityClass.RealTime,
			_ => ProcessPriorityClass.Normal
		};

	/// <summary>
	/// Converte o ORDINAL de <c>ProcessPriorityChoice</c> (Normal=0,
	/// AboveNormal=1, High=2) para o enum do Voltris.
	///
	/// BUG CORRIGIDO: <c>BrainActionExecutorV2</c> fazia
	/// <c>(ProcessPriorityClass)action.Param</c>, tratando o ordinal como se
	/// fosse o flag nativo. O dominio e completamente diferente, entao o valor
	/// chegava ao ProcessArm sem sentido — com Param=3 (que nem existe em
	/// ProcessPriorityChoice) o .NET recusava e a acao era perdida com
	/// InvalidEnumArgumentException.
	/// </summary>
	public static ProcessPriorityClass FromChoiceOrdinal(int ordinal)
		=> ordinal switch
		{
			1 => ProcessPriorityClass.AboveNormal,
			2 => ProcessPriorityClass.High,
			_ => ProcessPriorityClass.Normal
		};
}
