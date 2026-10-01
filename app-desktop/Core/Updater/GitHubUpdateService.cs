//REMOVIDO: using System.Web;

 using System;

 using System.Diagnostics;

 using System.IO;

 using System.Linq;

 using System.Net;

 using System.Net.Http;

 using System.Reflection;

 using System.Text.Json;

 using System.Text.Json.Serialization;

 using System.Threading;

 using System.Threading.Tasks;

 using VoltrisOptimizer.Helpers;
 using VoltrisOptimizer.Services;

 namespace VoltrisOptimizer.Core.Updater 
{
public sealed class GitHubUpdateService: IDisposable, IAsyncDisposable 
{
private static readonly string[]REPO_OWNERS = {
"DougFHansen"};

 private static readonly string[]REPO_NAMES = {
"voltris - releases"};

 private static readonly string[]VERSION_JSON_URLS = {
"https://raw.githubusercontent.com/DougFHansen/voltris - releases/main/version.json"};

 public static readonly TimeSpan CHECK_INTERVAL_BACKGROUND = TimeSpan.FromMinutes(30);

 private readonly HttpClient _httpClient;

 private readonly ILoggingService?_logger;

 private readonly SemaphoreSlim _semaphore = new(1, 1);

 private readonly object _cacheLock = new();

 private CancellationTokenSource?_pollingCts;

 private Task?_pollingTask;

 private DateTime _lastCheckTime = DateTime.MinValue;

 private GitHubUpdateInfo?_cachedInfo;

 private DateTime _cacheTime = DateTime.MinValue;

 private string?_lastNotifiedVersion;

 private readonly string _stateFilePath;

 public event EventHandler < UpdateCheckEventArgs >?UpdateAvailable;

 public event EventHandler < UpdateCheckCompletedEventArgs >?CheckCompleted;

 public GitHubUpdateService(ILoggingService?logger = null) {
_logger = logger;

 var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

 var folder = Path.Combine(appData,"VoltrisOptimizer");

 Directory.CreateDirectory(folder);

 _stateFilePath = Path.Combine(folder,"update_state.json");

 LoadState();

 var handler = new HttpClientHandler {
AutomaticDecompression = DecompressionMethods.GZip|DecompressionMethods.Deflate, AllowAutoRedirect = true};

 _httpClient = new HttpClient(handler) {
Timeout = TimeSpan.FromSeconds(30)};

            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"Voltris/{GetCurrentVersion()}");

            LogInfo($"Inicializado versão {GetCurrentVersion()}");

        }

