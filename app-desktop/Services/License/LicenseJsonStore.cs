using System;
using System.IO;
using System.Text.Json;

namespace VoltrisOptimizer.Services.License
{
    /// <summary>
    /// Armazena dados da licença em JSON em %LOCALAPPDATA%\Voltris\license.json
    /// para garantir persistência entre atualizações do programa.
    /// </summary>
    internal static class LicenseJsonStore
    {
        private static readonly string FilePath;
        private static readonly object _lock = new();

        static LicenseJsonStore()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Voltris");
            if (!Directory.Exists(appData))
                Directory.CreateDirectory(appData);
            FilePath = Path.Combine(appData, "license.json");
        }

        public static void Save(string licenseType, string licenseDisplayName, string licenseKey, DateTime expiresAt, string billingPeriod = "year")
        {
            try
            {
                var data = new LicenseJsonData
                {
                    LicenseType = licenseType,
                    LicenseDisplayName = licenseDisplayName,
                    // A chave NÃO é gravada em claro. O campo LicenseKey fica vazio
                    // e o segredo real vai para LicenseKeyEnc, protegido com DPAPI
                    // (CurrentUser): só a mesma conta do Windows nesta máquina abre.
                    // Sem isso, license.json era um bearer token copiável — bastava
                    // abrir o arquivo para reutilizar a licença em outro PC.
                    LicenseKey = "",
                    LicenseKeyEnc = VoltrisOptimizer.Core.Security.SecureConfigProtection.ProtectSecret(licenseKey) ?? "",
                    ExpiresAt = expiresAt,
                    BillingPeriod = billingPeriod ?? "year",
                    SavedAt = DateTime.UtcNow
                };

                lock (_lock)
                {
                    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = true });
                    File.WriteAllText(FilePath, json);
                }

                App.LoggingService?.LogInfo($"[LicenseJsonStore] Licença salva (chave protegida via DPAPI): {FilePath}");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseJsonStore] Erro ao salvar: {ex.Message}");
            }
        }

        public static (string? licenseType, string? displayName, string? licenseKey, DateTime? expiresAt, string? billingPeriod) Load()
        {
            try
            {
                lock (_lock)
                {
                    if (!File.Exists(FilePath))
                    {
                        // Migração: copiar do registro para JSON (primeira execução)
                        MigrateFromRegistry();
                        if (!File.Exists(FilePath))
                            return (null, null, null, null, null);
                    }

                    var json = File.ReadAllText(FilePath);
                    var data = JsonSerializer.Deserialize<LicenseJsonData>(json);
                    if (data == null) return (null, null, null, null, null);

                    // resolving a chave: tenta o campo criptografado; se o arquivo
                    // veio de uma versão anterior (chave em claro), usa o campo
                    // antigo e regrava já protegido — migração transparente.
                    string? licenseKey = null;
                    if (!string.IsNullOrEmpty(data.LicenseKeyEnc))
                    {
                        licenseKey = VoltrisOptimizer.Core.Security.SecureConfigProtection.UnprotectSecret(data.LicenseKeyEnc);
                        if (licenseKey is null)
                        {
                            App.LoggingService?.LogError("[LicenseJsonStore] Chave DPAPI não pôde ser lida (DPAPI indisponível neste perfil).");
                        }
                    }
                    else if (!string.IsNullOrEmpty(data.LicenseKey))
                    {
                        licenseKey = data.LicenseKey;
                        App.LoggingService?.LogInfo("[LicenseJsonStore] Migrando chave de texto claro para DPAPI...");
                        Save(data.LicenseType, data.LicenseDisplayName, licenseKey, data.ExpiresAt, data.BillingPeriod);
                    }

                    App.LoggingService?.LogInfo($"[LicenseJsonStore] Licença carregada: type={data.LicenseType}, expires={data.ExpiresAt:yyyy-MM-dd}, billing={data.BillingPeriod}");

                    return (data.LicenseType, data.LicenseDisplayName, licenseKey, data.ExpiresAt, data.BillingPeriod);
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseJsonStore] Erro ao carregar: {ex.Message}");
                return (null, null, null, null, null);
            }
        }

        public static void Clear()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(FilePath))
                        File.Delete(FilePath);
                }
                App.LoggingService?.LogInfo("[LicenseJsonStore] Licença removida do JSON");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseJsonStore] Erro ao limpar: {ex.Message}");
            }
        }

        private static void MigrateFromRegistry()
        {
            try
            {
                using var baseKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Voltris\Optimizer\Token", false);
                if (baseKey == null) return;

                var encToken = baseKey.GetValue("LT")?.ToString();
                var encType = baseKey.GetValue("LTY")?.ToString();
                var encDisplay = baseKey.GetValue("LDN")?.ToString();
                var encExpiry = baseKey.GetValue("LE")?.ToString();

                if (string.IsNullOrEmpty(encToken) || string.IsNullOrEmpty(encType) || string.IsNullOrEmpty(encExpiry)) return;

                // Tentar ler o registro via LicenseTokenStore (já criptografado)
                // Se chegamos aqui e temos dados no registro, migramos
                App.LoggingService?.LogInfo("[LicenseJsonStore] Dados encontrados no registro. Migrando para JSON...");
                
                // NOTA: Aqui não podemos usar o DecryptValue do LicenseTokenStore facilmente 
                // sem mudar visibilidade, então vamos usar o Registry como fonte na primeira carga do TokenStore
                // O próprio TryRestoreFromRegistry do LicenseTokenStore já faz o fallback para o Registro
                // se o JSON não existir. O JSON será salvo pelo SetValidatedLicense na próxima ativação
                // ou podemos forçar um Save se o Registro for lido com sucesso.
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseJsonStore] Erro na migração: {ex.Message}");
            }
        }

        private class LicenseJsonData
        {
            public string LicenseType { get; set; } = "";
            public string LicenseDisplayName { get; set; } = "";
            /// <summary>LEGADO — sempre vazio em novas gravações. Mantido só para
            /// conseguir ler arquivos de versões anteriores.</summary>
            public string LicenseKey { get; set; } = "";
            /// <summary>Chave da licença protegida com DPAPI (Base64).</summary>
            public string LicenseKeyEnc { get; set; } = "";
            public DateTime ExpiresAt { get; set; } = DateTime.MinValue;
            public string BillingPeriod { get; set; } = "year";
            public DateTime SavedAt { get; set; } = DateTime.UtcNow;
        }
    }
}
