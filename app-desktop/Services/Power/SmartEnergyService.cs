using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Core.Constants;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Utils.Win32;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Services.Power
{
    // ─────────────────────────────────────────────────────────────────────────
    // Models
    // ─────────────────────────────────────────────────────────────────────────

    public enum EnergyProfile { Gaming, Work, Economy, UltraPerformance, Balanced, Custom }
    public enum DeviceType { Desktop, Laptop, Unknown }

    public class PowerPlanInfo
    {
        public string Guid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsVoltris { get; set; }
        public bool IsBuiltIn { get; set; }
    }

    public class EnergyRecommendation : System.ComponentModel.INotifyPropertyChanged
    {
        public EnergyProfile Profile { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public int EnergyScore { get; set; }
        public string AccentColor { get; set; } = "#31A8FF";

        // Estado visual de selecionado/ativado no card
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    Debug.WriteLine($"[EnergyRecommendation] IsSelected={value} para Profile={Profile} Title={Title}");
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public class EnergyScore
    {
        public int Overall { get; set; }
        public int Performance { get; set; }
        public int Efficiency { get; set; }
        public int Thermal { get; set; }
        public string Grade => Overall switch { >= 90 => "S", >= 75 => "A", >= 60 => "B", >= 45 => "C", _ => "D" };
        public string GradeColor => Grade switch { "S" => "#FFD700", "A" => "#00FF88", "B" => "#31A8FF", "C" => "#FFAA00", _ => "#FF4466" };
    }

    public class PowerPlanSettings
    {
        public int CpuMinPercent { get; set; } = 5;
        public int CpuMaxPercent { get; set; } = 100;
        public int CpuBoostMode { get; set; } = 2;
        public int DiskTimeoutAc { get; set; } = 0;
        public int DiskTimeoutDc { get; set; } = 20;
        public bool UsbSelectiveSuspend { get; set; } = true;
        public int PcieLinkState { get; set; } = 0;
        public int DisplayTimeoutAc { get; set; } = 15;
        public int DisplayTimeoutDc { get; set; } = 5;
        public int WirelessMode { get; set; } = 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Smart Energy Service
    // ─────────────────────────────────────────────────────────────────────────

    public sealed class SmartEnergyService : IDisposable
    {
        private const string TAG = "[SmartEnergy]";
        private const string VOLTRIS_PREFIX = "Voltris Energy - ";

        private readonly ILoggingService _logger;
        private bool _disposed;

        public event EventHandler<string>? ActivePlanChanged;

        public SmartEnergyService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInfo($"{TAG} [CTOR] SmartEnergyService inicializado (Modo Nativo)");
        }

        // ── Hardware Detection ────────────────────────────────────────────────

        public async Task<DeviceType> DetectDeviceTypeAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (GetSystemPowerStatus(out var power))
                    {
                        // Se a bateria está ausente ou o flag indica NoSystemBattery
                        bool hasBattery = (power.BatteryFlag & 128) == 0;
                        return hasBattery ? DeviceType.Laptop : DeviceType.Desktop;
                    }
                }
                catch { }
                return DeviceType.Desktop;
            });
        }

        public async Task<string> GetCpuNameAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                    return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "AMD/Intel Processor";
                }
                catch { return "Standard CPU"; }
            });
        }

        // ── Plan Management ───────────────────────────────────────────────────

        public async Task<List<PowerPlanInfo>> GetAllPlansAsync()
        {
            return await Task.Run(() =>
            {
                var plans = new List<PowerPlanInfo>();
                try
                {
                    var activeGuid = GetActivePlanGuidSync();
                    uint index = 0;
                    uint bufferSize = 16;
                    IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);

                    try
                    {
                        while (true)
                        {
                            bufferSize = 16;
                            uint res = Utils.Win32.PowerNativeMethods.PowerEnumerate(
                                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                                (uint)16, // ACCESS_SCHEME (16)
                                index, buffer, ref bufferSize);

                            if (res == 259) break; // ERROR_NO_MORE_ITEMS
                            if (res == 0) // SUCCESS
                            {
                                var guid = Marshal.PtrToStructure<Guid>(buffer);
                                string guidStr = guid.ToString().ToLowerInvariant();

                                // Obter nome original
                                string name = GetPlanNameNative(guid);

                                plans.Add(new PowerPlanInfo
                                {
                                    Guid = guidStr,
                                    Name = name,
                                    IsActive = guidStr == activeGuid,
                                    IsVoltris = name.StartsWith(VOLTRIS_PREFIX, StringComparison.OrdinalIgnoreCase),
                                    IsBuiltIn = IsBuiltInPlan(guidStr)
                                });
                            }
                            index++;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }

                    _logger.LogInfo($"{TAG} Listados {plans.Count} planos via API nativa.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{TAG} Erro ao listar planos nativos: {ex.Message}", ex);
                    // Fallback para powercfg se a API falhar por algum motivo obscuro
                    return GetAllPlansLegacy();
                }
                return plans;
            });
        }

        private List<PowerPlanInfo> GetAllPlansLegacy()
        {
            var plans = new List<PowerPlanInfo>();
            try
            {
                var output = RunPowercfg("/list");
                var activeGuid = GetActivePlanGuidSync();
                var lines = output.Split('\n');

                foreach (var line in lines)
                {
                    var match = Regex.Match(line,
                        @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\((.+?)\)");
                    if (!match.Success) continue;

                    var guid = match.Groups[1].Value.ToLowerInvariant();
                    var name = match.Groups[2].Value.Trim().TrimEnd('*').Trim();

                    plans.Add(new PowerPlanInfo
                    {
                        Guid = guid,
                        Name = name,
                        IsActive = guid == activeGuid,
                        IsVoltris = name.StartsWith(VOLTRIS_PREFIX, StringComparison.OrdinalIgnoreCase),
                        IsBuiltIn = IsBuiltInPlan(guid)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} Fallback legacy também falhou: {ex.Message}");
            }
            return plans;
        }

        private string GetPlanNameNative(Guid guid)
        {
            uint bufferSize = 256;
            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
            try
            {
                uint res = Utils.Win32.PowerNativeMethods.PowerReadFriendlyName(
                    IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, buffer, ref bufferSize);

                if (res == 0 && bufferSize > 0)
                {
                    return Marshal.PtrToStringUni(buffer) ?? VoltrisOptimizer.Services.LocalizationService.Instance.GetString("Plano sem nome");
                }
                return VoltrisOptimizer.Services.LocalizationService.Instance.GetString("Plano Genérico");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public async Task<bool> SetActivePlanAsync(string guid)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!Guid.TryParse(guid, out Guid schemeGuid)) return false;

                    if (true)
                    {
                        bool orchestratorResult = ProfilePowerAuthority.RequestProfileApply("SmartEnergy", "pedido legado de troca de plano", _logger);
                        if (orchestratorResult && IsPlanReallyActive(schemeGuid))
                        {
                            _logger.LogSuccess($"{TAG} Plano ativado via orquestrador e CONFIRMADO no Windows: {guid}");
                            ActivePlanChanged?.Invoke(this, guid);
                            return true;
                        }
                        _logger.LogWarning(
                            $"{TAG} Orquestrador não confirmed a ativação de {guid}. " +
                            "Tentando powercfg diretamente.");
                    }

                    // Fallback direto, com verificação real do exit code e read-back.
                    var res = RunPowercfgChecked($"/setactive {guid}");
                    if (!res.Success)
                    {
                        _logger.LogError($"{TAG} powercfg /setactive {guid} falhou (exit={res.ExitCode}): {res.ErrorMessage}");
                        return false;
                    }

                    // Read-back: /setactive com exit 0 ainda pode ter sido ignorado sem
                    // elevacao. Confirma com PowerGetActiveScheme antes de reportar sucesso.
                    if (!IsPlanReallyActive(schemeGuid))
                    {
                        _logger.LogError(
                            $"{TAG} powercfg /setactive retornou sucesso mas o plano ativo do Windows " +
                            $"NÃO é {guid}. Operação não confirmada (provável falta de permissão).");
                        return false;
                    }

                    _logger.LogSuccess($"{TAG} Plano ativado via powercfg e CONFIRMADO no Windows: {guid}");
                    ActivePlanChanged?.Invoke(this, guid);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{TAG} Erro ao ativar plano: {ex.Message}", ex);
                    return false;
                }
            });
        }

        /// <summary>
        /// Confirma, lendo do próprio Windows, qual plano de energia está ativo.
        /// Usado como read-back obrigatório após qualquer ativação.
        /// </summary>
        public static bool IsPlanReallyActive(Guid expected)
        {
            try
            {
                if (Utils.Win32.PowerNativeMethods.PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr) != 0 || ptr == IntPtr.Zero)
                    return false;

                var actual = Marshal.PtrToStructure<Guid>(ptr);
                try { Utils.Win32.PowerNativeMethods.LocalFree(ptr); } catch { }

                _activePlanGuidCache = actual.ToString();
                return actual == expected;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SmartEnergy][IsPlanReallyActive] {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Último GUID ativo lido do Windows (cache para a UI).</summary>
        internal static string? _activePlanGuidCache;

        public async Task<string?> ClonePlanAsync(string sourceGuid, string newName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!Guid.TryParse(sourceGuid, out Guid srcGuid)) return null;

                    uint res = Utils.Win32.PowerNativeMethods.PowerDuplicateScheme(IntPtr.Zero, ref srcGuid, out IntPtr destPtr);
                    if (res == 0 && destPtr != IntPtr.Zero)
                    {
                        var destGuid = Marshal.PtrToStructure<Guid>(destPtr);
                        Utils.Win32.PowerNativeMethods.LocalFree(destPtr);

                        string newGuidStr = destGuid.ToString().ToLowerInvariant();

                        // Renomear o plano
                        IntPtr namePtr = Marshal.StringToHGlobalUni(newName);
                        try
                        {
                            Utils.Win32.PowerNativeMethods.PowerWriteFriendlyName(
                                IntPtr.Zero, ref destGuid, IntPtr.Zero, IntPtr.Zero, 
                                namePtr, (uint)((newName.Length + 1) * 2));
                        }
                        finally { Marshal.FreeHGlobal(namePtr); }

                        _logger.LogSuccess($"{TAG} Plano clonado via API nativa: {newName} ({newGuidStr})");
                        return newGuidStr;
                    }

                    _logger.LogWarning($"{TAG} Falha ao clonar via API (res={res}), tentando powercfg...");
                    return ClonePlanLegacy(sourceGuid, newName);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{TAG} Erro ao clonar plano (Nativo): {ex.Message}", ex);
                    return null;
                }
            });
        }

        private string? ClonePlanLegacy(string sourceGuid, string newName)
        {
            var output = RunPowercfg($"/duplicatescheme {sourceGuid}");
            var match = Regex.Match(output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
            if (match.Success)
            {
                var newGuid = match.Value.ToLowerInvariant();
                RunPowercfg($"/changename {newGuid} \"{newName}\"");
                return newGuid;
            }
            return null;
        }

        public async Task<bool> DeletePlanAsync(string guid)
        {
            _logger.LogInfo($"{TAG} [DeletePlanAsync] INÍCIO => guid={guid}");
            
            if (IsBuiltInPlan(guid)) 
            {
                _logger.LogWarning($"{TAG} [DeletePlanAsync] Tentativa de excluir plano integrado: {guid}");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    if (!Guid.TryParse(guid, out Guid schemeGuid)) 
                    {
                        _logger.LogError($"{TAG} [DeletePlanAsync] GUID inválido: {guid}");
                        return false;
                    }

                    // VERIFICAÇÃO CRÍTICA: Verificar se o plano está ativo antes de tentar excluir
                    string? activePlanGuid = GetActivePlanGuidSync();
                    if (string.Equals(activePlanGuid, guid, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning($"{TAG} [DeletePlanAsync] PLANO ATIVO DETECTADO! Não é possível excluir plano ativo: {guid}");
                        
                        // Tentar mudar para um plano seguro antes de excluir
                        _logger.LogInfo($"{TAG} [DeletePlanAsync] Mudando para plano Balanceado antes de excluir...");
                        var balancedGuid = SystemConstants.PowerPlans.BalancedGuid;
                        bool switched = ProfilePowerAuthority.RequestProfileApply("SmartEnergy_DeleteSafe", "pedido legado de troca de plano", _logger);
                        if (!switched)
                        {
                            _logger.LogError($"{TAG} [DeletePlanAsync] Falha ao mudar plano ativo");
                            return false;
                        }
                        _logger.LogSuccess($"{TAG} [DeletePlanAsync] Plano ativo mudado com sucesso para Balanceado");
                    }

                    // TENTATIVA 1: API Nativa (PowerDeleteScheme)
                    _logger.LogInfo($"{TAG} [DeletePlanAsync] Tentando excluso via API nativa...");
                    uint res = Utils.Win32.PowerNativeMethods.PowerDeleteScheme(IntPtr.Zero, ref schemeGuid);
                    if (res == 0)
                    {
                        _logger.LogSuccess($"{TAG} [DeletePlanAsync] Plano removido via API nativa: {guid}");
                        
                        // VALIDAÇÃO CRÍTICA: Verificar se o plano realmente foi removido
                        if (ValidatePlanRemoved(guid))
                        {
                            _logger.LogSuccess($"{TAG} [DeletePlanAsync] VALIDADO: Plano realmente removido do sistema");
                            return true;
                        }
                        else
                        {
                            _logger.LogWarning($"{TAG} [DeletePlanAsync] FALHA NA VALIDAÇÃO: API retornou sucesso mas plano ainda existe!");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"{TAG} [DeletePlanAsync] Falha na API nativa (res={res}), tentando powercfg...");
                    }

                    // TENTATIVA 2: powercfg.exe /delete COM VALIDAÇÃO
                    _logger.LogInfo($"{TAG} [DeletePlanAsync] Executando powercfg /delete {guid}...");
                    string powercfgOutput = RunPowercfg($"/delete {guid}");
                    
                    // VALIDAÇÃO: Verificar se houve erro no powercfg
                    if (string.IsNullOrWhiteSpace(powercfgOutput))
                    {
                        _logger.LogWarning($"{TAG} [DeletePlanAsync] powercfg não retornou output, possível falha");
                    }
                    else
                    {
                        _logger.LogInfo($"{TAG} [DeletePlanAsync] powercfg output: '{powercfgOutput.Trim()}'");
                    }

                    // VALIDAÇÃO FINAL CRÍTICA: Verificar se o plano realmente foi removido
                    bool actuallyRemoved = ValidatePlanRemoved(guid);
                    if (actuallyRemoved)
                    {
                        _logger.LogSuccess($"{TAG} [DeletePlanAsync] SUCESSO VALIDADO: Plano removido via powercfg: {guid}");
                        return true;
                    }
                    else
                    {
                        _logger.LogError($"{TAG} [DeletePlanAsync] FALHA CRÍTICA: Plano NÃO foi removido após todas as tentativas: {guid}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{TAG} [DeletePlanAsync] Erro ao remover plano: {ex.Message}", ex);
                    return false;
                }
            });
        }

        /// <summary>
        /// VALIDAÇÃO CRÍTICA: Verifica se um plano realmente foi removido do sistema
        /// </summary>
        private bool ValidatePlanRemoved(string guid)
        {
            try
            {
                _logger.LogInfo($"{TAG} [ValidatePlanRemoved] Verificando se plano {guid} foi realmente removido...");
                
                // Método 1: Verificar na lista de planos
                string listOutput = RunPowercfg("/list");
                bool stillInList = listOutput.Contains(guid, StringComparison.OrdinalIgnoreCase);
                
                if (stillInList)
                {
                    _logger.LogWarning($"{TAG} [ValidatePlanRemoved] PLANO AINDA EXISTE na lista: {guid}");
                    return false;
                }

                // Método 2: Tentar obter informações do plano (deve falhar se foi removido)
                try
                {
                    string queryOutput = RunPowercfg($"/query {guid}");
                    if (!string.IsNullOrWhiteSpace(queryOutput))
                    {
                        _logger.LogWarning($"{TAG} [ValidatePlanRemoved] PLANO AINDA RESPONDE ao query: {guid}");
                        return false;
                    }
                }
                catch
                {
                    _logger.LogInfo($"{TAG} [ValidatePlanRemoved] Query falhou (esperado para plano removido): {guid}");
                }

                _logger.LogSuccess($"{TAG} [ValidatePlanRemoved] VALIDADO: Plano realmente removido: {guid}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [ValidatePlanRemoved] Erro na validação: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> RenamePlanAsync(string guid, string newName)
        {
            Debug.WriteLine($"{TAG}[RenamePlanAsync] INÍCIO => guid={guid} newName='{newName}'");
            return await Task.Run(() =>
            {
                try
                {
                    Debug.WriteLine($"{TAG}[RenamePlanAsync] Executando powercfg /changename...");
                    RunPowercfg($"/changename {guid} \"{newName}\"");
                    Debug.WriteLine($"{TAG}[RenamePlanAsync] FIM => true");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG}[RenamePlanAsync] EXCEÇÃO: {ex.GetType().Name} => {ex.Message}");
                    Debug.WriteLine($"{TAG}[RenamePlanAsync] StackTrace: {ex.StackTrace}");
                    _logger.LogError($"{TAG} Erro ao renomear plano: {ex.Message}", ex);
                    return false;
                }
            });
        }

        public async Task<bool> ExportPlanAsync(string guid, string filePath)
        {
            Debug.WriteLine($"{TAG}[ExportPlanAsync] INÍCIO => guid={guid} filePath='{filePath}'");
            return await Task.Run(() =>
            {
                try
                {
                    if (!Guid.TryParse(guid, out _))
                    {
                        _logger.LogWarning($"{TAG} ExportPlanAsync: GUID inválido '{guid}'.");
                        return false;
                    }

                    // Garante que a pasta de destino existe — sem isso o powercfg falha
                    // e a UI recebia sucesso mesmo sem arquivo.
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        _logger.LogWarning($"{TAG} ExportPlanAsync: destino inexistente '{dir}'. Não foi possível exportar.");
                        return false;
                    }

                    // Remove arquivo anterior para que o read-back não valide um arquivo velho.
                    long preexistingSize = 0;
                    if (File.Exists(filePath))
                    {
                        try { preexistingSize = new FileInfo(filePath).Length; File.Delete(filePath); }
                        catch (Exception delEx) { _logger.LogWarning($"{TAG} Não foi possível remover o .pow anterior: {delEx.Message}"); }
                    }

                    Debug.WriteLine($"{TAG}[ExportPlanAsync] Executando powercfg /export...");
                    var res = RunPowercfgChecked($"/export \"{filePath}\" {guid}");

                    if (!res.Success)
                    {
                        _logger.LogError($"{TAG} powercfg /export falhou (exit={res.ExitCode}): {res.ErrorMessage}");
                        return false;
                    }

                    // VERIFICAÇÃO REAL: o arquivo precisa existir e ter conteúdo.
                    // A versão anterior retornava true incondicionalmente.
                    if (!File.Exists(filePath))
                    {
                        _logger.LogError($"{TAG} powercfg /export reportou sucesso, mas o arquivo '{filePath}' não foi criado.");
                        return false;
                    }

                    var info = new FileInfo(filePath);
                    if (info.Length <= 0)
                    {
                        _logger.LogError($"{TAG} powercfg /export criou '{filePath}', mas o arquivo está vazio (0 bytes).");
                        return false;
                    }

                    _logger.LogSuccess($"{TAG} Plano '{guid}' exportado para '{filePath}' ({info.Length} bytes).");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG}[ExportPlanAsync] EXCEÇÃO: {ex.GetType().Name} => {ex.Message}");
                    Debug.WriteLine($"{TAG}[ExportPlanAsync] StackTrace: {ex.StackTrace}");
                    _logger.LogError($"{TAG} Erro ao exportar plano: {ex.Message}", ex);
                    return false;
                }
            });
        }

        public async Task<string?> ImportPlanAsync(string filePath)
        {
            Debug.WriteLine($"{TAG}[ImportPlanAsync] INÍCIO => filePath='{filePath}'");
            return await Task.Run(() =>
            {
                try
                {
                    Debug.WriteLine($"{TAG}[ImportPlanAsync] Executando powercfg /import...");
                    var output = RunPowercfg($"/import \"{filePath}\"");
                    Debug.WriteLine($"{TAG}[ImportPlanAsync] Output: '{output}'");
                    var match = Regex.Match(output,
                        @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
                    if (match.Success)
                    {
                        Debug.WriteLine($"{TAG}[ImportPlanAsync] GUID importado: {match.Value}");
                        return match.Value.ToLowerInvariant();
                    }
                    Debug.WriteLine($"{TAG}[ImportPlanAsync] GUID não encontrado no output");
                    return null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG}[ImportPlanAsync] EXCEÇÃO: {ex.GetType().Name} => {ex.Message}");
                    Debug.WriteLine($"{TAG}[ImportPlanAsync] StackTrace: {ex.StackTrace}");
                    _logger.LogError($"{TAG} Erro ao importar plano: {ex.Message}", ex);
                    return null;
                }
            });
        }

        // ── Smart Recommendations ─────────────────────────────────────────────

        public async Task<List<EnergyRecommendation>> GetRecommendationsAsync()
        {
            Debug.WriteLine($"{TAG}[GetRecommendationsAsync] INÍCIO");
            var deviceType = await DetectDeviceTypeAsync();
            var cpuName = await GetCpuNameAsync();
            var isLaptop = deviceType == DeviceType.Laptop;
            Debug.WriteLine($"{TAG}[GetRecommendationsAsync] deviceType={deviceType} isLaptop={isLaptop} cpuName='{cpuName}'");

            var recs = new List<EnergyRecommendation>
            {
                new()
                {
                    Profile = EnergyProfile.Gaming,
                    Title = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("Modo Gamer"),
                    Description = isLaptop
                        ? VoltrisOptimizer.Services.LocalizationService.Instance.GetString("Máxima performance com proteção térmica para notebooks")
                        : VoltrisOptimizer.Services.LocalizationService.Instance.GetString("CPU e GPU sem limitações — latência mínima para jogos"),
                    Icon = "🎮",
                    EnergyScore = isLaptop ? 72 : 95,
                    AccentColor = "#FF4B6B"
                },
                new()
                {
                    Profile = EnergyProfile.Work,
                    Title = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("Modo Trabalho"),
                    Description = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("Balanceado entre performance e consumo — ideal para produtividade"),
                    Icon = "💼",
                    EnergyScore = 78,
                    AccentColor = "#31A8FF"
                },
                new()
                {
                    Profile = EnergyProfile.Economy,
                    Title = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("Modo Economia"),
                    Description = isLaptop
                        ? VoltrisOptimizer.Services.LocalizationService.Instance.GetString("Maximiza autonomia da bateria com performance adequada")
                        : VoltrisOptimizer.Services.LocalizationService.Instance.GetString("Reduz consumo elétrico mantendo responsividade"),
                    Icon = "🔋",
                    EnergyScore = isLaptop ? 95 : 60,
                    AccentColor = "#00FF88"
                },
                new()
                {
                    Profile = EnergyProfile.UltraPerformance,
                    Title = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("Desempenho Máximo"),
                    Description = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("Desempenho máximo absoluto — sem nenhuma limitação de energia"),
                    Icon = "⚡",
                    EnergyScore = 100,
                    AccentColor = "#FFD700"
                }
            };
            Debug.WriteLine($"{TAG}[GetRecommendationsAsync] FIM => {recs.Count} recomendações geradas");
            return recs;
        }

        // ── One-Click Apply ───────────────────────────────────────────────────

        public async Task<bool> ApplyProfileAsync(EnergyProfile profile)
        {
            Debug.WriteLine($"{TAG}[ApplyProfileAsync] INÍCIO => profile={profile}");
            _logger.LogInfo($"{TAG} Aplicando perfil: {profile}");

            Debug.WriteLine($"{TAG}[ApplyProfileAsync] Determinando GUID alvo para perfil {profile}...");
            var targetGuid = profile switch
            {
                EnergyProfile.Gaming          => SystemConstants.PowerPlans.HighPerformance,
                EnergyProfile.Work            => SystemConstants.PowerPlans.Balanced,
                EnergyProfile.Economy         => SystemConstants.PowerPlans.PowerSaver,
                EnergyProfile.UltraPerformance => await EnsureUltimatePerformanceAsync(),
                EnergyProfile.Balanced        => SystemConstants.PowerPlans.Balanced,
                _                             => SystemConstants.PowerPlans.Balanced
            };
            Debug.WriteLine($"{TAG}[ApplyProfileAsync] GUID alvo: {targetGuid}");

            Debug.WriteLine($"{TAG}[ApplyProfileAsync] Chamando SetActivePlanAsync...");
            var success = await SetActivePlanAsync(targetGuid);
            Debug.WriteLine($"{TAG}[ApplyProfileAsync] SetActivePlanAsync retornou: {success}");

            if (success && profile == EnergyProfile.Gaming)
            {
                Debug.WriteLine($"{TAG}[ApplyProfileAsync] Perfil Gaming — aplicando tweaks de gaming...");
                await ApplyGamingTweaksAsync();
                Debug.WriteLine($"{TAG}[ApplyProfileAsync] Gaming tweaks aplicados");
            }
            else if (success && profile == EnergyProfile.UltraPerformance)
            {
                Debug.WriteLine($"{TAG}[ApplyProfileAsync] Perfil UltraPerformance — aplicando tweaks ultra...");
                await ApplyUltraPerformanceTweaksAsync();
                Debug.WriteLine($"{TAG}[ApplyProfileAsync] Ultra tweaks aplicados");
            }
            else
            {
                Debug.WriteLine($"{TAG}[ApplyProfileAsync] Nenhum tweak adicional para perfil {profile}");
            }

            Debug.WriteLine($"{TAG}[ApplyProfileAsync] FIM => {success}");
            return success;
        }

        // ── Energy Score ──────────────────────────────────────────────────────

        public async Task<EnergyScore> CalculateEnergyScoreAsync()
        {
            Debug.WriteLine($"{TAG}[CalculateEnergyScoreAsync] INÍCIO");
            return await Task.Run(async () =>
            {
                Debug.WriteLine($"{TAG}[CalculateEnergyScoreAsync] Obtendo GUID ativo...");
                var activeGuid = GetActivePlanGuidSync();
                Debug.WriteLine($"{TAG}[CalculateEnergyScoreAsync] GUID ativo: {activeGuid}");

                Debug.WriteLine($"{TAG}[CalculateEnergyScoreAsync] Obtendo frequência atual e máxima da CPU...");
                var cpuFreq = await GetCurrentCpuFrequencyMhzAsync();
                var maxFreq = await GetMaxCpuFrequencyMhzAsync();
                Debug.WriteLine($"{TAG}[CalculateEnergyScoreAsync] CpuFreq={cpuFreq} MHz MaxFreq={maxFreq} MHz");

                int perfScore = activeGuid switch
                {
                    var g when g == SystemConstants.PowerPlans.UltimatePerformance => 100,
                    var g when g == SystemConstants.PowerPlans.HighPerformance      => 90,
                    var g when g == SystemConstants.PowerPlans.Balanced             => 65,
                    var g when g == SystemConstants.PowerPlans.PowerSaver           => 35,
                    _                                                               => 70
                };
                Debug.WriteLine($"{TAG}[CalculateEnergyScoreAsync] perfScore={perfScore}");

                int effScore = activeGuid switch
                {
                    var g when g == SystemConstants.PowerPlans.PowerSaver           => 95,
                    var g when g == SystemConstants.PowerPlans.Balanced             => 75,
                    var g when g == SystemConstants.PowerPlans.HighPerformance      => 50,
                    var g when g == SystemConstants.PowerPlans.UltimatePerformance  => 30,
                    _                                                               => 65
                };
                Debug.WriteLine($"{TAG}[CalculateEnergyScoreAsync] effScore={effScore}");

                int thermalScore = maxFreq > 0 ? (int)((double)cpuFreq / maxFreq * 100) : 70;
                thermalScore = Math.Clamp(thermalScore, 0, 100);
                Debug.WriteLine($"{TAG}[CalculateEnergyScoreAsync] thermalScore={thermalScore}");

                var score = new EnergyScore
                {
                    Performance = perfScore,
                    Efficiency  = effScore,
                    Thermal     = thermalScore,
                    Overall     = (perfScore + effScore + thermalScore) / 3
                };
                Debug.WriteLine($"{TAG}[CalculateEnergyScoreAsync] FIM => Overall={score.Overall} Grade={score.Grade}");
                return score;
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        public string GetActivePlanGuidSync()
        {
            try
            {
                uint res = Utils.Win32.PowerNativeMethods.PowerGetActiveScheme(IntPtr.Zero, out IntPtr guidPtr);
                if (res == 0 && guidPtr != IntPtr.Zero)
                {
                    var guid = Marshal.PtrToStructure<Guid>(guidPtr);
                    Utils.Win32.PowerNativeMethods.LocalFree(guidPtr);
                    return guid.ToString().ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} Erro ao obter plano ativo via API: {ex.Message}");
            }
            return string.Empty;
        }

        public async Task<int> GetCurrentCpuFrequencyMhzAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    Debug.WriteLine($"{TAG}[GetCurrentCpuFrequencyMhzAsync] Iniciando leitura via NtPowerInformation...");
                    int processorCount = Environment.ProcessorCount;
                    uint size = (uint)(Marshal.SizeOf<Utils.Win32.PowerNativeMethods.ProcessorPowerInformation>() * processorCount);
                    IntPtr buffer = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        uint res = Utils.Win32.PowerNativeMethods.CallNtPowerInformation(
                            11, // ProcessorInformation
                            IntPtr.Zero, 0,
                            buffer, size);

                        if (res == 0)
                        {
                            var info = Marshal.PtrToStructure<Utils.Win32.PowerNativeMethods.ProcessorPowerInformation>(buffer);
                            Debug.WriteLine($"{TAG}[GetCurrentCpuFrequencyMhzAsync] Sucesso: {info.CurrentMhz} MHz");
                            return (int)info.CurrentMhz;
                        }
                        Debug.WriteLine($"{TAG}[GetCurrentCpuFrequencyMhzAsync] Falha na chamada nativa: res={res}");
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                catch { }
                return 0;
            });
        }

        public async Task<int> GetMaxCpuFrequencyMhzAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    int processorCount = Environment.ProcessorCount;
                    uint size = (uint)(Marshal.SizeOf<Utils.Win32.PowerNativeMethods.ProcessorPowerInformation>() * processorCount);
                    IntPtr buffer = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        if (Utils.Win32.PowerNativeMethods.CallNtPowerInformation(11, IntPtr.Zero, 0, buffer, size) == 0)
                        {
                            var info = Marshal.PtrToStructure<Utils.Win32.PowerNativeMethods.ProcessorPowerInformation>(buffer);
                            return (int)info.MaxMhz;
                        }
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                catch { }
                return 0;
            });
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus lpSystemPowerStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        private async Task<string> EnsureUltimatePerformanceAsync()
        {
            Debug.WriteLine($"{TAG}[EnsureUltimatePerformanceAsync] INÍCIO");
            return await Task.Run(() =>
            {
                Debug.WriteLine($"{TAG}[EnsureUltimatePerformanceAsync] Verificando se Ultimate Performance já existe...");
                var output = RunPowercfg("/list");

                // Verifica se já existe um GUID ativo com o alias exato
                if (output.Contains(SystemConstants.PowerPlans.UltimatePerformance, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"{TAG}[EnsureUltimatePerformanceAsync] Ultimate Performance Alias GUID já existe (raro), retornando.");
                    return SystemConstants.PowerPlans.UltimatePerformance;
                }

                // Verifica se já existe algum plano gerado com nome 'Ultimate Performance' ou 'Desempenho Máximo'
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (Regex.IsMatch(line, @"\(\s*(Ultimate Performance|Desempenho Máximo|Desempenho Ultimate)\s*\)", RegexOptions.IgnoreCase))
                    {
                        var m = Regex.Match(line, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
                        if (m.Success)
                        {
                            var existingGuid = m.Value.ToLowerInvariant();
                            Debug.WriteLine($"{TAG}[EnsureUltimatePerformanceAsync] Plano já existente encontrado via nome: {existingGuid}");
                            return existingGuid;
                        }
                    }
                }

                Debug.WriteLine($"{TAG}[EnsureUltimatePerformanceAsync] Não encontrado — provisionando via duplicatescheme...");
                var dupOutput = RunPowercfg($"/duplicatescheme {SystemConstants.PowerPlans.UltimatePerformance}");
                var match = Regex.Match(dupOutput, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
                if (match.Success)
                {
                    var newGuid = match.Value.ToLowerInvariant();
                    Debug.WriteLine($"{TAG}[EnsureUltimatePerformanceAsync] FIM => Novo plano gerado GUID: {newGuid}");
                    return newGuid;
                }

                Debug.WriteLine($"{TAG}[EnsureUltimatePerformanceAsync] Falha ao extrair GUID, retornando alias fallback.");
                return SystemConstants.PowerPlans.UltimatePerformance;
            });
        }

        private async Task ApplyGamingTweaksAsync()
        {
            Debug.WriteLine($"{TAG}[ApplyGamingTweaksAsync] INÍCIO");
            await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo($"{TAG} [GamingTweaks] Perfil Gamer selecionado — usando plano nativo do Windows sem modificações diretas.");
                    _logger.LogInfo($"{TAG} [GamingTweaks] Energia gerenciada naturalmente pelo Windows para estabilidade máxima.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG}[ApplyGamingTweaksAsync] EXCEÇÃO (parcial): {ex.GetType().Name} => {ex.Message}");
                    _logger.LogWarning($"{TAG} Gaming tweaks parciais: {ex.Message}");
                }
            });
        }

        private async Task ApplyUltraPerformanceTweaksAsync()
        {
            Debug.WriteLine($"{TAG}[ApplyUltraPerformanceTweaksAsync] INÍCIO");
            await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo($"{TAG} [UltraTweaks] Perfil Ultra Performance selecionado — usando plano nativo do Windows sem modificações no registro.");
                    _logger.LogInfo($"{TAG} [UltraTweaks] Energia gerenciada naturalmente pelo Windows para estabilidade máxima.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG}[ApplyUltraPerformanceTweaksAsync] EXCEÇÃO: {ex.GetType().Name} => {ex.Message}");
                    _logger.LogWarning($"{TAG} Ultra tweaks parciais: {ex.Message}");
                }
            });
        }

        private static bool IsBuiltInPlan(string guid)
        {
            var result =
                guid.Equals(SystemConstants.PowerPlans.Balanced, StringComparison.OrdinalIgnoreCase) ||
                guid.Equals(SystemConstants.PowerPlans.HighPerformance, StringComparison.OrdinalIgnoreCase) ||
                guid.Equals(SystemConstants.PowerPlans.PowerSaver, StringComparison.OrdinalIgnoreCase) ||
                guid.Equals(SystemConstants.PowerPlans.UltimatePerformance, StringComparison.OrdinalIgnoreCase);
            Debug.WriteLine($"[SmartEnergy][IsBuiltInPlan] guid={guid} => {result}");
            return result;
        }

        /// <summary>
        /// Resultado real de uma chamada ao powercfg.exe.
        /// <see cref="Success"/> só é true quando o processo realmente terminou
        /// com código 0 — antes, todas as chamadas inferiam sucesso apenas da
        /// ausência de exceção, o que fazia a interface mentir.
        /// </summary>
        internal readonly struct PowercfgResult
        {
            public bool Success { get; init; }
            public int ExitCode { get; init; }
            public bool TimedOut { get; init; }
            public string StdOut { get; init; }
            public string StdErr { get; init; }

            public string ErrorMessage
            {
                get
                {
                    if (TimedOut) return "powercfg.exe excedeu o tempo limite de execução.";
                    if (!string.IsNullOrWhiteSpace(StdErr)) return StdErr.Trim();
                    if (!Success) return $"powercfg.exe retornou código de saída {ExitCode}.";
                    return string.Empty;
                }
            }
        }

        /// <summary>
        /// Executa powercfg.exe e devolve stdout (usado por quem só le saída, ex.: /list).
        /// Prefira <see cref="RunPowercfgChecked"/> sempre que a operação alterar o sistema.
        /// </summary>
        internal static string RunPowercfg(string args)
        {
            var result = RunPowercfgChecked(args);
            return result.Success ? result.StdOut : string.Empty;
        }

        /// <summary>
        /// Executa powercfg.exe e devolve o desfecho REAL (exit code, stderr, timeout).
        /// Corriges três defeitos da versão anterior:
        ///  1. engolia toda exceção e devolvia string.Empty (callers assumiam sucesso);
        ///  2. lia stdout e stderr SEQUENCIALMENTE, com risco de deadlock quando o
        ///     buffer de stderr enche antes de stdout ser lido;
        ///  3. ignorava o resultado de WaitForExit e acessava ExitCode mesmo em timeout
        ///     (o que lançava InvalidOperationException e era engolido).
        /// </summary>
        internal static PowercfgResult RunPowercfgChecked(string args)
        {
            Debug.WriteLine($"[SmartEnergy][RunPowercfg] IN�CIO => args='{args}'");

            // [FIX:POWER-GATE] ESTE É O SEGUNDO CAMINHO DE ESCRITA, E O MAIS
            // PERIGOSO.
            //
            // Treze lugares do app gravam energia chamando `powercfg.exe`
            // diretamente, e NENHUM deles passa pela API nativa — portanto
            // o portão ligado em `PowerNativeMethods` não os alcança. É por
            // aqui que o Brain trocava o plano por um nativo do Windows
            // (`powercfg /setactive`), tirando do ar o plano gerenciado e
            // tornando inertes todos os valores gravados nele.
            //
            // Este é o wrapper de TODOS os `RunPowercfg`, então filtrar aqui
            // cobre o segundo caminho de uma vez só.
            if (PowerWriteGate.IsBlockedByGate(args, out string gateReason, out bool isPlanSwitch))
            {
                App.LoggingService?.LogWarning(
                    $"[PowerGate] powercfg BLOQUEADO: '{args.Trim()}' -> {gateReason}");

                return new PowercfgResult
                {
                    Success = false,
                    ExitCode = 5,
                    StdOut = string.Empty,
                    StdErr = $"powercfg bloqueado pelo PowerWriteGate: {gateReason}"
                };
            }

            if (isPlanSwitch)
            {
                // Troca de plano é legítima (Gamer Mode, Brain), mas precisa
                // ficar registrada: é ela que tira o plano gerenciado do ar.
                PowerWriteGate.NotifyPlanSwitchAttempt(
                    "powercfg", args.Trim(), App.LoggingService);
            }

            try
            {
                if (args.Contains("SCHEME_CURRENT", StringComparison.OrdinalIgnoreCase) &&
                    args.Contains("setacvalueindex", StringComparison.OrdinalIgnoreCase))
                {
                    bool isLaptop = false;
                    try
                    {
                        var sysInfo = App.Services?.GetService(typeof(ISystemInfoService)) as ISystemInfoService;
                        isLaptop = sysInfo?.IsLaptop() ?? false;
                    }
                    catch { }

                    if (isLaptop)
                    {
                        Debug.WriteLine($"[SmartEnergy][RunPowercfg] BLOQUEADO: Laptop detectado, ignorando modificação no SCHEME_CURRENT.");
                        return new PowercfgResult
                        {
                            Success = false,
                            ExitCode = -1,
                            TimedOut = false,
                            StdOut = string.Empty,
                            StdErr = "Operação não aplicada: bloqueada em notebooks para preservar o plano Balanced nativo."
                        };
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null)
                {
                    Debug.WriteLine($"[SmartEnergy][RunPowercfg] Process.Start retornou null para args='{args}'");
                    return new PowercfgResult
                    {
                        Success = false, ExitCode = -1, TimedOut = false,
                        StdOut = string.Empty, StdErr = "Não foi possível iniciar powercfg.exe."
                    };
                }

                // Leitura ASSÍNCRONA dos dois pipes: evita o deadlock clássico.
                var tOut = p.StandardOutput.ReadToEndAsync();
                var tErr = p.StandardError.ReadToEndAsync();

                bool exited = p.WaitForExit(15_000);
                if (!exited)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    return new PowercfgResult
                    {
                        Success = false, ExitCode = -1, TimedOut = true,
                        StdOut = SafeRead(tOut), StdErr = "Tempo limite de 15s excedido."
                    };
                }

                string output = SafeRead(tOut);
                string error = SafeRead(tErr);
                int exitCode = p.ExitCode;

                Debug.WriteLine($"[SmartEnergy][RunPowercfg] ExitCode={exitCode} OutputLen={output.Length} ErrorLen={error.Length}");
                if (!string.IsNullOrWhiteSpace(error))
                    Debug.WriteLine($"[SmartEnergy][RunPowercfg] STDERR: {error.Trim()}");
                Debug.WriteLine($"[SmartEnergy][RunPowercfg] FIM => output='{output.Trim()}'");

                return new PowercfgResult
                {
                    Success = exitCode == 0,
                    ExitCode = exitCode,
                    TimedOut = false,
                    StdOut = output,
                    StdErr = error
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SmartEnergy][RunPowercfg] EXCEÇÃO: {ex.GetType().Name} => {ex.Message}");
                return new PowercfgResult
                {
                    Success = false, ExitCode = -1, TimedOut = false,
                    StdOut = string.Empty, StdErr = $"{ex.GetType().Name}: {ex.Message}"
                };
            }
        }

        private static string SafeRead(Task<string> t)
        {
            try { return t.GetAwaiter().GetResult() ?? string.Empty; }
            catch { return string.Empty; }
        }

        public void Dispose()
        {
            Debug.WriteLine($"{TAG}[Dispose] Dispose chamado, _disposed={_disposed}");
            _disposed = true;
            Debug.WriteLine($"{TAG}[Dispose] FIM");
        }
    }
}
