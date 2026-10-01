using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoltrisOptimizer.Core.NetworkIntelligence;

public sealed class NetworkDecision
{
    public string Decisao { get; init; } = "idle";

    public string Motivo { get; init; } = string.Empty;

    public List<string> Acoes { get; init; } = new();

    public int Prioridade { get; init; } = 1;

    public bool RequerConfirmacaoUsuario { get; init; }

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public string ToJson()
    {
        return JsonSerializer.Serialize(new
        {
            decisao = Decisao,
            motivo = Motivo,
            acoes = Acoes,
            prioridade = Prioridade,
            requer_confirmacao_usuario = RequerConfirmacaoUsuario,
            timestamp = TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff")
        }, new JsonSerializerOptions {  WriteIndented = true, ReferenceHandler = ReferenceHandler.IgnoreCycles });
    }
}
