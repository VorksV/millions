using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.StreamHub.Models;

namespace VoltrisOptimizer.Services.StreamHub.Interfaces
{
    // ─── OBS WEBSOCKET ───────────────────────────────────────────────────────────
    public interface IObsService
    {
        bool IsConnected { get; }
        event EventHandler<StreamMetrics>? MetricsUpdated;
        event EventHandler<string>? SceneChanged;
        event EventHandler<bool>? StreamStatusChanged;
        event EventHandler<bool>? MicrophoneStatusChanged;

        Task<bool> ConnectAsync(string url, string password, CancellationToken ct = default);
        Task DisconnectAsync();
        Task<StreamMetrics> GetCurrentMetricsAsync();
        Task<bool> SetSceneAsync(string sceneName);
        Task<List<string>> GetScenesAsync();
        Task<bool> ToggleMuteAsync(string sourceName);
        Task StartMonitoringAsync(CancellationToken ct);
    }

    // ─── TWITCH ──────────────────────────────────────────────────────────────────
    public interface ITwitchService
    {
        bool IsConnected { get; }
        event EventHandler<ChatMessage>? MessageReceived;
        event EventHandler<string>? FollowReceived;
        event EventHandler<string>? SubscriptionReceived;
        event EventHandler<(string user, int amount)>? DonationReceived;
        event EventHandler<PlatformStatus>? StatusChanged;

        Task<bool> ConnectAsync(string channel, string oauthToken, CancellationToken ct = default);
        Task DisconnectAsync();
        Task<PlatformStatus> GetStatusAsync();
        Task SendMessageAsync(string message);
    }

    // ─── YOUTUBE ─────────────────────────────────────────────────────────────────
    public interface IYouTubeService
    {
        bool IsConnected { get; }
        event EventHandler<ChatMessage>? MessageReceived;
        event EventHandler<PlatformStatus>? StatusChanged;

        Task<bool> ConnectAsync(string apiKey, string liveChatId, CancellationToken ct = default);
        Task DisconnectAsync();
        Task<PlatformStatus> GetStatusAsync();
    }

    // ─── CHAT AGGREGATOR ─────────────────────────────────────────────────────────
    public interface IChatAggregatorService
    {
        event EventHandler<ChatMessage>? MessageReceived;
        event EventHandler<int>? ChatActivityChanged; // msgs/min

        int MessagesPerMinute { get; }
        int TotalMessages { get; }
        IReadOnlyList<string> FrequentUsers { get; }

        void RegisterPlatform(StreamPlatform platform, IObservable<ChatMessage>? source = null);
        void InjectMessage(ChatMessage message);
        void Reset();
    }

    // ─── STREAM HEALTH MONITOR ───────────────────────────────────────────────────
    public interface IStreamHealthMonitor
    {
        event EventHandler<StreamAlert>? AlertRaised;
        event EventHandler<StreamAlert>? AlertResolved;

        IReadOnlyList<StreamAlert> ActiveAlerts { get; }
        int HealthScore { get; }

        void UpdateMetrics(StreamMetrics metrics);
        void AcknowledgeAlert(string alertId);
        void ClearAll();
    }

    // ─── HIGHLIGHT DETECTOR ──────────────────────────────────────────────────────
    public interface IHighlightDetectorService
    {
        event EventHandler<HighlightMoment>? HighlightDetected;

        IReadOnlyList<HighlightMoment> Highlights { get; }

        void OnChatActivity(int messagesInWindow);
        void OnPlatformEvent(HighlightTrigger trigger, string description);
        void MarkManualHighlight(TimeSpan streamTimestamp, string description = "Marcação manual");
        void SetStreamStartTime(DateTime startTime);
    }

    // ─── ENGAGEMENT ASSISTANT ────────────────────────────────────────────────────
    public interface IEngagementAssistantService
    {
        event EventHandler<EngagementSuggestion>? SuggestionGenerated;

        IReadOnlyList<EngagementSuggestion> ActiveSuggestions { get; }

        void OnChatMessage(ChatMessage message);
        void OnChatIdle(TimeSpan idleDuration);
        void OnViewerCountChange(int previous, int current);
        void DismissSuggestion(string id);
        void Reset();
    }

    // ─── STREAM HUB ORCHESTRATOR ─────────────────────────────────────────────────
    public interface IStreamHubService
    {
        bool IsRunning { get; }
        StreamHubSettings Settings { get; }
        StreamMetrics CurrentMetrics { get; }
        IReadOnlyList<PlatformStatus> PlatformStatuses { get; }

        event EventHandler<StreamMetrics>? MetricsUpdated;
        event EventHandler<ChatMessage>? ChatMessageReceived;
        event EventHandler<StreamAlert>? AlertRaised;
        event EventHandler<HighlightMoment>? HighlightDetected;
        event EventHandler<EngagementSuggestion>? SuggestionGenerated;
        event EventHandler<PlatformStatus>? PlatformStatusChanged;

        Task StartAsync(StreamHubSettings settings, CancellationToken ct = default);
        Task StopAsync();
        Task<bool> ConnectObsAsync();
        Task<bool> ConnectTwitchAsync();
        Task<bool> ConnectYouTubeAsync();
        void MarkHighlight(string description = "Marcação manual");
        Task<bool> SwitchObsSceneAsync(string sceneName);
        Task<List<string>> GetObsScenesAsync();
        void SaveSettings(StreamHubSettings settings);
        StreamHubSettings LoadSettings();
    }
}
