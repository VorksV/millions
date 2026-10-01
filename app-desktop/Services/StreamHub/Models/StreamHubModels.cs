using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.StreamHub.Models
{
    // ─── PLATAFORMAS ────────────────────────────────────────────────────────────
    public enum StreamPlatform
    {
        Twitch,
        YouTube,
        TikTok,
        Facebook,
        OBS,
        Unknown
    }

    public enum StreamStatus
    {
        Offline,
        Online,
        Connecting,
        Error,
        Reconnecting
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical
    }

    public enum AlertType
    {
        BitrateDropped,
        DroppedFrames,
        MicrophoneMuted,
        StreamFrozen,
        HighCpuUsage,
        HighRamUsage,
        NetworkInstability,
        ChatSpike,
        PlatformDisconnected,
        EngagementLow
    }

    public enum HighlightTrigger
    {
        ChatSpike,
        AudioVolumePeak,
        FollowEvent,
        DonationEvent,
        SubscriptionEvent,
        ManualMark
    }

    // ─── MÉTRICAS DA STREAM ──────────────────────────────────────────────────────
    public class StreamMetrics
    {
        public bool IsLive { get; set; }
        public int BitrateKbps { get; set; }
        public int DroppedFrames { get; set; }
        public double DroppedFramePercent { get; set; }
        public int Fps { get; set; }
        public int TargetFps { get; set; }
        public double CpuUsagePercent { get; set; }
        public double RamUsageMb { get; set; }
        public double NetworkBandwidthMbps { get; set; }
        public bool IsMicrophoneActive { get; set; }
        public string ActiveScene { get; set; } = string.Empty;
        public TimeSpan StreamDuration { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        // Saúde geral (0-100)
        public int HealthScore
        {
            get
            {
                int score = 100;
                if (DroppedFramePercent > 5) score -= 30;
                else if (DroppedFramePercent > 1) score -= 10;
                if (BitrateKbps < 1000 && IsLive) score -= 20;
                if (CpuUsagePercent > 90) score -= 20;
                if (!IsMicrophoneActive && IsLive) score -= 15;
                return Math.Max(0, score);
            }
        }
    }

    // ─── STATUS DE PLATAFORMA ────────────────────────────────────────────────────
    public class PlatformStatus
    {
        public StreamPlatform Platform { get; set; }
        public StreamStatus Status { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public int ViewerCount { get; set; }
        public string StreamTitle { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public int ReconnectAttempts { get; set; }
        public string PlatformColor { get; set; } = "#FFFFFF";
    }

    // ─── MENSAGEM DE CHAT ────────────────────────────────────────────────────────
    public class ChatMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public StreamPlatform Platform { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsHighlighted { get; set; }
        public bool IsFrequentUser { get; set; }
        public bool IsModerator { get; set; }
        public bool IsSubscriber { get; set; }
        public string? BadgeEmoji { get; set; }
        public string PlatformColor { get; set; } = "#FFFFFF";
        public string PlatformIcon { get; set; } = string.Empty;

        // Cor do username baseada na plataforma
        public string UsernameColor => Platform switch
        {
            StreamPlatform.Twitch => "#9146FF",
            StreamPlatform.YouTube => "#FF0000",
            StreamPlatform.TikTok => "#00F2EA",
            StreamPlatform.Facebook => "#1877F2",
            _ => "#FFFFFF"
        };
    }

    // ─── ALERTA INTELIGENTE ──────────────────────────────────────────────────────
    public class StreamAlert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public AlertType Type { get; set; }
        public AlertSeverity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsAcknowledged { get; set; }
        public string? SuggestedAction { get; set; }

        public string SeverityColor => Severity switch
        {
            AlertSeverity.Critical => "#FF4466",
            AlertSeverity.Warning => "#FFAA00",
            AlertSeverity.Info => "#00D4FF",
            _ => "#FFFFFF"
        };

        public string SeverityIcon => Severity switch
        {
            AlertSeverity.Critical => "⛔",
            AlertSeverity.Warning => "⚠️",
            AlertSeverity.Info => "ℹ️",
            _ => "•"
        };
    }

    // ─── MOMENTO DE DESTAQUE ─────────────────────────────────────────────────────
    public class HighlightMoment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public TimeSpan StreamTimestamp { get; set; }
        public HighlightTrigger Trigger { get; set; }
        public string Description { get; set; } = string.Empty;
        public int Intensity { get; set; } // 1-10
        public bool IsExported { get; set; }

        public string TriggerIcon => Trigger switch
        {
            HighlightTrigger.ChatSpike => "💬",
            HighlightTrigger.AudioVolumePeak => "🔊",
            HighlightTrigger.FollowEvent => "❤️",
            HighlightTrigger.DonationEvent => "💰",
            HighlightTrigger.SubscriptionEvent => "⭐",
            HighlightTrigger.ManualMark => "📌",
            _ => "✨"
        };
    }

    // ─── SUGESTÃO DE ENGAJAMENTO ─────────────────────────────────────────────────
    public class EngagementSuggestion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Icon { get; set; } = "💡";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsActioned { get; set; }
        public int Priority { get; set; } // 1-5
    }

    // ─── CONFIGURAÇÕES DO STREAM HUB ─────────────────────────────────────────────
    public class StreamHubSettings
    {
        // OBS
        public string ObsWebSocketUrl { get; set; } = "ws://localhost:4455";
        public string ObsWebSocketPassword { get; set; } = string.Empty;
        public bool ObsAutoConnect { get; set; } = true;

        // Twitch
        public string TwitchChannelName { get; set; } = string.Empty;
        public string TwitchOAuthToken { get; set; } = string.Empty;
        public string TwitchClientId { get; set; } = string.Empty;

        // YouTube
        public string YouTubeApiKey { get; set; } = string.Empty;
        public string YouTubeLiveChatId { get; set; } = string.Empty;

        // TikTok
        public string TikTokUsername { get; set; } = string.Empty;

        // Facebook
        public string FacebookPageId { get; set; } = string.Empty;
        public string FacebookAccessToken { get; set; } = string.Empty;

        // Alertas
        public int BitrateDropThresholdKbps { get; set; } = 2000;
        public double DroppedFrameAlertPercent { get; set; } = 2.0;
        public double CpuAlertPercent { get; set; } = 85.0;

        // Highlight Detection
        public int ChatSpikeThreshold { get; set; } = 10; // msgs em 5s
        public bool AutoDetectHighlights { get; set; } = true;

        // Engagement
        public int ChatIdleAlertSeconds { get; set; } = 120;
        public bool EnableEngagementAssistant { get; set; } = true;
    }

    // ─── INSIGHTS DE CRESCIMENTO ─────────────────────────────────────────────────
    public class StreamInsight
    {
        public string Title { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Trend { get; set; } = string.Empty; // "↑", "↓", "→"
        public string TrendColor { get; set; } = "#FFFFFF";
        public string Icon { get; set; } = "📊";
    }

    // ─── SESSÃO DE STREAM ────────────────────────────────────────────────────────
    public class StreamSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public int PeakViewers { get; set; }
        public int TotalMessages { get; set; }
        public int TotalFollows { get; set; }
        public int TotalDonations { get; set; }
        public List<HighlightMoment> Highlights { get; set; } = new();
        public double AverageBitrateKbps { get; set; }
        public double AverageDroppedFramePercent { get; set; }
        public TimeSpan Duration => (EndedAt ?? DateTime.Now) - StartedAt;
    }
}
