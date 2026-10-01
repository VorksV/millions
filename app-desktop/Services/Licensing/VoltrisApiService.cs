using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Text;

namespace VoltrisOptimizer.Services.Licensing
{
    public class VoltrisApiService
    {
        private static VoltrisApiService? _instance;
        public static VoltrisApiService Instance => _instance ??= new VoltrisApiService();

        private readonly HttpClient _httpClient;
        private string? _token;
        private const string ApiBaseUrl = "https://www.voltris.com.br/api";
        private const string TokenStorageKey = "ApiToken";

        private VoltrisApiService()
        {
            _httpClient = new HttpClient();
            LoadToken();
        }

        private void LoadToken()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Voltris\Optimizer", false);
                _token = key?.GetValue(TokenStorageKey)?.ToString();
                if (!string.IsNullOrEmpty(_token))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                }
            }
            catch { }
        }

        private void SaveToken(string token)
        {
            _token = token;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Voltris\Optimizer");
                key.SetValue(TokenStorageKey, token);
            }
            catch { }
        }

        public void Logout()
        {
            _token = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Voltris\Optimizer");
                key.DeleteValue(TokenStorageKey, false);
            }
            catch { }
        }

        public bool IsAuthenticated => !string.IsNullOrEmpty(_token);

        public async Task<(bool success, string? error, string? name)> LoginAsync(string email, string password)
        {
            try
            {
                var json = JsonSerializer.Serialize(new { email, password }, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var response = await _httpClient.PostAsync($"{ApiBaseUrl}/sign-in", new StringContent(json, Encoding.UTF8, "application/json"));
                var content = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(content);
                    var session = doc.RootElement.GetProperty("session");
                    var token = session.GetProperty("access_token").GetString();
                    var user = doc.RootElement.GetProperty("user");
                    var name = user.GetProperty("user_metadata").GetProperty("full_name").GetString();

                    if (!string.IsNullOrEmpty(token))
                    {
                        SaveToken(token);
                        return (true, null, name);
                    }
                }

                // Handle error
                using var errDoc = JsonDocument.Parse(content);
                var error = errDoc.RootElement.TryGetProperty("error", out var errProp) ? errProp.GetString() : "Erro desconhecido ao fazer login";
                return (false, error, null);
            }
            catch (Exception ex)
            {
                return (false, $"Erro de conexão: {ex.Message}", null);
            }
        }

        public async Task<List<LicenseInfoResponse>> GetMyLicensesAsync()
        {
            if (!IsAuthenticated) return new List<LicenseInfoResponse>();

            try
            {
                var response = await _httpClient.GetAsync($"{ApiBaseUrl}/v1/license/me");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<MyLicensesRoot>(content, new JsonSerializerOptions {   PropertyNameCaseInsensitive = true });
                    return data?.Licenses ?? new List<LicenseInfoResponse>();
                }
            }
            catch { }
            return new List<LicenseInfoResponse>();
        }

        public async Task<(bool success, string message)> ActivateDeviceAsync(string licenseKey)
        {
            if (!IsAuthenticated) return (false, "Você precisa estar logado para ativar.");

            try
            {
                var hwid = await VoltrisOptimizer.Services.License.HardwareTrialService.Instance.GetHwidAsync();
                var components = await VoltrisOptimizer.Services.License.HardwareTrialService.Instance.GetHardwareComponentsAsync();
                var machineName = Environment.MachineName;

                var json = JsonSerializer.Serialize(new
                {
                    license_key = licenseKey,
                    hwid = hwid,
                    components = JsonSerializer.Serialize(components, VoltrisOptimizer.App.GlobalJsonOptions),
                    device_name = machineName,
                    machine_name = machineName,
                    os_version = Environment.OSVersion.ToString()
                });
                var response = await _httpClient.PostAsync($"{ApiBaseUrl}/v1/license/activate", new StringContent(json, Encoding.UTF8, "application/json"));

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                
                if (response.IsSuccessStatusCode)
                {
                    return (true, "Dispositivo ativado com sucesso!");
                }
                
                var error = doc.RootElement.TryGetProperty("errorMessage", out var errProp) ? errProp.GetString() : "Não foi possível ativar este dispositivo.";
                return (false, error ?? "Erro desconhecido");
            }
            catch (Exception ex)
            {
                return (false, $"Erro de conexão: {ex.Message}");
            }
        }
    }

    public class MyLicensesRoot
    {
        public List<LicenseInfoResponse> Licenses { get; set; } = new();
    }

    public class LicenseInfoResponse
    {
        public string Id { get; set; } = "";
        public string Key { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsActive { get; set; }
        public bool IsExpired { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public int MaxDevices { get; set; }
        public int DevicesInUse { get; set; }
    }
}