        // ==================== STATE ====================
        private void LoadState()
        {
try {
if(!File.Exists(_stateFilePath))return;

 var json = File.ReadAllText(_stateFilePath);

 var state = JsonSerializer.Deserialize<UpdateState>(json, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

 _lastNotifiedVersion = state?.Version;

 }
        catch (Exception ex)
        {
            LogWarning($"Erro ao carregar state: {ex.Message}");

 }
 }
 private void SaveState(string version) {
try {
var json = JsonSerializer.Serialize(new UpdateState {
Version = version, Timestamp = DateTime.UtcNow}, new JsonSerializerOptions {  PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

 File.WriteAllText(_stateFilePath, json);

        }
        catch (Exception ex)
        {
            LogWarning($"Erro ao salvar state: {ex.Message}");
        }
    }

    // ==================== MAIN ====================
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool force = false, CancellationToken ct = default)
    {
GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("UpdateGithubChecking"), true);

await _semaphore.WaitAsync(ct).ConfigureAwait(false);

 try {
if(!force&&DateTime.UtcNow - _lastCheckTime < TimeSpan.FromSeconds(10)) {
GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("UpdateCacheVerified"));
return new UpdateCheckResult {
Status = UpdateCheckStatus.Skipped};
}

 _lastCheckTime = DateTime.UtcNow;

 var current = GetCurrentVersion();

GlobalProgressService.Instance.UpdateProgress(50, "Consultando repositório...");
 var remote = await FetchLatestVersion(ct).ConfigureAwait(false);

            if (remote == null) {
                GlobalProgressService.Instance.FailOperation(LocalizationService.Instance.GetString("UpdateRepoQueryFailed"));
                return new UpdateCheckResult {
Status = UpdateCheckStatus.Failed};
            }

 var cmp = CompareVersions(remote.Version, current);

 if(cmp > 0&&_lastNotifiedVersion!= remote.Version) {
_lastNotifiedVersion = remote.Version;

 SaveState(remote.Version);

 var result = new UpdateCheckResult {
Status = UpdateCheckStatus.UpdateAvailable, UpdateInfo = remote, CurrentVersion = current};

  UpdateAvailable?.Invoke(this, new UpdateCheckEventArgs(result));

GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("UpdateNewVersionFound"));
  return result;

  }
GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("UpdateNoneFound"));
  return new UpdateCheckResult {
Status = UpdateCheckStatus.NoUpdate, UpdateInfo = remote, CurrentVersion = current};

 }
 finally {
_semaphore.Release();

 }
        }

    // ==================== FETCH ====================
    private async Task<GitHubUpdateInfo?> FetchLatestVersion(CancellationToken ct)
    {
        lock (_cacheLock)
        {
            if (_cachedInfo != null && DateTime.UtcNow - _cacheTime < TimeSpan.FromMinutes(1))
                return _cachedInfo;
        }

        foreach (var i in Enumerable.Range(0, REPO_OWNERS.Length))
        {
            var result = await TryFetchRepo(REPO_OWNERS[i], REPO_NAMES[i], ct).ConfigureAwait(false);

            if (result != null)
            {
                lock (_cacheLock)
                {
                    _cachedInfo = result;
                    _cacheTime = DateTime.UtcNow;
                }
                return result;
            }
        }

        return null;
    }

    private async Task<GitHubUpdateInfo?> TryFetchRepo(string owner, string repo, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest?_={DateTime.UtcNow.Ticks}";

 var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);

 if(!response.IsSuccessStatusCode)return null;

 var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

 using var doc = JsonDocument.Parse(json);

 if(!doc.RootElement.TryGetProperty("tag_name", out var tagProp))return null;

 var version = tagProp.GetString()?.TrimStart('v')??"0.0.0";

            return new GitHubUpdateInfo
            {
                Version = version,
                DownloadUrl = $"https://github.com/{owner}/{repo}/releases/latest",
                ReleaseDate = DateTime.UtcNow,
                Source = $"{owner}/{repo}"
            };
        }
        catch
        {
            return null;
        }
    }

    // ==================== UTILS ====================
    public static string GetCurrentVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    }

    private static int CompareVersions(string v1, string v2)
    {
        return new Version(v1).CompareTo(new Version(v2));
    }

    // ==================== LOG ====================
    private void LogInfo(string msg) => _logger?.LogInfo(msg);

    private void LogWarning(string msg) => _logger?.LogWarning(msg);

    // ==================== DISPOSE ====================
    public async ValueTask DisposeAsync()
    {
if(_pollingCts!= null) {
_pollingCts.Cancel();

 _pollingCts.Dispose();

 }
 if(_pollingTask!= null) {
try {
await _pollingTask.ConfigureAwait(false);

 }
 catch {
}
 }
 _httpClient.Dispose();

    }

    public void Dispose()
    {
        // 🔥 CORREÇÃO: Nunca usar GetResult() em Dispose se ele puder ser chamado da UI Thread.
        // Como implementamos IAsyncDisposable, o container deve preferir DisposeAsync.
        // Para o Dispose(void) legado, tentamos uma limpeza síncrona mínima.
        try
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _httpClient?.Dispose();
            _semaphore?.Dispose();
        }
        catch { }
    }

    private class UpdateState
    {
        public string Version { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}

public class GitHubUpdateInfo
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public DateTime ReleaseDate { get; set; }
    public string Source { get; set; } = "";
}

public enum UpdateCheckStatus
{
    UpdateAvailable,
    NoUpdate,
    Failed,
    Skipped
}

public class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; set; }
    public GitHubUpdateInfo? UpdateInfo { get; set; }
    public string CurrentVersion { get; set; } = "";

    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable;
}

public class UpdateCheckEventArgs : EventArgs
{
    public UpdateCheckResult Result { get; }
    public UpdateCheckEventArgs(UpdateCheckResult r) => Result = r;
}

public class UpdateCheckCompletedEventArgs : EventArgs
{
    public UpdateCheckResult Result { get; }
    public UpdateCheckCompletedEventArgs(UpdateCheckResult r) => Result = r;
}
}
