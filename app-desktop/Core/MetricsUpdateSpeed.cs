namespace VoltrisOptimizer.Core;

public enum MetricsUpdateSpeed
{
    /// <summary>Modo mínimo: apenas heartbeat essencial. Usado quando app está no tray ou minimizada.</summary>
    Minimal = 10000,
    /// <summary>Modo reduzido: atualiza menos frequentemente. Usado quando app está em background.</summary>
    Reduced = 5000,
    /// <summary>Modo lento: ideal para monitoramento passivo.</summary>
    Low = 4000,
    /// <summary>Modo normal: equilíbrio entre precisão e consumo.</summary>
    Normal = 1000,
    /// <summary>Modo rápido: uso durante execução de tarefas ou modo gamer.</summary>
    High = 500
}
