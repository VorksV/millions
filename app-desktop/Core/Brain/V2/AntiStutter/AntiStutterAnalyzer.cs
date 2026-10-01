using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class AntiStutterAnalyzer
{
	private const double CPU_SINGLE_CORE_THRESHOLD = 85.0;

	private const double CPU_MULTI_CORE_THRESHOLD = 90.0;

	private const double RAM_PRESSURE_THRESHOLD = 85.0;

	private const double DISK_QUEUE_THRESHOLD = 2.0;

	public BottleneckAnalysis Analyze(AntiStutterSnapshot snap)
	{
		if (snap == null)
		{
			return new BottleneckAnalysis
			{
				HasBottleneck = false
			};
		}
		List<BottleneckDetail> list = new List<BottleneckDetail>();
		if (snap.CpuMaxCoreUsage > 85.0 && snap.CpuUsageTotal < 70.0)
		{
			list.Add(Create(BottleneckType.CpuSingleCore, snap.CpuMaxCoreUsage, 85.0, "CPU single-core saturation"));
		}
		if (snap.CpuUsageTotal > 90.0)
		{
			list.Add(Create(BottleneckType.CpuMultiCore, snap.CpuUsageTotal, 90.0, "CPU full saturation"));
		}
		if (snap.RamUsagePercent > 85.0 || snap.PageFileUsageMB > 2048)
		{
			list.Add(Create(BottleneckType.RamPressure, snap.RamUsagePercent, 85.0, "Memory pressure/paging"));
		}
		if (snap.DiskQueueLength > 2.0)
		{
			list.Add(Create(snap.DiskIsSsd ? BottleneckType.DiskSaturated : BottleneckType.DiskHdd, snap.DiskQueueLength, 2.0, "Disk I/O congestáion"));
		}
		if (!snap.IsHighPerformance && snap.IsGame)
		{
			list.Add(new BottleneckDetail
			{
				Type = BottleneckType.PowerPlan,
				Severity = 0.6,
				Metric = "Power Plan",
				Value = 0.0,
				Description = "Non-optimal power plan for gaming"
			});
		}
		List<BottleneckDetail> list2 = list.OrderByDescending((BottleneckDetail d) => d.Severity).ToList();
		BottleneckType bottleneckType = list2.FirstOrDefault()?.Type ?? BottleneckType.None;
		BottleneckType secondaryBottleneck = list2.Skip(1).FirstOrDefault()?.Type ?? BottleneckType.None;
		double num = ((list2.Count > 0) ? list2.Take(3).Average((BottleneckDetail d) => d.Severity) : 0.0);
		return new BottleneckAnalysis
		{
			HasBottleneck = (num > 0.4),
			PrimaryBottleneck = bottleneckType,
			SecondaryBottleneck = secondaryBottleneck,
			SeverityScore = num,
			Details = list2,
			Diagnosis = GenerateDiagnosis(bottleneckType, snap),
			Recommendation = GenerateRecommendation(bottleneckType)
		};
	}

	private static BottleneckDetail Create(BottleneckType type, double value, double threshold, string description)
	{
		double severity = Math.Clamp((value - threshold) / threshold, 0.0, 1.0);
		return new BottleneckDetail
		{
			Type = type,
			Severity = severity,
			Metric = description,
			Value = value,
			Description = description
		};
	}

	private string GenerateDiagnosis(BottleneckType type, AntiStutterSnapshot snap)
	{

		string result;
		switch (type)
		{
		case BottleneckType.CpuSingleCore:
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(21, 1);
			defaultInterpolatedStringHandler.AppendLiteral("CPU core saturado (");
			defaultInterpolatedStringHandler.AppendFormatted(snap.CpuMaxCoreUsage, "F0");
			defaultInterpolatedStringHandler.AppendLiteral("%)");
			result = defaultInterpolatedStringHandler.ToStringAndClear();
			break;
		}
		case BottleneckType.CpuMultiCore:
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(27, 1);
			defaultInterpolatedStringHandler.AppendLiteral("CPU totalmente saturada (");
			defaultInterpolatedStringHandler.AppendFormatted(snap.CpuUsageTotal, "F0");
			defaultInterpolatedStringHandler.AppendLiteral("%)");
			result = defaultInterpolatedStringHandler.ToStringAndClear();
			break;
		}
		case BottleneckType.RamPressure:
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(23, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Pressão de memória (");
			defaultInterpolatedStringHandler.AppendFormatted(snap.RamUsagePercent, "F0");
			defaultInterpolatedStringHandler.AppendLiteral("%)");
			result = defaultInterpolatedStringHandler.ToStringAndClear();
			break;
		}
		case BottleneckType.DiskHdd:
		case BottleneckType.DiskSaturated:
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(29, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Disco congestionado (fila: ");
			defaultInterpolatedStringHandler.AppendFormatted(snap.DiskQueueLength, "F1");
			defaultInterpolatedStringHandler.AppendLiteral(")");
			result = defaultInterpolatedStringHandler.ToStringAndClear();
			break;
		}
		case BottleneckType.PowerPlan:
			result = "Plano de energia não otimizado";
			break;
		default:
			result = "Sistema estável";
			break;
		}

		return result;
	}

	private string GenerateRecommendation(BottleneckType type)
	{

		string result = type switch
		{
			BottleneckType.CpuSingleCore => "Ajustar prioridade do processão e plano de energia", 
			BottleneckType.CpuMultiCore => "Reduzir processãos em background", 
			BottleneckType.RamPressure => "Fechar aplicações não essenciais", 
			BottleneckType.DiskHdd => "Mover aplicação para SSD", 
			BottleneckType.DiskSaturated => "Aguardar operações de I/O ou reduzir carga", 
			BottleneckType.PowerPlan => "Ativar modo de alto desempenho", 
			_ => "Nenhuma ação necessária"};

		return result;
	}
}
