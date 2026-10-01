using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Personalize;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services
{
    public class SettingsService
    {
        private static string SettingsPath
        {
            get
            {
                AppDataPaths.EnsureDirectory(AppDataPaths.UnifiedRoot);
                return AppDataPaths.Settings;
            }
        }

        // Lock para garantir thread-safety em operações de arquivo
        private static readonly object _settingsLock = new object();

        private static readonly Lazy<SettingsService> _instanceLazy = new(() => new SettingsService());
        public static SettingsService Instance => _instanceLazy.Value;

        public AppSettings Settings { get; private set; }
        
        // Evento para notificar mudanças de perfil
        public event EventHandler<IntelligentProfileType>? ProfileChanged;
        
        // Evento para notificar mudanças de vinculação de conta (login/logout do Voltris Cloud)
        public event EventHandler<(bool IsLinked, string? Email)>? LinkingStatusChanged;

        // Evento genérico para notificar mudanças em qualquer configuração
        public event EventHandler? SettingsChanged;

        private SettingsService()
        {
            Settings = new AppSettings();
            LoadSettings();
        }

        private static void MigrateFromOldPaths()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                var oldExePath = Path.Combine(baseDir, "settings.json");
                if (File.Exists(oldExePath) && !File.Exists(SettingsPath))
                {
                    AppDataPaths.EnsureDirectory(AppDataPaths.UnifiedRoot);
                    File.Copy(oldExePath, SettingsPath, true);
                    App.LoggingService?.LogInfo($"[SETTINGS] Migrado de '{oldExePath}' para '{SettingsPath}'");
                }

                var oldLocalPath = Path.Combine(localAppData, "VoltrisOptimizer", "settings.json");
                if (File.Exists(oldLocalPath) && !File.Exists(SettingsPath))
                {
                    AppDataPaths.EnsureDirectory(AppDataPaths.UnifiedRoot);
                    File.Copy(oldLocalPath, SettingsPath, true);
                    App.LoggingService?.LogInfo($"[SETTINGS] Migrado de '{oldLocalPath}' para '{SettingsPath}'");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SETTINGS] Falha na migração de caminhos legados: {ex.Message}");
            }
        }

        public void LoadSettings()
        {
            lock (_settingsLock)
            {
                try
                {
                    App.LoggingService?.LogInfo("[SETTINGS] Iniciando LoadSettings()");

                    MigrateFromOldPaths();
                    
                    // FIX: Retry mechanism para startup do Windows (arquivo pode estar bloqueado/em uso)
                    int retryCount = 0;
                    const int maxRetries = 3;
                    string? json = null;
                    Exception? lastException = null;
                    
                    while (retryCount < maxRetries)
                    {
                        try
                        {
                            if (File.Exists(SettingsPath))
                            {
                                json = File.ReadAllText(SettingsPath);
                                break;
                            }
                            else
                            {
                                App.LoggingService?.LogWarning($"[SETTINGS] settings.json não encontrado (tentativa {retryCount + 1}/{maxRetries})");
                                break;
                            }
                        }
                        catch (IOException ioEx) when (retryCount < maxRetries - 1)
                        {
                            retryCount++;
                            lastException = ioEx;
                            App.LoggingService?.LogWarning($"[SETTINGS] Erro de I/O ao ler settings (tentativa {retryCount}/{maxRetries}): {ioEx.Message}");
                            System.Threading.Thread.Sleep(150 * retryCount);
                        }
                    }
                    
                    if (json != null)
                    {
                        App.LoggingService?.LogInfo("[SETTINGS] Arquivo settings.json lido com sucesso");
                        
                        // Validar schema e adicionar propriedades faltantes
                        bool needsMigration = false;
                        try
                        {
                            using var doc = JsonDocument.Parse(json);

                            // Verificar SchemaVersion (case-insensitive — JSON usa camelCase)
                            if (!PropExists(doc.RootElement, "SchemaVersion"))
                            {
                                App.LoggingService?.LogWarning("[SETTINGS] ⚠️ JSON não contém SchemaVersion - adicionando");
                                needsMigration = true;
                            }

                            // Verificar IsFirstRun
                            if (!PropExists(doc.RootElement, "IsFirstRun"))
                            {
                                App.LoggingService?.LogWarning("[SETTINGS] ⚠️ JSON não contém IsFirstRun - será criado novo objeto");
                                needsMigration = true;
                            }

                            // Verificar outras propriedades críticas
                            if (!PropExists(doc.RootElement, "OnboardingCompleted"))
                            {
                                App.LoggingService?.LogWarning("[SETTINGS] ⚠️ JSON não contém OnboardingCompleted - adicionando");
                                needsMigration = true;
                            }

                            if (!PropExists(doc.RootElement, "LinkedUserEmail"))
                            {
                                App.LoggingService?.LogWarning("[SETTINGS] ⚠️ JSON não contém LinkedUserEmail - adicionando");
                                needsMigration = true;
                            }

                            if (!PropExists(doc.RootElement, "IsDeviceLinked"))
                            {
                                App.LoggingService?.LogWarning("[SETTINGS] ⚠️ JSON não contém IsDeviceLinked - adicionando");
                                needsMigration = true;
                            }
                        }
                        catch (Exception parseEx)
                        {
                            App.LoggingService?.LogError($"[SETTINGS] Erro ao validar schema: {parseEx.Message}");
                            needsMigration = true;
                        }
                        
                        if (needsMigration)
                        {
                            App.LoggingService?.LogInfo("[SETTINGS] Migrando settings para schema mais recente...");
                        }
                        
                            Settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new AppSettings();

                        // ── Migração: chave OpenAI de texto claro -> DPAPI ──
                        // Arquivos gravados por versões anteriores têm
                        // "openAIApiKey" em claro. Como a property agora é
                        // [JsonIgnore], esse valor não é lido; converte-se o
                        // campo legado uma única vez e regrava o arquivo.
                        MigrateLegacyOpenAiKey(json);

                        // FIX: Validar consistência das configurações críticas após carregamento
                        ValidateAndFixCriticalSettings(json);
                        
                        App.LoggingService?.LogInfo($"[SETTINGS] Valores carregados:");
                        App.LoggingService?.LogInfo($"[SETTINGS]   IsFirstRun: {Settings.IsFirstRun}");
                        App.LoggingService?.LogInfo($"[SETTINGS]   IsDeviceLinked: {Settings.IsDeviceLinked}");
                        App.LoggingService?.LogInfo($"[SETTINGS]   LinkedUserEmail: '{Settings.LinkedUserEmail}'");
                        App.LoggingService?.LogInfo($"[SETTINGS]   OnboardingCompleted: {Settings.OnboardingCompleted}");
                        App.LoggingService?.LogInfo($"[SETTINGS]   EnableTransparency: {Settings.EnableTransparency}");
                        
                        // 🔥 DIAGNÓSTICO: Verificar se IsFirstRun foi carregado corretamente
                        App.LoggingService?.LogInfo($"[SETTINGS] 🔥 DIAGNÓSTICO: IsFirstRun após carregar = {Settings.IsFirstRun}");
                        
                        // Migração: Garantir que DLS e DPC Watchdog venham ativados por padrão
                        var needsSave = needsMigration;
                        
                        // Migração de Schema: Adicionar propriedades faltantes
                        if (Settings.SchemaVersion < 1)
                        {
                            Settings.SchemaVersion = 1;
                            needsSave = true;
                            App.LoggingService?.LogInfo("[SETTINGS] Schema migrado para versão 1");
                        }
                        
                        // Garantir IsFirstRun definido corretamente
                        if (!needsMigration && Settings.IsFirstRun && Settings.OnboardingCompleted)
                        {
                            // Se onboarding foi completado mas IsFirstRun ainda é true, corrigir
                            Settings.IsFirstRun = false;
                            needsSave = true;
                            App.LoggingService?.LogInfo("[SETTINGS] IsFirstRun corrigido para false (onboarding já completado)");
                        }
                        
                        // HasGamerModeConfigured defaults to false if not present
                        try
                        {
                            using var doc = JsonDocument.Parse(json);
                            if (!doc.RootElement.TryGetProperty("HasGamerModeConfigured", out _))
                            {
                                Settings.HasGamerModeConfigured = false;
                                needsSave = true;
                            }
                        }
                        catch { }

                        // Verificar AllowBackgroundDpcWatchdog
                        try
                        {
                            var jsonDoc = JsonDocument.Parse(json);
                            if (!jsonDoc.RootElement.TryGetProperty("AllowBackgroundDpcWatchdog", out _))
                            {
                                Settings.AllowBackgroundDpcWatchdog = true;
                                needsSave = true;
                            }
                            else if (!Settings.AllowBackgroundDpcWatchdog)
                            {
                                Settings.AllowBackgroundDpcWatchdog = true;
                                needsSave = true;
                            }
                        }
                        catch
                        {
                            Settings.AllowBackgroundDpcWatchdog = true;
                            needsSave = true;
                        }
                        
                        // IntelligentProfile padrão
                        try
                        {
                            using var doc = JsonDocument.Parse(json);
                            if (!doc.RootElement.TryGetProperty("IntelligentProfile", out _))
                            {
                                Settings.IntelligentProfile = IntelligentProfileType.GeneralBalanced;
                                needsSave = true;
                            }
                        }
                        catch { }

                        if (needsSave)
                        {
                            // Chamada interna segura (já estamos no lock, mas SaveSettings tem lock, então cuidado com reentrância se não usar Monitor)
                            // Como lock suporta reentrância na mesma thread, ok chamar SaveSettings aqui.
                            SaveSettingsInternal(); 
                        }
                    }
                    else
                    {
                        App.LoggingService?.LogWarning("[SETTINGS] settings.json não existe - criando novo com valores padrão");
                        Settings = new AppSettings();
                        Settings.IntelligentProfile = IntelligentProfileType.GeneralBalanced;
                        Settings.StartWithWindows = true;
                        // [FIX:STARTUP-VISIBILIDADE] Desmarcado por padrão: a
                        // abertura manual deve mostrar a janela. Quem quiser a
                        // bandeja nos lançamentos manuais marca a opção; o
                        // auto-start do Windows já sobe para a bandeja por conta
                        // própria, via StartupAutostartDetector.
                        Settings.StartMinimized = false;
                        Settings.EnableDynamicLoadStabilizer = true;
                        Settings.AllowBackgroundDpcWatchdog = true;
                        Settings.AutoGamerMode = true;
                        // FIX: Manter EnableTransparency = true (padrão) mas logar para diagnóstico
                        App.LoggingService?.LogInfo("[SETTINGS] EnableTransparency definido como true (padrão)");
                    }
                }
                catch
                {
                    App.LoggingService?.LogError("[SETTINGS] Erro crítico ao carregar settings - usando fallback");
                    Settings = new AppSettings();
                    Settings.StartWithWindows = true;
                    // [FIX:STARTUP-VISIBILIDADE] Mesmo no fallback de erro, o
                    // padrão é NÃO esconder a janela: um erro de leitura não deve
                    // custar ao usuário a janela que ele acabou de pedir.
                    Settings.StartMinimized = false;
                    Settings.EnableDynamicLoadStabilizer = true;
                    Settings.AllowBackgroundDpcWatchdog = true;
                    // FIX: Manter EnableTransparency = true (padrão) em caso de erro crítico
                }
            }
        }

        /// <summary>
        /// FIX: Valida e corrige configurações críticas após carregamento para prevenir
        /// inconsistências no startup do Windows (ex: acrylic com transparência "fantasma")
        /// </summary>
        private void ValidateAndFixCriticalSettings(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                
                // Verificar se EnableTransparency existe no JSON original
                bool transparencyExists = doc.RootElement.TryGetProperty("EnableTransparency", out var transparencyProp);
                
                if (transparencyExists)
                {
                    bool savedTransparency = transparencyProp.GetBoolean();
                    
                    // FIX: Se o JSON tem EnableTransparency=false mas o objeto carregado tem true,
                    // isso indica um problema de desserialização - forçar sincronização
                    if (savedTransparency != Settings.EnableTransparency)
                    {
                        App.LoggingService?.LogWarning($"[SETTINGS] ⚠️ EnableTransparency inconsistente! JSON={savedTransparency}, Objeto={Settings.EnableTransparency}. Corrigindo para {savedTransparency}");
                        Settings.EnableTransparency = savedTransparency;
                    }
                    else
                    {
                        App.LoggingService?.LogInfo($"[SETTINGS] ✅ EnableTransparency consistente: {Settings.EnableTransparency}");
                    }
                }
                else
                {
                    App.LoggingService?.LogWarning("[SETTINGS] EnableTransparency não existe no JSON - adicionando com valor true (padrão)");
                    Settings.EnableTransparency = true;
                }
                
                // Verificar Theme consistency
                bool themeExists = doc.RootElement.TryGetProperty("Theme", out var themeProp);
                if (themeExists && Settings.Theme != themeProp.GetString())
                {
                    Settings.Theme = themeProp.GetString() ?? "Dark";
                    App.LoggingService?.LogInfo($"[SETTINGS] Theme sincronizado: {Settings.Theme}");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[SETTINGS] ValidateAndFixCriticalSettings falhou: {ex.Message}");
            }
        }

        public void SaveSettings()
        {
            _ = Task.Run(() => 
            {
                lock (_settingsLock)
                {
                    SaveSettingsInternal();
                }
            });
        }
        
        // 🔥 MÉTODO PARA FORÇAR SALVAMENTO COMPLETO (incluindo IsFirstRun)
        public void ForceSaveAllSettings()
        {
            lock (_settingsLock)
            {
                App.LoggingService?.LogInfo("[SETTINGS] 🔥 ForceSaveAllSettings() - Forçando salvamento completo");
                
                // Garantir que todas as propriedades importantes estejam definidas
                if (Settings == null)
                {
                    Settings = new AppSettings();
                }
                
                // Forçar valores padrão se necessário
                App.LoggingService?.LogInfo($"[SETTINGS] 🔥 Valores antes do force save:");
                App.LoggingService?.LogInfo($"[SETTINGS]   IsFirstRun: {Settings.IsFirstRun}");
                App.LoggingService?.LogInfo($"[SETTINGS]   IsDeviceLinked: {Settings.IsDeviceLinked}");
                
                SaveSettingsInternal();
                
                App.LoggingService?.LogInfo("[SETTINGS] 🔥 ForceSaveAllSettings() concluído");
            }
        }

        private void SaveSettingsInternal()
        {
            App.LoggingService?.LogInfo("[SETTINGS] Iniciando SaveSettings()");
            App.LoggingService?.LogInfo($"[SETTINGS] Valores a salvar:");
            App.LoggingService?.LogInfo($"[SETTINGS]   IsFirstRun: {Settings.IsFirstRun}");
            App.LoggingService?.LogInfo($"[SETTINGS]   IsDeviceLinked: {Settings.IsDeviceLinked}");
            App.LoggingService?.LogInfo($"[SETTINGS]   LinkedUserEmail: '{Settings.LinkedUserEmail}'");
            App.LoggingService?.LogInfo($"[SETTINGS]   OnboardingCompleted: {Settings.OnboardingCompleted}");
            
            // 🔥 DIAGNÓSTICO: Verificar se IsFirstRun está sendo salvo corretamente

            const int maxRetries = 3;
            int retryCount = 0;
            Exception? lastException = null;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    // Escrita atômica via arquivo temporário
                    var tempPath = SettingsPath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    
                    if (!File.Exists(tempPath)) throw new IOException("Failed to write temporary settings file");

                    File.Move(tempPath, SettingsPath, true);
                    
                    if (!File.Exists(SettingsPath)) throw new IOException("Settings file was not created successfully");

                    CreateBackupInternal();

                    try
                    {
                        SettingsChanged?.Invoke(this, EventArgs.Empty);
                    }
                    catch { }

                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    retryCount++;
                    
                    // Log consolidado no sistema principal de logging
                    try
                    {
                        App.LoggingService?.LogError($"[SETTINGS] Attempt {retryCount}/{maxRetries} failed: {ex.Message}", ex);
                    }
                    catch { }

                    if (retryCount < maxRetries) System.Threading.Thread.Sleep(100 * retryCount);
                }
            }

            if (lastException != null)
            {
                // Restaurar backup em caso de falha catastrófica
                try
                {
                    var backupPath = SettingsPath + ".bak";
                    if (File.Exists(backupPath)) File.Copy(backupPath, SettingsPath, true);
                }
                catch { }

                var telemetry = App.Services?.GetService(typeof(VoltrisOptimizer.Services.Telemetry.TelemetryService)) as VoltrisOptimizer.Services.Telemetry.TelemetryService;
                _ = telemetry?.TrackExceptionAsync(lastException, "SaveSettings Failed After Retries");
                
                throw new IOException($"Failed to save settings after {maxRetries} attempts", lastException);
            }
        }

        private static bool PropExists(JsonElement element, string propertyName)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void CreateBackup()
        {
            lock (_settingsLock)
            {
                CreateBackupInternal();
            }
        }

        /// <summary>
        /// [FIX:M-7] Backup com retorno explícito para a UI poder informar o
        /// usuário. Use este em vez de <see cref="CreateBackup"/> quando o
        /// resultado precisa aparecer na tela.
        /// </summary>
        public bool CreateBackupWithResult() => TryCreateBackup();

        /// <summary>
        /// Converte a antiga "openAIApiKey" (texto claro) para "openAIApiKeyEnc"
        /// (DPAPI) uma única vez, preservando a chave que o usuário já tinha.
        /// Sem isso, a property ter virado [JsonIgnore] apagaria a chave no
        /// primeiro save.
        /// </summary>
        private void MigrateLegacyOpenAiKey(string rawJson)
        {
            try
            {
                if (Settings == null) return;
                if (!string.IsNullOrEmpty(Settings.OpenAIApiKeyEnc)) return; // já migrada (ou nunca houve chave)

                using var doc = JsonDocument.Parse(rawJson);
                if (!doc.RootElement.TryGetProperty("openAIApiKey", out var legacy)) return;

                var plain = legacy.GetString();
                if (string.IsNullOrWhiteSpace(plain)) return;

                Settings.OpenAIApiKeyEnc =
                    VoltrisOptimizer.Core.Security.SecureConfigProtection.ProtectSecret(plain) ?? "";

                App.LoggingService?.LogInfo("[SETTINGS] openAIApiKey migrada de texto claro para DPAPI.");
                SaveSettings();
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[SETTINGS] Falha ao migrar openAIApiKey: {ex.Message}");
            }
        }

        private const int MaxSettingsBackups = 10;

        /// <summary>
        /// [FIX:M-7] Resultado do último backup, para feedback ao usuário.
        /// </summary>
        public string? LastBackupPath { get; private set; }
        public bool LastBackupSucceeded { get; private set; }
        public string? LastBackupError { get; private set; }

        /// <summary>
        /// [FIX:M-7] Cria um backup e DEVOLVE o resultado, em vez de falhar em silêncio.
        ///
        /// BUG ORIGINAL: CreateBackupInternal não logava NADA no caminho de
        /// sucesso, e o chamador público <see cref="CreateBackup"/> devolvia void.
        /// Só a falha aparecia no log (e num nível Warning). Na prática isso
        /// significava que não havia como responder "o backup das configurações
        /// foi realmente criado?" — o arquivo podia não existir, o antivírus
        /// podia estar segurando, o disco podia estar cheio, e o log ficava
        /// calado em todos esses casos de sucesso. Como o backup é a única rede
        /// de proteção antes de uma restauração de sistema, a ausência de
        /// confirmação é justamente o que faz o backup não valer como garantia.
        /// A retenção também não dizia o que apagou.
        /// </summary>
        public bool TryCreateBackup()
        {
            try
            {
                AppDataPaths.EnsureDirectory(AppDataPaths.Backups);

                var backupPath = Path.Combine(AppDataPaths.Backups, $"settings_backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json");
                File.Copy(SettingsPath, backupPath, true);

                // Confirma que o backup é utilizável: sem isso, um File.Copy que
                // "funciona" mas deixa um arquivo truncado (disco cheio no meio da
                // escrita) seria reportado como sucesso.
                long backupBytes = new FileInfo(backupPath).Length;
                if (backupBytes <= 0)
                {
                    LastBackupSucceeded = false;
                    LastBackupError = "arquivo de backup criado com 0 bytes";
                    App.LoggingService?.LogError(
                        $"[FIX:M-7] Backup de configurações criado com 0 bytes — tratado como FALHA | {backupPath}");
                    return false;
                }

                // A retenção ordena pelo NOME, não por CreationTime: o timestamp do
                // nome é o instante real do backup, enquanto CreationTime pode ter
                // sido alterado por um restore (File.Copy carrega o tempo do
                // arquivo de origem) e desordenar a fila.
                var todosBackups = Directory.GetFiles(AppDataPaths.Backups, "settings_backup_*.json")
                    .OrderByDescending(f => f, StringComparer.Ordinal)
                    .ToList();
                var backups = todosBackups.Skip(MaxSettingsBackups).ToList();

                int removidos = 0;
                foreach (var oldBackup in backups)
                {
                    try
                    {
                        File.Delete(oldBackup);
                        removidos++;
                    }
                    catch (Exception ex)
                    {
                        App.LoggingService?.LogWarning($"[SETTINGS] Não foi possível remover backup antigo {Path.GetFileName(oldBackup)}: {ex.Message}");
                    }
                }

                LastBackupPath = backupPath;
                LastBackupSucceeded = true;
                LastBackupError = null;

                App.LoggingService?.LogInfo(
                    $"[FIX:M-7] Backup de configurações CONFIRMADO | arquivo={Path.GetFileName(backupPath)} | " +
                    $"bytes={backupBytes} | backupsRetidos={Math.Min(todosBackups.Count, MaxSettingsBackups)}/{MaxSettingsBackups} | " +
                    $"removidosPelaRetencao={removidos}");
                return true;
            }
            catch (Exception ex)
            {
                // Antes era "catch { }": um backup que falhasse (disco cheio,
                // antivírus segurando o arquivo) não deixava nenhum rastro.
                LastBackupSucceeded = false;
                LastBackupError = ex.Message;
                App.LoggingService?.LogWarning($"[SETTINGS] Falha ao criar backup das configurações: {ex.Message}");
                return false;
            }
        }

        private void CreateBackupInternal()
        {
            TryCreateBackup();
        }

        public bool RestoreBackup(string backupPath)
        {
            lock (_settingsLock)
            {
                try
                {
                    if (!File.Exists(backupPath))
                    {
                        App.LoggingService?.LogWarning($"[SETTINGS] RestoreBackup: arquivo não encontrado: {backupPath}");
                        return false;
                    }

                    // Valida ANTES de sobrescrever: um backup corrompido não pode
                    // derrubar as configurações atuais e ainda assim "restaurar".
                    var candidate = JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(backupPath),
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });

                    if (candidate == null)
                    {
                        App.LoggingService?.LogError($"[SETTINGS] RestoreBackup: conteúdo inválido em {backupPath}. Configurações atuais mantidas.");
                        return false;
                    }

                    File.Copy(backupPath, SettingsPath, true);
                    Settings = candidate;
                    App.LoggingService?.LogInfo($"[SETTINGS] Backup restaurado: {Path.GetFileName(backupPath)}");
                    return true;
                }
                catch (Exception ex)
                {
                    // Antes era "catch { }": um restore que corrompesse as
                    // configurações sumia sem deixar rastro no log.
                    App.LoggingService?.LogError($"[SETTINGS] Falha ao restaurar backup {backupPath}: {ex.Message}");
                    return false;
                }
            }
        }

        public List<string> GetAvailableBackups()
        {
            try
            {
                if (Directory.Exists(AppDataPaths.Backups))
                {
                    return Directory.GetFiles(AppDataPaths.Backups, "settings_backup_*.json")
                        .OrderByDescending(f => File.GetCreationTime(f))
                        .ToList();
                }
            }
            catch { }
            return new List<string>();
        }
        
        /// <summary>
        /// Notifica listeners sobre mudança de perfil inteligente
        /// </summary>
        public void NotifyProfileChanged(IntelligentProfileType newProfile)
        {
            ProfileChanged?.Invoke(this, newProfile);
            App.LoggingService?.LogInfo($"[SETTINGS] Perfil alterado para: {newProfile}");
        }

        /// <summary>
        /// Notifica todos os listeners sobre mudança no status de vinculação da conta.
        /// Chamar após qualquer login, logout ou vinculação/desvinculação.
        /// </summary>
        public void NotifyLinkingStatusChanged(bool isLinked, string? email)
        {
            App.LoggingService?.LogInfo($"[SETTINGS] LinkingStatus alterado: IsLinked={isLinked}, Email={email ?? "null"}");
            LinkingStatusChanged?.Invoke(this, (isLinked, email));
        }
    }

    public enum IntelligentProfileType
    {
        GamerCompetitive,      // Gamer Competitivo
        GamerSinglePlayer,     // Gamer SinglePlayer
        GamerSimulation,       // Gamer Simulação
        GamerMMO,              // Gamer MMO / RPG
        GamerStrategy,         // Gamer Estratégia
        WorkOffice,           // Empresarial Escritório
        CreativeVideoEditing, // Edição de Vídeo / Design
        DeveloperProgramming, // Desenvolvimento / Programação
        GeneralBalanced,      // Uso Geral Balanceado
        EnterpriseSecure      // Enterprise Seguro (Corporativo)
    }

    public class AppSettings
    {
        // Versionamento de Schema
        public int SchemaVersion { get; set; } = 1;
        public string LastSavedVersion { get; set; } = "1.0.0.0";

        // Geral
        public Language Language { get; set; } = Language.Portuguese;
        public bool LanguageDetected { get; set; } = false;
        public string Theme { get; set; } = "Dark";
        public IntelligentProfileType IntelligentProfile { get; set; } = IntelligentProfileType.GeneralBalanced;

        /// <summary>
        /// [FIX:STRATEGY-3-STATES] A estratégia escolhida em "Como prefere que o
        /// sistema se comporte?", guardada como TOKEN estável em português.
        ///
        /// Fica como texto (e não como enum) pelo mesmo motivo dos tokens de
        /// perfil: o valor gravado é o contrato entre a tela e o motor, e não deve
        /// mudar quando o texto exibido é traduzido. A conversão para os três
        /// estados reais é feita por
        /// <c>Services.Performance.PerformanceStrategyMap.ToStrategy</c>.
        ///
        /// Vazio significa "nada escolhido ainda", e aí vale Equilibrado.
        /// </summary>
        public string PerformanceStrategyToken { get; set; } = string.Empty;
        public bool EnableValidationMode { get; set; } = false;
        public bool StartWithWindows { get; set; } = true;

        /// <summary>
        /// [FIX:STARTUP-VISIBILIDADE] Padrão DESMARCADO (false).
        ///
        /// Antes o padrão era true, e como "Iniciar com o Windows" também é true
        /// por padrão, o app subia direto para a bandeja em TODO lançamento — o
        /// usuário clicava no executável para abrir a interface e a janela não
        /// aparecia, sem forma de pedir para aparecer.
        ///
        /// A distinção agora é feita por origem, não por um único booleano
        /// misturado: o auto-start do Windows sobe para a bandeja (ver
        /// StartupAutostartDetector), e esta opção passa a ser a ESCOLHA de quem
        /// quer a bandeja também nos lançamentos manuais. Quem não marca, vê a
        /// janela.
        /// </summary>
        public bool StartMinimized { get; set; } = false;
        public bool EnableTransparency { get; set; } = true;
        public string DashboardLayout { get; set; } = "Circular"; // "Circular" (novo modo central) ou "Classic" (interface antiga)
        public bool MinimizeToTray { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool SidebarCollapsed { get; set; } = true;
        public bool VoiceControlEnabled { get; set; } = false;
        /// <summary>
        /// Chave da API OpenAI.
        /// <para>
        /// Não é mais serializada em texto puro. O valor em memória vive só
        /// aqui; o que vai para settings.json é <see cref="OpenAIApiKeyEnc"/>,
        /// protegido com DPAPI (CurrentUser) — mesma conta do Windows, mesma
        /// máquina. Antes, settings.json continha a chave em claro, ao lado do
        /// device_credential.bin que JÁ era criptografado: incoherentente.
        /// </para>
        /// <para>
        /// Transparente para quem chama: os ~10 pontos de uso continuam apenas
        /// lendo e atribuindo <c>OpenAIApiKey</c>.
        /// </para>
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string OpenAIApiKey
        {
            get => VoltrisOptimizer.Core.Security.SecureConfigProtection.UnprotectSecret(OpenAIApiKeyEnc) ?? "";
            set => OpenAIApiKeyEnc = VoltrisOptimizer.Core.Security.SecureConfigProtection.ProtectSecret(value) ?? "";
        }

        /// <summary>Blob DPAPI (Base64) da chave OpenAI. É isto que vai ao disco.</summary>
        public string OpenAIApiKeyEnc { get; set; } = "";
        
        // Onboarding
        public bool IsFirstRun { get; set; } = true;
        public bool OnboardingCompleted { get; set; } = false;
        
        // ── TRIAL REMOVIDO ───────────────────────────────────────────────
        // O produto é GRATUITO: não existe prazo, expiração nem dias
        // restantes. Apenas recursos PRO exigem licença, e esse gate segue
        // normal.
        //
        // Estes 4 campos são INERTES. Mantidos para não quebrar a
        // desserialização de settings.json já gravado em disco e para o
        // código legado continuar compilando. Nenhum tem efeito prático:
        // TrialProtectionService e HardwareTrialService respondem "nunca
        // expirado", e IsTrialExpired() ainda zera estes flags se encontrar
        // algum valor ligado vindo de uma versão anterior.
        public DateTime? FirstRunDate { get; set; } = null;
        public double TrialDaysUsedBeforeActivation { get; set; } = 0.0;
        public bool LicensePageShown { get; set; } = false;
        public bool TrialExpired { get; set; } = false;

        // Para testes (DEBUG)
        public bool ForceTrialExpired { get; set; } = false;
        public bool ForcePurchaseModal { get; set; } = false;
        
        // License API
        public string LicenseApiUrl { get; set; } = "https://api.voltris.com/v1/license";
        public string? LicenseKey { get; set; }
        public string? LicenseType { get; set; }
        public DateTime? LicenseExpiresAt { get; set; }
        
        // Empresa / Telemetria
        public bool UseLocalApi { get; set; } = false; // Forçar produção por padrão
        public string? InstallationId { get; set; }
        public bool WelcomePromptShown { get; set; }
        public string? LinkedUserEmail { get; set; }
        public bool IsDeviceLinked { get; set; } = false;
        public DateTime? LinkedAt { get; set; } // Timestamp of when device was linked
        public DateTime? LastLinkCheckAt { get; set; } // Timestamp of last API link status check
        public bool IsLicenseRevoked { get; set; } = false;
        
        // CORREÇÃO ENTERPRISE: Telemetria opt-out
        public bool TelemetryEnabled { get; set; } = true; // Opt-out por padrão (GDPR compliant)
        
        // ============================================
        // Página Personalizar (PersonalizeView)
        // ============================================
        public bool TaskbarCenteringEnabled { get; set; } = false;
        public bool TaskbarStyleEnabled { get; set; } = false;
        public int TaskbarStyleIndex { get; set; } = 0;
        public bool Win11IconsEnabled { get; set; } = false;
        public int TaskbarOpacity { get; set; } = 255;
        
        // Novos: Efeitos Visuais (Persistência)
        public bool PersonalizeWindowAnimations { get; set; } = true;
        public bool PersonalizeMenuAnimations { get; set; } = true;
        public bool PersonalizeTaskbarAnimations { get; set; } = true;
        public bool PersonalizeDropShadows { get; set; } = true;
        public bool PersonalizeFontSmoothing { get; set; } = true;
        public bool PersonalizeTransparencyEffects { get; set; } = true;
        public bool PersonalizeHardwareAcceleration { get; set; } = false;
        public bool PersonalizeExplorerHighPerf { get; set; } = false;
        public bool PersonalizeHardwareScheduling { get; set; } = false;
        public bool PersonalizeGamingGpuPriority { get; set; } = false;
        public bool PersonalizeMpoEnabled { get; set; } = true;
        public PersonalizeProfile PersonalizeProfilePlan { get; set; } = PersonalizeProfile.Normal;

        // VoltrisBlur (Explorer effect)
        public bool VoltrisBlurInstalled { get; set; } = false;
        public int VoltrisBlurEffectIndex { get; set; } = 1;   // 1=Acrylic default
        public bool VoltrisBlurClearAddress { get; set; } = true;
        public bool VoltrisBlurClearBarBg { get; set; } = true;
        public bool VoltrisBlurClearWinUIBg { get; set; } = true;
        public bool VoltrisBlurShowLine { get; set; } = false;
        public int VoltrisBlurAlpha { get; set; } = 120;
        public int VoltrisBlurColorR { get; set; } = 0;
        public int VoltrisBlurColorG { get; set; } = 0;
        public int VoltrisBlurColorB { get; set; } = 0;

        // Cursor Personalizado
        public string SelectedCursorTheme { get; set; } = string.Empty; // "" = padrão do Windows

        // 🔥 PROPRIEDADE ADICIONADA: Configurações de Display Personalizadas
        public Dictionary<string, object> CustomDisplaySettings { get; set; } = new Dictionary<string, object>();

        // ============================================
        // Página de Limpeza (CleanupView)
        // ============================================
        public bool CleanCache { get; set; } = true;
        public bool CleanTemp { get; set; } = true;
        public bool CleanRecycle { get; set; } = true;
        public bool CleanThumbnails { get; set; } = true;
        public bool CleanBrowsers { get; set; } = true;
        public bool CleanLogs { get; set; } = false;
        public bool CleanPrefetch { get; set; } = false;
        public bool CleanWindowsUpdate { get; set; } = false;
        public Dictionary<string, bool> CleanupItemSelections { get; set; } = new();
        
        // ============================================
        // Página de Desempenho (PerformanceView)
        // ============================================
        public bool OptimizeStartup { get; set; } = true;
        public bool OptimizeServices { get; set; } = true;
        public bool OptimizeVisualEffects { get; set; } = true;
        public bool OptimizePower { get; set; } = true;
        public bool OptimizeMemory { get; set; } = true;
        public bool OptimizeDisk { get; set; } = true;
        
        // ============================================
        // Página de Rede (NetworkView)
        // ============================================
        public bool OptimizeDns { get; set; } = true;
        public bool OptimizeTcp { get; set; } = true;
        public bool OptimizeWinsock { get; set; } = true;
        public bool OptimizeNetworkAdapter { get; set; } = true;
        
        // ============================================
        // Página de Sistema (SystemView)
        // ============================================
        public bool OptimizeRegistry { get; set; } = false;
        public bool OptimizeTelemetry { get; set; } = true;
        public bool OptimizePrivacy { get; set; } = true;
        public bool OptimizeDefender { get; set; } = false;
        public bool OptimizeIndexing { get; set; } = true;
        public bool OptimizeSuperfetch { get; set; } = false;
        
        // ============================================
        // Página Gamer (GamerView)
        // ============================================
        public bool AutoGamerMode { get; set; } = true; // Ativado por padrão
        public bool HasGamerModeConfigured { get; set; } = false; // Novo: Indica se o usuário já definiu a preferência
        public bool GamerOptimizeCpu { get; set; } = true;
        public bool GamerOptimizeGpu { get; set; } = true;
        public bool GamerOptimizeRam { get; set; } = true;
        public bool GamerOptimizeNetwork { get; set; } = true;
        public bool GamerCloseBackground { get; set; } = true;
        public bool GamerDisableEffects { get; set; } = true;

        // Otimizações Avançadas
        public bool EnableDynamicLoadStabilizer { get; set; } = true; // Ativado por padrão
        public Optimization.DlsPolicy DlsPolicy { get; set; } = Optimization.DlsPolicy.Auto;
        public List<string> DlsWhitelist { get; set; } = new List<string>();
        public List<string> DlsBlacklist { get; set; } = new List<string>();
        public string? DlsManualGameProcess { get; set; }
        
        // DPC Watchdog
        public bool AllowBackgroundDpcWatchdog { get; set; } = true; // Ativado por padrão
        
        // ============================================
        // Smart App Focus Engine (Foco Inteligente)
        // ============================================
        public bool EnableSmartFocus { get; set; } = true; // Ativado por padrão
        public int SmartFocusCpuPriority { get; set; } = 2; // 0=Normal, 1=AboveNormal, 2=High
        public int SmartFocusIoPriority { get; set; } = 2; // 0=VeryLow, 1=Low, 2=Normal, 3=High, 4=Critical
        public bool EnableFocusMetrics { get; set; } = true; // Coletar métricas de frame-time/latência
        public int FocusNotificationCooldownMinutes { get; set; } = 5; // Cooldown de notificação
        public bool SmartFocusNotificationShownOnce { get; set; } = false; // Aviso único profissional (não spam) do Foco Inteligente
        public List<string> SmartFocusProtectedProcesses { get; set; } = new List<string>(); // Processos customizados para NÃO boostar
        public List<string> SmartFocusBoostAllowList { get; set; } = new List<string>(); // Processos para SEMPRE boostar (mesmo se protegidos)
        public int FocusBoostCooldownMs { get; set; } = 2000; // Cooldown entre boosts do mesmo processo (ms)
        
        // ============================================
        // Notificações do Sistema
        // ============================================
        public bool NotificationsEnabled { get; set; } = true;
        public bool NotifyOnThreatDetected { get; set; } = true;
        public bool NotifyOnScanComplete { get; set; } = true;
        public bool NotifyOnNewDevice { get; set; } = true;
        public bool ToastNotificationsMuted { get; set; } = false;
        
        // ============================================
        // Página de Tela (DisplayView)
        // ============================================
        public double DisplayGamma { get; set; } = 1.0;
        public bool DisplayVSyncEnabled { get; set; } = true;

        // Context Menu
        public bool EnableDesktopContextMenu { get; set; } = true;

        // Storage Optimization
        public bool HasOptimizedStorage { get; set; } = false;

        // ============================================
        // Páginas Beta (Recuperar / Drivers)
        // ============================================
        public bool ShowRecoveryPage { get; set; } = false;
        public bool ShowDriversPage { get; set; } = false;

        // ============================================
        // Página de Privacidade (PrivacyView)
        // ============================================
        public bool PrivacyTelemetryDisabled { get; set; } = false;
        public bool PrivacyLocationDisabled { get; set; } = false;
        public bool PrivacyAdvertisingIdDisabled { get; set; } = false;
        public bool PrivacyCortanaDisabled { get; set; } = false;
        public bool PrivacyCoPilotDisabled { get; set; } = false;
        public bool PrivacyRecallDisabled { get; set; } = false;
        public bool PrivacyBluetoothDisabled { get; set; } = false;
        public bool PrivacyHandwritingDisabled { get; set; } = false;
        public bool PrivacyTextInputDisabled { get; set; } = false;
        public bool PrivacyPersonalizationDisabled { get; set; } = false;
        public bool PrivacyActivityUploadDisabled { get; set; } = false;
        public bool PrivacyClipboardSyncDisabled { get; set; } = false;
        public bool PrivacyDiagnosticsToastDisabled { get; set; } = false;
        public bool PrivacyOnlineSpeechDisabled { get; set; } = false;
        public bool PrivacyOneDriveDisabled { get; set; } = false;
        public bool PrivacyActivityFeedDisabled { get; set; } = false;
    }
}
