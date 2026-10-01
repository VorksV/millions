using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Threading;
using VoltrisOptimizer.Services.Enterprise;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço de proteção do período de trial
    /// Implementa múltiplas camadas de segurança para evitar manipulação
    /// PERFORMANCE: MachineId agora é calculado UMA VEZ por sessão e cacheado.
    /// </summary>
    public class TrialProtectionService
    {
        private static readonly Lazy<TrialProtectionService> _instance = new(() => new TrialProtectionService());
        public static TrialProtectionService Instance => _instance.Value;
        
        // Constantes
        public const int TRIAL_DAYS = 7;
        private const string REGISTRY_KEY_PATH = @"SOFTWARE\Voltris\Optimizer";
        private const string TRIAL_START_VALUE = "InstallDate";
        private const string TRIAL_HASH_VALUE = "InstallHash";
        private const string MACHINE_ID_VALUE = "MachineId";
        
        // Caminho do arquivo de trial oculto
        private static readonly string TrialFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Voltris", ".trial");
        
        // Caminho secundário (backup)
        private static readonly string TrialBackupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Voltris", ".trialdata");
        
        // Chave de ofuscação (gerada a partir do hardware ID)
        private string? _encryptionKey;

        // CORREÇÃO PERFORMANCE CRÍTICA: Cache do MachineId por sessão.
        // GetMachineId() fazia 3 consultas WMI BLOQUEANTES a cada chamada.
        // Com 6+ invocações por verificação de trial = 18+ queries WMI por ciclo.
        private string? _cachedMachineId;
        
        private TrialProtectionService()
        {
            // OTIMIZAÇÃO: Não calcular WMI no construtor pois pode travar a UI thread se o singleton 
            // for resolvido pelo DI durante a criação da MainWindow/DashboardViewModel.
            // _cachedMachineId será preenchido sob demanda ou via InitializeAsync().
        }

        private static readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _isInitialized = false;

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;
            await _initLock.WaitAsync();
            try
            {
                if (_isInitialized) return;
                
                _cachedMachineId = await Task.Run(() => ComputeMachineIdOnce());
                _encryptionKey = GenerateMachineKey();
                _isInitialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Calcula o MachineId UMA VEZ usando WMI e retorna o valor cacheado em seguida.
        /// </summary>
        private string ComputeMachineIdOnce()
        {
            try
            {
                var sb = new StringBuilder();
                
                // Tentar primeiro via registro (muito mais rápido que WMI)
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY_PATH);
                    var stored = key?.GetValue(MACHINE_ID_VALUE)?.ToString();
                    if (!string.IsNullOrEmpty(stored) && stored.Length == 16)
                        return stored; // Já existe, não precisa de WMI
                }
                catch { }

                // 1. CPU ID via Intrinsics/Registro (Zero WMI)
                string cpuId = "";
                try
                {
                    if (System.Runtime.Intrinsics.X86.X86Base.IsSupported)
                    {
                        var result = System.Runtime.Intrinsics.X86.X86Base.CpuId(1, 0);
                        cpuId = $"{result.Edx:X8}{result.Eax:X8}";
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(cpuId))
                {
                    try
                    {
                        cpuId = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "")?.ToString()?.Trim() ?? "";
                    }
                    catch { }
                }
                sb.Append(cpuId);

                // 2. Motherboard Serial via Registro (Zero WMI)
                string mbSerial = "";
                try
                {
                    mbSerial = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardSerialNumber", "")?.ToString()?.Trim() ?? "";
                }
                catch { }
                sb.Append(mbSerial);

                // 3. BIOS Serial via Registro (Zero WMI)
                string biosSerial = "";
                try
                {
                    biosSerial = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "BiosSerialNumber", "")?.ToString()?.Trim() ?? 
                                 Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "SystemSerialNumber", "")?.ToString()?.Trim() ?? "";
                }
                catch { }
                sb.Append(biosSerial);

                // Fallback para WMI apenas se não conseguiu ler nada significativo via caminhos rápidos
                if (string.IsNullOrEmpty(cpuId) && string.IsNullOrEmpty(mbSerial) && string.IsNullOrEmpty(biosSerial))
                {
                    App.LoggingService?.LogInfo("[Trial] Caminhos rápidos vazios, iniciando WMI fallback para MachineId...");
                    
                    // CPU ID Fallback
                    try
                    {
                        using var searcher = new System.Management.ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                        foreach (var obj in searcher.Get())
                        {
                using var __dispose_obj = obj;
                            sb.Append(obj["ProcessorId"]?.ToString() ?? "");
                            break;
                        }
                    }
                    catch { }
                    
                    // Motherboard serial Fallback
                    try
                    {
                        using var searcher = new System.Management.ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                        foreach (var obj in searcher.Get())
                        {
                using var __dispose_obj = obj;
                            sb.Append(obj["SerialNumber"]?.ToString() ?? "");
                            break;
                        }
                    }
                    catch { }
                    
                    // BIOS serial Fallback
                    try
                    {
                        using var searcher = new System.Management.ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS");
                        foreach (var obj in searcher.Get())
                        {
                using var __dispose_obj = obj;
                            sb.Append(obj["SerialNumber"]?.ToString() ?? "");
                            break;
                        }
                    }
                    catch { }
                }

                if (sb.Length > 0)
                    return ComputeHash(sb.ToString()).Substring(0, 16);
            }
            catch { }
            
            // Fallback final para nome da máquina + usuário (zero WMI, zero I/O)
            return ComputeHash(Environment.MachineName + Environment.UserName).Substring(0, 16);
        }
        
        /// <summary>
        /// Inicializa o período de trial
        /// </summary>
        public void InitializeTrial()
        {
            // ─────────────────────────────────────────────────────────────
            // TRIAL REMOVIDO — PRODUTO GRATUITO (decisão do dono do projeto)
            // ─────────────────────────────────────────────────────────────
            // O Voltris é gratuito. Não existe período de teste, prazo nem
            // expiração. Apenas os recursos marcados como PRO exigem licença,
            // e esse gate continua funcionando normalmente.
            //
            // Antes, este método gravava uma data de início e o trial expirava
            // em 7 dias, o que bloqueava a própria aplicação — comportamento
            // incompatível com um produto gratuito. O método é mantido apenas
            // para não quebrar as chamadas legadas, mas não grava nada.
            try
            {
                ClearTrialData();
                App.LoggingService?.LogInfo("[Trial] Trial desativado: produto gratuito, sem prazo de expiração.");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[Trial] Erro ao desativar trial", ex);
            }
        }
        
        /// <summary>
        /// Verifica se o trial expirou
        /// </summary>
        public bool IsTrialExpired()
        {
            // ─────────────────────────────────────────────────────────────
            // SEMPRE FALSO — o produto é gratuito.
            // ─────────────────────────────────────────────────────────────
            // Não há prazo, portanto o trial nunca expira. Este é o ponto
            // único e autoritativo: qualquer tela que consultasse expiração
            // aqui (antes havia 707 referências a "Trial" em 39 arquivos)
            // recebe "não expirado" sem precisar tocar em cada uma delas.
            //
            // O gate de licença PRO é OUTRO sistema (LicenseManager / LicenseGate)
            // e permanece intacto — features PRO continuam exigindo licença.
            try
            {
                // Fecha a porta de exploração que existia antes: ajustar o relógio
                // do sistema ou editar o registro não pode mais "expirar" o app.
                if (SettingsService.Instance.Settings.ForceTrialExpired)
                {
                    App.LoggingService?.LogWarning("[Trial] ForceTrialExpired ignorado: o produto é gratuito e não expira.");
                    SettingsService.Instance.Settings.ForceTrialExpired = false;
                }
                if (SettingsService.Instance.Settings.TrialExpired)
                {
                    SettingsService.Instance.Settings.TrialExpired = false;
                }

                return false;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[Trial] Erro ao verificar trial", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Obtém dias restantes do trial
        /// </summary>
        public int GetDaysRemaining()
        {
            // Produto gratuito: não há contagem regressiva. -1 é o sentinela
            // que o próprio código já usava para "licenciado / sem limite",
            // então as telas que exibem dias restantes deixam de nagging.
            return -1;
        }

        /// <summary>
        /// CRÍTICO: Limpa completamente todos os dados de trial de TODAS as fontes.
        /// Usado quando o usuário revoga a licença para resetar o estado do trial.
        /// Sem este método, SaveTrialData reativa o trial automaticamente após Revoke.
        /// </summary>
        public void ClearTrialData()
        {
            try
            {
                App.LoggingService?.LogInfo("[Trial] ⚠️ LIMPEZA COMPLETA DE DADOS DE TRIAL INICIADA");

                // 1. Limpar Registry
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY_PATH, true);
                    if (key != null)
                    {
                        try { key.DeleteValue(TRIAL_START_VALUE, false); } catch { }
                        try { key.DeleteValue(TRIAL_HASH_VALUE, false); } catch { }
                        try { key.DeleteValue(MACHINE_ID_VALUE, false); } catch { }
                    }
                    App.LoggingService?.LogSuccess("[Trial] Registry limpo com sucesso");
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogWarning($"[Trial] Erro ao limpar Registry: {ex.Message}");
                }

                // 2. Limpar arquivo .trial
                try
                {
                    if (File.Exists(TrialFilePath))
                    {
                        File.SetAttributes(TrialFilePath, FileAttributes.Normal);
                        File.Delete(TrialFilePath);
                        App.LoggingService?.LogSuccess($"[Trial] Arquivo .trial deletado: {TrialFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogWarning($"[Trial] Erro ao deletar .trial: {ex.Message}");
                }

                // 3. Limpar backup .trialdata
                try
                {
                    if (File.Exists(TrialBackupPath))
                    {
                        File.SetAttributes(TrialBackupPath, FileAttributes.Normal);
                        File.Delete(TrialBackupPath);
                        App.LoggingService?.LogSuccess($"[Trial] Arquivo .trialdata deletado: {TrialBackupPath}");
                    }
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogWarning($"[Trial] Erro ao deletar .trialdata: {ex.Message}");
                }

                // 4. Limpar configurações
                try
                {
                    SettingsService.Instance.Settings.FirstRunDate = DateTime.MinValue;
                    SettingsService.Instance.SaveSettings();
                    App.LoggingService?.LogSuccess("[Trial] Configurações de FirstRunDate limpas");
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogWarning($"[Trial] Erro ao limpar settings: {ex.Message}");
                }

                App.LoggingService?.LogSuccess("[Trial] ✅ LIMPEZA COMPLETA DE TRIAL FINALIZADA");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[Trial] Erro crítico ao limpar dados de trial", ex);
            }
        }
        
        /// <summary>
        /// Obtém a data de início do trial de múltiplas fontes
        /// </summary>
        private DateTime? GetTrialStartDate()
        {
            DateTime? fromRegistry = null;
            DateTime? fromFile = null;
            DateTime? fromBackup = null;
            DateTime? fromSettings = null;
            
            // 1. Tentar do registro
            fromRegistry = GetTrialFromRegistry();
            
            // 2. Tentar do arquivo oculto
            fromFile = GetTrialFromFile(TrialFilePath);
            
            // 3. Tentar do backup
            fromBackup = GetTrialFromFile(TrialBackupPath);
            
            // 4. Tentar das configurações
            fromSettings = SettingsService.Instance.Settings.FirstRunDate;
            
            // Usar a data mais antiga (mais confiável - previne reset)
            DateTime? result = null;
            
            if (fromRegistry.HasValue)
                result = fromRegistry;
            
            if (fromFile.HasValue && (!result.HasValue || fromFile.Value < result.Value))
                result = fromFile;
            
            if (fromBackup.HasValue && (!result.HasValue || fromBackup.Value < result.Value))
                result = fromBackup;
            
            if (fromSettings.HasValue && (!result.HasValue || fromSettings.Value < result.Value))
                result = fromSettings;
            
            // Se encontrou uma data, garantir que está sincronizada em todas as fontes
            if (result.HasValue)
            {
                SyncTrialData(result.Value);
            }
            
            return result;
        }
        
        /// <summary>
        /// Obtém trial do registro do Windows
        /// </summary>
        private DateTime? GetTrialFromRegistry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY_PATH);
                if (key == null) return null;
                
                var encryptedDate = key.GetValue(TRIAL_START_VALUE)?.ToString();
                var storedHash = key.GetValue(TRIAL_HASH_VALUE)?.ToString();
                var storedMachineId = key.GetValue(MACHINE_ID_VALUE)?.ToString();
                
                if (string.IsNullOrEmpty(encryptedDate) || string.IsNullOrEmpty(storedHash))
                    return null;
                
                // Verificar machine ID — USANDO CACHE (zero WMI neste ponto)
                var currentMachineId = GetMachineId();
                if (storedMachineId != currentMachineId)
                {
                    App.LoggingService?.LogWarning("[Trial] Machine ID diferente detectado");
                    return null;
                }
                
                // Descriptografar e verificar hash
                var decrypted = Decrypt(encryptedDate);
                var expectedHash = ComputeHash(decrypted + currentMachineId);
                
                if (storedHash != expectedHash)
                {
                    App.LoggingService?.LogWarning("[Trial] Hash de trial inválido no registro");
                    return null;
                }
                
                if (DateTime.TryParse(decrypted, out var date))
                    return date;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[Trial] Erro ao ler registro: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Obtém trial de arquivo
        /// </summary>
        private DateTime? GetTrialFromFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                
                var content = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<TrialFileData>(Decrypt(content), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                
                if (data == null) return null;
                
                // Verificar machine ID — USANDO CACHE (zero WMI neste ponto)
                if (data.MachineId != GetMachineId())
                {
                    App.LoggingService?.LogWarning("[Trial] Machine ID diferente no arquivo");
                    return null;
                }
                
                // Verificar hash
                var expectedHash = ComputeHash(data.StartDate.ToString("o") + data.MachineId);
                if (data.Hash != expectedHash)
                {
                    App.LoggingService?.LogWarning("[Trial] Hash de trial inválido no arquivo");
                    return null;
                }
                
                return data.StartDate;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Salva dados do trial em múltiplas localizações
        /// </summary>
        private void SaveTrialData(DateTime startDate)
        {
            var machineId = GetMachineId(); // USANDO CACHE
            var hash = ComputeHash(startDate.ToString("o") + machineId);
            
            // 1. Salvar no registro
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY_PATH);
                key.SetValue(TRIAL_START_VALUE, Encrypt(startDate.ToString("o")));
                key.SetValue(TRIAL_HASH_VALUE, hash);
                key.SetValue(MACHINE_ID_VALUE, machineId);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[Trial] Erro ao salvar no registro: {ex.Message}");
            }
            
            // 2. Salvar em arquivo oculto
            SaveTrialToFile(TrialFilePath, startDate, machineId, hash);
            
            // 3. Salvar backup (apenas se for admin, pois C:\ProgramData é restrito)
            if (IsAdministrator())
            {
                SaveTrialToFile(TrialBackupPath, startDate, machineId, hash);
            }
            
            // 4. Salvar nas configurações
            SettingsService.Instance.Settings.FirstRunDate = startDate;
            SettingsService.Instance.SaveSettings();
        }
        
        private void SaveTrialToFile(string path, DateTime startDate, string machineId, string hash)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    try
                    {
                        if (!Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                            App.LoggingService?.LogInfo($"[Trial] Diretório criado: {dir}");
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        App.LoggingService?.LogWarning($"[Trial] Permissão negada ao criar diretório: {dir}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        App.LoggingService?.LogWarning($"[Trial] Erro ao criar diretório {dir}: {ex.Message}");
                        return;
                    }
                }
                
                var data = new TrialFileData
                {
                    StartDate = startDate,
                    MachineId = machineId,
                    Hash = hash,
                    Version = 1
                };
                
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    // CORREÇÃO FORENSE #4: Previne JsonException por ciclo de referência
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                // Limpar atributos se o arquivo já existir para permitir sobrescrever
                if (File.Exists(path))
                {
                    try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                }

                File.WriteAllText(path, Encrypt(json));
                
                // Ocultar arquivo novamente
                try { File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System); } catch { }
                App.LoggingService?.LogInfo($"[Trial] Arquivo salvo com sucesso: {path}");
            }
            catch (UnauthorizedAccessException)
            {
                App.LoggingService?.LogWarning($"[Trial] Permissão negada ao escrever arquivo: {path}");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[Trial] Erro ao salvar arquivo {path}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Sincroniza dados de trial entre todas as fontes
        /// </summary>
        private void SyncTrialData(DateTime startDate)
        {
            // OTIMIZAÇÃO: Rodar sincronização em background para não travar a UI thread no startup
            Task.Run(() => 
            {
                try
                {
                    var machineId = GetMachineId(); // USANDO CACHE
                    var hash = ComputeHash(startDate.ToString("o") + machineId);
                    
                    // Verificar e atualizar registro
                    var fromRegistry = GetTrialFromRegistry();
                    if (!fromRegistry.HasValue || fromRegistry.Value != startDate)
                    {
                        try
                        {
                            using var key = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY_PATH);
                            key.SetValue(TRIAL_START_VALUE, Encrypt(startDate.ToString("o")));
                            key.SetValue(TRIAL_HASH_VALUE, hash);
                            key.SetValue(MACHINE_ID_VALUE, machineId);
                        }
                        catch { }
                    }
                    
                    // Verificar e atualizar arquivos
                    var fromFile = GetTrialFromFile(TrialFilePath);
                    if (!fromFile.HasValue || fromFile.Value != startDate)
                    {
                        SaveTrialToFile(TrialFilePath, startDate, machineId, hash);
                    }
                    
                    var fromBackup = GetTrialFromFile(TrialBackupPath);
                    if (!fromBackup.HasValue || fromBackup.Value != startDate)
                    {
                        SaveTrialToFile(TrialBackupPath, startDate, machineId, hash);
                    }
                    
                    // Atualizar settings
                    if (SettingsService.Instance.Settings.FirstRunDate != startDate)
                    {
                        SettingsService.Instance.Settings.FirstRunDate = startDate;
                        SettingsService.Instance.SaveSettings();
                    }
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError("[Trial] Erro na sincronização de background", ex);
                }
            });
        }
        
        /// <summary>
        /// Detecta manipulação de clock do sistema
        /// </summary>
        private bool IsClockManipulated(DateTime trialStart)
        {
            try
            {
                // Se a data do sistema é anterior à data de início do trial, é suspeito
                if (DateTime.UtcNow < trialStart.AddHours(-1))
                {
                    return true;
                }
                
                // Verificar última data conhecida
                var lastKnownDate = GetLastKnownDate();
                if (lastKnownDate.HasValue && DateTime.UtcNow < lastKnownDate.Value.AddHours(-1))
                {
                    return true;
                }
                
                // Atualizar última data conhecida
                SaveLastKnownDate(DateTime.UtcNow);
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        private DateTime? GetLastKnownDate()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY_PATH);
                var value = key?.GetValue("LastSeen")?.ToString();
                if (!string.IsNullOrEmpty(value) && DateTime.TryParse(Decrypt(value), out var date))
                    return date;
            }
            catch { }
            return null;
        }
        
        private void SaveLastKnownDate(DateTime date)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY_PATH);
                key.SetValue("LastSeen", Encrypt(date.ToString("o")));
            }
            catch { }
        }
        
        /// <summary>
        /// Retorna o MachineId cacheado (calculado UMA VEZ no construtor).
        /// Custo: O(1) — apenas retorna uma string em memória.
        /// </summary>
        private string GetMachineId()
        {
            // Se ainda não foi calculado (ex: acesso síncrono antes do InitializeAsync), 
            // usar fallback rápido baseado em Environment para evitar WMI na UI thread.
            if (string.IsNullOrEmpty(_cachedMachineId))
            {
                return ComputeHash(Environment.MachineName + Environment.UserName).Substring(0, 16);
            }
            return _cachedMachineId;
        }
        
        private bool IsAdministrator()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
        
        private string GenerateMachineKey()
        {
            var machineId = GetMachineId(); // USANDO CACHE
            return ComputeHash("VOLTRIS_KEY_" + machineId).Substring(0, 32);
        }
        
        #region Criptografia
        
        private string Encrypt(string plainText)
        {
            try
            {
                if (string.IsNullOrEmpty(_encryptionKey))
                    _encryptionKey = GenerateMachineKey();
                
                var key = Encoding.UTF8.GetBytes(_encryptionKey);
                var iv = new byte[16];
                Array.Copy(key, iv, 16);
                
                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                
                using var encryptor = aes.CreateEncryptor();
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                
                return Convert.ToBase64String(encryptedBytes);
            }
            catch
            {
                // Fallback: Base64 simples
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
            }
        }
        
        private string Decrypt(string cipherText)
        {
            try
            {
                if (string.IsNullOrEmpty(_encryptionKey))
                    _encryptionKey = GenerateMachineKey();
                
                var key = Encoding.UTF8.GetBytes(_encryptionKey);
                var iv = new byte[16];
                Array.Copy(key, iv, 16);
                
                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                
                using var decryptor = aes.CreateDecryptor();
                var cipherBytes = Convert.FromBase64String(cipherText);
                var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // Fallback: Base64 simples
                try
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(cipherText));
                }
                catch
                {
                    return cipherText;
                }
            }
        }
        
        private string ComputeHash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
        
        #endregion
        
        /// <summary>
        /// Verifica a integridade do trial de forma assíncrona (pode consultar servidor)
        /// </summary>
        public async Task<bool> VerifyTrialIntegrityAsync()
        {
            try
            {
                // Verificações locais
                var startDate = GetTrialStartDate();
                if (startDate == null)
                    return true; // Primeiro uso
                
                // Verificar manipulação de clock
                if (IsClockManipulated(startDate.Value))
                    return false;
                
                // Tentar verificar hora online (opcional)
                var onlineTime = await GetOnlineTimeAsync();
                if (onlineTime.HasValue)
                {
                    // Se a hora online mostra que o trial expirou, mas o clock local não
                    var onlineDaysPassed = (onlineTime.Value - startDate.Value).TotalDays;
                    if (onlineDaysPassed > TRIAL_DAYS)
                    {
                        App.LoggingService?.LogWarning("[Trial] Verificação online indica trial expirado");
                        return false;
                    }
                }
                
                return true;
            }
            catch
            {
                return true; // Em caso de erro, permitir uso
            }
        }
        
        private async Task<DateTime?> GetOnlineTimeAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await client.GetAsync("http://worldtimeapi.org/api/ip");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(json);
                    var dateStr = doc.RootElement.GetProperty("utc_datetime").GetString();
                    if (DateTime.TryParse(dateStr, out var date))
                        return date;
                }
            }
            catch { }
            
            return null;
        }
        
        /// <summary>
        /// Classe para serialização do arquivo de trial
        /// </summary>
        private class TrialFileData
        {
            public DateTime StartDate { get; set; }
            public string MachineId { get; set; } = "";
            public string Hash { get; set; } = "";
            public int Version { get; set; }
        }
    }
}
