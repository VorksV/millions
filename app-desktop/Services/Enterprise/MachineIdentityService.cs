using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Enterprise.Models;

namespace VoltrisOptimizer.Services.Enterprise
{
    public class MachineIdentityService
    {
        private readonly ISystemInfoService _systemInfoService;
        private MachineIdentity? _cachedIdentity;
        private static readonly System.Threading.SemaphoreSlim _lock = new(1, 1);

        public MachineIdentityService(ISystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService;
        }

        public async Task<MachineIdentity> GetMachineIdentityAsync()
        {
            if (_cachedIdentity != null) return _cachedIdentity;

            await _lock.WaitAsync();
            try
            {
                if (_cachedIdentity != null) return _cachedIdentity;

                string cacheFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris", "identity_cache.json");
                try
                {
                    if (File.Exists(cacheFile))
                    {
                        string json = await File.ReadAllTextAsync(cacheFile);
                        var cached = JsonSerializer.Deserialize<MachineIdentity>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        if (cached != null && !string.IsNullOrEmpty(cached.MachineId))
                        {
                            // Valida se a InstallationId também bate com as configs (segurança adicional)
                            var currentSettings = SettingsService.Instance.Settings;
                            if (cached.MachineId == currentSettings.InstallationId)
                            {
                                _cachedIdentity = cached;
                                return cached;
                            }
                        }
                    }
                }
                catch { }

                var cpu = await _systemInfoService.GetCpuInfoAsync();
                var ram = await _systemInfoService.GetRamInfoAsync();
                var drives = await _systemInfoService.GetDrivesInfoAsync();
                var firstDisk = drives != null && drives.Length > 0 ? drives[0] : null;
                var networks = await _systemInfoService.GetNetworkInfoAsync();
                
                // Tenta pegar o MAC Address mais estável (primeiro ativo)
                string mac = "00:00:00:00:00:00";
                if (networks != null && networks.Length > 0)
                {
                   foreach(var net in networks)
                   {
                       if(!string.IsNullOrEmpty(net.MacAddress))
                       {
                           mac = net.MacAddress;
                           break;
                       }
                   }
                }

                var identity = new MachineIdentity
                {
                    Hostname = Environment.MachineName,
                    OsVersion = Environment.OSVersion.ToString(),
                    CpuModel = (cpu?.Name ?? "Unknown CPU").Replace("™", "").Replace("®", "").Trim(),
                    RamTotalGb = ram != null ? (int)Math.Round(ram.TotalBytes / (1024.0 * 1024 * 1024)) : 0,
                    DiskSerial = ReadPhysicalDiskSerial() ?? firstDisk?.Label ?? "Unknown Disk",
                    MacAddress = mac,
                    Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86"
                };

                // Gerar Hash Único (Machine ID) baseado no InstallationId original se disponível
                // Isso garante compatibilidade com o sistema de vinculação (Welcome screen)

                // Se já temos um ID salvo, validamos se é um UUID v4 RFC 4122 válido.
                // Guid.TryParse aceita qualquer GUID, mas o servidor exige UUID v1-v8 (RFC 4122).
                var settings = SettingsService.Instance.Settings;
                if (!string.IsNullOrEmpty(settings.InstallationId) && IsValidRfc4122Uuid(settings.InstallationId))
                {
                    identity.MachineId = settings.InstallationId;
                }
                else
                {
                    // CORREÇÃO: ID salvo é inválido (ex: versão 0 como 0BBC2D9A-0039-FD42-...).
                    // Gerar novo UUID v4 válido e sobrescrever o ID corrompido nas configurações.
                    var oldId = settings.InstallationId;
                    identity.MachineId = GenerateStableMachineId(identity);
                    
                    // Salvar o ID gerado nas configurações para manter consistência
                    settings.InstallationId = identity.MachineId;
                    SettingsService.Instance.SaveSettings();

                    // Log explícito para rastreabilidade da correção
                    System.Diagnostics.Debug.WriteLine(
                        $"[MachineIdentity] ID inválido detectado e corrigido: '{oldId}' → '{identity.MachineId}'");
                }

                try
                {
                    string dir = Path.GetDirectoryName(cacheFile)!;
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string json = JsonSerializer.Serialize(identity, new JsonSerializerOptions {  PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    await File.WriteAllTextAsync(cacheFile, json);
                }
                catch { }

                _cachedIdentity = identity;
                return identity;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Resolve o installation_id persistido, sem depender do container de DI.
        ///
        /// REGRA: o ID de instalação é PERSISTENTE. Se o valor salvo não é um
        /// UUID RFC 4122 válido, é gerado um novo DETERMINÍSTICO a partir de
        /// traits estaveis da máquina e gravado. Nunca use Guid.NewGuid() aqui:
        /// isso criava uma máquina fantasma nova a cada abertura do app.
        /// </summary>
        public static string ResolvePersistedInstallationId(AppSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.InstallationId) && IsValidRfc4122Uuid(settings.InstallationId))
                return settings.InstallationId;

            var oldId = settings.InstallationId;
            var generated = GenerateDeterministicId();

            settings.InstallationId = generated;
            SettingsService.Instance.SaveSettings();

            System.Diagnostics.Debug.WriteLine(
                $"[MachineIdentity] installation_id inválido ({oldId ?? "(nulo)"}) -> {generated}");

            return generated;
        }

        /// <summary>
        /// UUID v4 determinístico: mesmo hardware => mesmo id, em qualquer
        /// reinstalação. Baseado apenas em traits que não identificam a pessoa.
        /// </summary>
        private static string GenerateDeterministicId()
        {
            var raw = string.Join('|',
                Environment.MachineName.ToUpperInvariant(),
                Environment.OSVersion.Platform.ToString(),
                Environment.Is64BitOperatingSystem ? "x64" : "x86",
                Environment.ProcessorCount.ToString());

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));

            // Ajusta para UUID v4 (versao 4, variante RFC 4122).
            hash[6] = (byte)((hash[6] & 0x0F) | 0x40);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

            var guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, 16);
            return new Guid(guidBytes).ToString();
        }

