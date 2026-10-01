using System;
using System.Threading;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter
{
	/// <summary>
	/// [BRAIN-PONTE] O ÚLTIMO SNAPSHOT, VISÍVEL PARA O PERFIL.
	///
	/// POR QUE ISTO EXISTE
	/// =====================
	/// O Brain e o Perfil já conversavam, mas apenas num sentido: o Brain pedia
	/// que o perfil se reaplicasse. O Perfil não tinha como dizer de volta o que
	/// ele sabia — e por isso decidia energia olhando só para o TIER, que é uma
	/// característica da MÁQUINA, e nunca para o INSTANTE.
	///
	/// Esta classe é a ponte mínima que fecha essa lacuna. O profiler publica
	/// aqui o snapshot que acabou de coletar; o Perfil lê daqui quando resolve a
	/// linha. Nenhum dos dois conhece o outro: o profiler não sabe que existe
	/// Perfil, e o Perfil não sabe que existe profiler.
	///
	/// POR QUE NÃO É UM SINGLETON DO PROFILER
	/// ======================================
	/// Porque <see cref="AntiStutterProfiler"/> é uma instância criada pelo
	/// container, e <c>ProfilePowerApplier</c> é estático e chamado de vários
	/// lugares, inclusive de caminhos que rodam antes do Brain existir. Fazer o
	/// aplicador procurar a instância do profiler no container inverteria a
	/// dependência: o dono do plano passaria a depender de um componente de
	/// coleta, e bastaria o Brain não estar registrado para a decisão de energia
	/// quebrar num <c>NullReferenceException</c>.
	///
	/// Atestado em arquivo volátil é o contrato certo: a ponte só existe se o
	/// Brain tiver rodado, e a ausência dela é um estado legítimo que o
	/// classificador de regime sabe tratar (regime <c>Unknown</c>, que vale o
	/// piso do Windows).
	///
	/// O QUE NÃO É PERMITIDO AQUI
	/// ==========================
	/// Nenhuma decisão. Nenhuma escrita. Nenhuma leitura de hardware. Esta
		/// classe guarda e devolve — e qualquer coisa que saia do escopo de "guardar
	/// e devolver" é um terceiro dono de energia nascendo dentro da ponte.
	/// </summary>
	public static class AntiStutterSnapshotHub
	{
		/// <summary>
		/// Snapshot mais recente, ou <c>null</c> se o Brain nunca rodou.
		///
		/// Usa <see cref="Volatile"/> porque o valor é escrito por uma thread de
		/// fundo e lido pela thread que aplica o perfil. A referência é lida de
		/// forma atômica de qualquer modo, mas o <c>Volatile</c> documenta a
		/// intenção e impede que o compilador ou o processador reordene a leitura
		/// para dentro de um laço, o que faria o Perfil ver um snapshot velho sem
		/// nenhuma pista de que existe um novo.
		/// </summary>
		private static AntiStutterSnapshot? _latest;

		/// <summary>Quando o snapshot atual foi produzido.</summary>
		private static long _publishedTicks;

		/// <summary>O snapshot mais recente, ou <c>null</c>.</summary>
		public static AntiStutterSnapshot? Latest => Volatile.Read(ref _latest);

		/// <summary>Instante da publicação, em ticks do sistema.</summary>
		public static DateTimeOffset PublishedAt =>
			new DateTimeOffset(new DateTime(Interlocked.Read(ref _publishedTicks), DateTimeKind.Utc));

		/// <summary>
		/// Publica um snapshot novo. Chamado pelo profiler a cada ciclo.
		/// </summary>
		public static void Publish(AntiStutterSnapshot snapshot)
		{
			if (snapshot == null)
			{
				return;
			}

			// A ordem importa: o instante é gravado ANTES do snapshot. Se gravasse
			// depois, um leitor que visse o snapshot novo mas o instante velho
			// concluiria que o dado é mais velho do que é — que é o erro que
			// expira dados bons, e é o pior erro possível numa ponte de
			// segurança.
			Interlocked.Exchange(ref _publishedTicks, DateTime.UtcNow.Ticks);
			Volatile.Write(ref _latest, snapshot);
		}

		/// <summary>
		/// Devolve o snapshot, mas só se for recente o bastante para valer.
		///
		/// Um snapshot velho é pior do que nenhum: ele descreve um instante que
		/// já passou, e uma decisão de energia tomada sobre ele é uma decisão
		/// sobre o passado. Depois do prazo, a resposta é "não olhei", que é a
		/// única resposta honesta.
		/// </summary>
		/// <param name="maxAge">
		/// Idade máxima aceita. O padrão é generoso de propósito: o profiler
		/// roda a cada 5 segundos, então 30 segundos já cobrem três ciclos
		/// perdidos por uma falha temporária sem declarar a máquina desconhecida.
		/// </param>
		public static AntiStutterSnapshot? Current(TimeSpan? maxAge = null)
		{
			AntiStutterSnapshot? snapshot = Latest;
			if (snapshot == null)
			{
				return null;
			}

			TimeSpan limite = maxAge ?? TimeSpan.FromSeconds(30);
			TimeSpan idade = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _publishedTicks), DateTimeKind.Utc);

			return idade <= limite ? snapshot : null;
		}

		/// <summary>Limpa a ponte. Usado no encerramento e nos testes.</summary>
		public static void Clear()
		{
			Volatile.Write(ref _latest, null);
			Interlocked.Exchange(ref _publishedTicks, 0);
		}
	}
}