        private static bool IsValidRfc4122Uuid(string id)
        {
            // Valida UUID RFC 4122: versão 1-8 no nibble de versão, variante 8/9/A/B no nibble de variante
            // Padrão: xxxxxxxx-xxxx-[1-8]xxx-[89abAB]xxx-xxxxxxxxxxxx
            if (!Guid.TryParse(id, out var guid)) return false;
            var bytes = guid.ToByteArray();
            // Em Guid .NET, byte[7] contém o nibble de versão no nibble alto (big-endian no campo time_hi_and_version)
            // Mas Guid.ToByteArray() usa little-endian para os primeiros 3 componentes
            // Verificar via string é mais simples e confiável
            var s = guid.ToString(); // xxxxxxxx-xxxx-Vxxx-Wxxx-xxxxxxxxxxxx
            var versionChar = s[14]; // posição do nibble de versão
            var variantChar = s[19]; // posição do nibble de variante
            var validVersion = versionChar >= '1' && versionChar <= '8';
            var variantLower = char.ToLower(variantChar);
            var validVariant = variantLower == '8' || variantLower == '9' || variantLower == 'a' || variantLower == 'b';
            return validVersion && validVariant;
        }

        /// <summary>
        /// Lê o serial FÍSICO do primeiro disco via WMI (Win32_DiskDrive.SerialNumber).
        /// <para>
        /// Antes este campo usava <c>DriveInfo.Label</c>, que é o NOME DO VOLUME
        /// ("Windows 10"), não o serial do disco. Como quase toda instalação do
        /// Windows tem o volume C: com um rótulo parecido, esse campo não
        /// identificava hardware nenhum — e o nome <c>DiskSerial</c> prometia
        /// algo que não entregava. Pior: é o tipo de campo que amarração de
        /// licença por máquina normalmente usa.
        /// </para>
        /// <para>
        /// O WMI entrega o serial real (ex.: "0025_3852_5141_F279"). Retorna
        /// null quando a consulta falha ou o disco não expõe serial (comum em
        /// RAID, USB e VMs), e aí o chamador cai no rótulo do volume.
        /// </para>
        /// </summary>
        private string? ReadPhysicalDiskSerial()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT SerialNumber FROM Win32_DiskDrive");

                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        var serial = obj["SerialNumber"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(serial))
                        {
                            // "None"/"Not Specified" são placeholders de fabricante.
                            if (serial.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                                serial.Equals("Not Specified", StringComparison.OrdinalIgnoreCase) ||
                                serial.Equals("Default string", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            return serial;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[IDENTITY] WMI de serial de disco indisponível: {ex.Message}");
            }

            return null;
        }

        private string GenerateStableMachineId(MachineIdentity identity)
        {
            // Combina dados imutáveis do hardware
            string rawId = $"{identity.CpuModel}-{identity.MacAddress}";
            
            // Tenta pegar UUID via WMI se possível (mais robusto)
            try
            {
#if  NET8_0_OR_GREATER && WINDOWS
                if (OperatingSystem.IsWindows())
                {
                    using (var searcher = new System.Management.ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct"))
                    {
                        foreach (System.Management.ManagementObject obj in searcher.Get())
                        {
                            var uuid = obj["UUID"]?.ToString();
                            if (!string.IsNullOrEmpty(uuid))
                            {
                                // Se o WMI já retornar um UUID válido, usamos ele diretamente
                                if (Guid.TryParse(uuid, out _))
                                    return uuid;
                                
                                rawId = uuid; 
                            }
                        }
                    }
                }
#endif
            }
            catch { }

            // Gera um UUID v4 determinístico baseado no hash SHA256 do hardware
            // IMPORTANTE: MD5 pode gerar GUIDs com versão inválida (ex: 0xF no nibble de versão)
            // UUID v4 requer: nibble de versão = 4, nibble de variante = 8/9/A/B
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawId));
                hash[6] = (byte)((hash[6] & 0x0F) | 0x40);
                hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
                var guidBytes = new byte[16];
                Array.Copy(hash, guidBytes, 16);
                return new Guid(guidBytes).ToString();
            }
        }

        /// <summary>
        /// Retorna a identidade do cache em memória (se disponível) ou cria um fallback mínimo
        /// usando apenas o InstallationId das Settings (sem WMI, sem I/O pesado).
        /// Usado quando GetMachineIdentityAsync() excede o timeout de startup.
        /// </summary>
        public MachineIdentity GetCachedIdentityOrFallback()
        {
            if (_cachedIdentity != null)
                return _cachedIdentity;

            // Fallback rápido: usar o InstallationId já salvo em disco (SettingsService é síncrono)
            var settings = SettingsService.Instance.Settings;
            var fallbackId = !string.IsNullOrEmpty(settings.InstallationId)
                ? settings.InstallationId
                : Guid.NewGuid().ToString();

            return new MachineIdentity
            {
                MachineId = fallbackId,
                Hostname = Environment.MachineName,
                OsVersion = Environment.OSVersion.ToString(),
                CpuModel = "Unknown (startup timeout)",
                Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86"
            };
        }
    }
}
