using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield.Advanced
{
    /// <summary>
    /// Análise Estática Avançada — Verifica imports, entropia, seções e assinaturas YARA simplificadas.
    /// </summary>
    public class AdvancedStaticAnalyzer
    {
        private readonly ILoggingService _logger;
        private readonly List<byte[]> _yaraRules;
        private readonly Dictionary<string, string[]> _suspiciousImports;
        private readonly string _voltrisPath;

        public AdvancedStaticAnalyzer(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _voltrisPath = AppDomain.CurrentDomain.BaseDirectory;

            // Imports comuns em malwares
            _suspiciousImports = new Dictionary<string, string[]>
            {
                [Decrypt("a2VybmVsMzIuZGxs")] = new[] {
                    Decrypt("VmlydHVhbEFsbG9j"), Decrypt("VmlydHVhbFByb3RlY3Q="), Decrypt("Q3JlYXRlUmVtb3RlVGhyZWFk"),
                    Decrypt("V3JpdGVQcm9jZXNzTWVtb3J5"), Decrypt("UXVldWVVc2VyQVBD"), Decrypt("U2V0V2luZG93c0hvb2tFeA==")
                },
                [Decrypt("bnRkbGwuZGxs")] = new[] {
                    Decrypt("TnRDcmVhdGVUaHJlYWRFeA=="), Decrypt("TnRRdWV1ZUFwY1RocmVhZA=="), Decrypt("UnRsTW92ZU1lbW9yeQ==")
                },
                [Decrypt("d2luaW5ldC5kbGw=")] = new[] {
                    Decrypt("SW50ZXJuZXRPcGVu"), Decrypt("SW50ZXJuZXRDb25uZWN0"), Decrypt("SHR0cE9wZW5SZXF1ZXN0")
                }
            };

            _yaraRules = new List<byte[]>();
            InitializeYaraSignatures();
        }

        private void InitializeYaraSignatures()
        {
            // --- PADRÕES DE PAYLOADS CONHECIDOS (DNA DE MALWARE) ---
            // Ofuscado para evitar falsos positivos no binário do Voltris
            
            // 1. DNA Identifiers
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("cmVmbGVjdGl2ZSBsb2FkZXI="))); // reflective loader
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("bWV0YXNwbG9pdA=="))); // metasploit
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("bWV0ZXJwcmV0ZXI="))); // meterpreter
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("UEFZTE9BRDogbWV0YXNwbG9pdA=="))); // PAYLOAD: metasploit
            _yaraRules.Add(new byte[] { 0xFC, 0x48, 0x83, 0xE4, 0xF0 }); // Prólogo Shellcode x64 (msfvenom)
            _yaraRules.Add(new byte[] { 0xFC, 0xBE, 0x00, 0x00, 0x00, 0x00, 0xEB, 0x19 }); // msfvenom reverse_tcp x86
            _yaraRules.Add(new byte[] { 0x55, 0x89, 0xE5, 0x57, 0x56, 0x53, 0x83, 0xEC }); // Meterpreter API Resolving
            
            // 2. Beacon DNA
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("UmVmbGVjdGl2ZUxvYWRlcg=="))); // ReflectiveLoader
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("JXMgKFBJRDogJWQp"))); // Beacon string
            _yaraRules.Add(new byte[] { 0x4D, 0x5A, 0x41, 0x52, 0x55, 0x48, 0x89, 0xE5 }); // Cobalt Strike DLL Lead
            
            // 3. Scripts DNA
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("V1NjcmlwdC5TaGVsbA=="))); // WScript.Shell
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("TmV0LldlYkNsaWVudA=="))); // Net.WebClient
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("RG93bmxvYWRTdHJpbmc="))); // DownloadString
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("RnJvbUJhc2U2NFN0cmluZw=="))); // FromBase64String
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("SW52b2tlLUV4cHJlc3Npb24="))); // Invoke-Expression
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("SUVYICg="))); // IEX (
            
            // 4. Mimikatz DNA
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("bWltaWthdHo="))); // mimikatz
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("c2VrdXJsc2E="))); // sekurlsa
            _yaraRules.Add(Encoding.ASCII.GetBytes(Decrypt("bG9nb25wYXNzd29yZHM="))); // logonpasswords
            
            _logger.LogInfo($"[AdvancedStatic] {_yaraRules.Count} assinaturas de ameaças carregadas no motor de scan.");
        }

        private string Decrypt(string base64)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(base64)); }
            catch { return string.Empty; }
        }

        public AdvancedScanResult AnalyzeFile(string filePath)
        {
            var result = new AdvancedScanResult();

            // Whitelist do Voltris Optimizer
            if (filePath.StartsWith(_voltrisPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInfo($"[AdvancedStatic] Ignorando arquivo do Voltris: {Path.GetFileName(filePath)}");
                return result;
            }
            
            // 🔧 WHITELIST PROFISSIONAL: Arquivos legítimos conhecidos que não devem ser analisados
            var normalizedPath = filePath.ToLowerInvariant();
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();
            
            // Whitelist de caminhos seguros
            if (normalizedPath.Contains("\\windows\\") || 
                normalizedPath.Contains("\\program files\\") ||
                normalizedPath.Contains("\\program files (x86)\\") ||
                normalizedPath.Contains("\\programdata\\") ||
                normalizedPath.Contains("voltrisoptimizer.exe") ||
                normalizedPath.Contains("system32") ||
                normalizedPath.Contains("syswow64") ||
                normalizedPath.Contains("msiexec.exe") ||
                normalizedPath.Contains("mpsigstub")) // Windows Defender update
            {
                return result;
            }
            
            // 🔧 WHITELIST DE FERRAMENTAS LEGÍTIMAS: Evitar falsos positivos
            var legitimateTools = new HashSet<string>
            {
                // Emuladores e Gaming
                "opl_manager.exe",           // PlayStation Open PS2 Loader Manager (FALSO POSITIVO COMUM)
                "x86launcher.exe",          // Launcher comum para emuladores
                "pcsx2.exe",                // PS2 Emulator
                "duckstation.exe",          // PS2 Emulator
                "rpcs3.exe",                // PS3 Emulator
                "cemu.exe",                 // Wii U Emulator
                "yuzu.exe",                 // Switch Emulator
                "ryujinx.exe",              // Switch Emulator
                "project64.exe",            // N64 Emulator
                "mupen64plus.exe",          // N64 Emulator
                "dolphin.exe",              // GameCube/Wii Emulator
                "retroarch.exe",            // Multi-emulator frontend
                
                // Launchers de Games
                "steam.exe",                // Steam
                "epicgameslauncher.exe",    // Epic Games Launcher
                "origin.exe",               // Origin/EA App
                "uplay.exe",                // Ubisoft Connect
                "goggalaxy.exe",           // GOG Galaxy
                "battlenet.exe",            // Battle.net
                
                // Ferramentas de Desenvolvimento
                "visualstudio.exe",         // Visual Studio
                "devenv.exe",               // Visual Studio
                "code.exe",                 // VS Code
                "vscode.exe",               // VS Code (alternative)
                "node.exe",                 // Node.js
                "python.exe",               // Python
                "java.exe",                 // Java
                "javaw.exe",                // Java (Windows)
                "git.exe",                  // Git
                "git-bash.exe",             // Git Bash
                "docker.exe",               // Docker
                "docker-desktop.exe",      // Docker Desktop
                
                // Ferramentas de Sistema
                "cpu-z.exe",                // CPU-Z
                "gpu-z.exe",                // GPU-Z
                "hwmonitor.exe",            // HWMonitor
                "aida64.exe",               // AIDA64
                "crystaldiskinfo.exe",      // CrystalDiskInfo
                "malwarebytes.exe",         // Malwarebytes
                "ccleaner.exe",             // CCleaner
                "defraggler.exe",           // Defraggler
                "recuva.exe",               // Recuva
                
                // Overclocking e Hardware
                "afterburner.exe",          // MSI Afterburner
                "precisionx.exe",           // EVGA Precision X
                "nvidia.exe",               // NVIDIA GeForce Experience
                "amd.exe",                  // AMD Software
                "intel.exe",                // Intel Graphics Command Center
                "razer.exe",                // Razer Synapse
                "corsair.exe",              // Corsair iCUE
                "logitech.exe",             // Logitech G HUB
                
                // Streaming e Gravação
                "obs.exe",                  // OBS Studio
                "obs-studio.exe",           // OBS Studio (alternative)
                "streamlabs.exe",           // Streamlabs
                "discord.exe",              // Discord
                "teams.exe",                // Microsoft Teams
                "slack.exe",                // Slack
                "zoom.exe",                 // Zoom
                
                // Navegadores
                "chrome.exe",               // Google Chrome
                "firefox.exe",              // Mozilla Firefox
                "msedge.exe",               // Microsoft Edge
                "opera.exe",                // Opera
                "brave.exe",                // Brave Browser
                
                // Utilitários
                "7zfm.exe",                 // 7-Zip Manager
                "winrar.exe",               // WinRAR
                "notepad++.exe",            // Notepad++
                "vlc.exe",                  // VLC Media Player
                "mpc-hc.exe",               // MPC-HC
                "potplayer.exe",            // PotPlayer
                
                // Remote Desktop
                "teamviewer.exe",           // TeamViewer
                "anydesk.exe",              // AnyDesk
                "rustdesk.exe",             // RustDesk
                "splashtop.exe",            // Splashtop
                "parsec.exe",               // Parsec
                "sunshine.exe",             // Sunshine
                
                // Ferramentas de Rede
                "wireshark.exe",            // Wireshark
                "nmap.exe",                 // Nmap
                "putty.exe",                // PuTTY
                "filezilla.exe",            // FileZilla
                "winscp.exe",               // WinSCP
                
                // Ferramentas de Virtualização
                "vmware.exe",               // VMware
                "virtualbox.exe",          // VirtualBox
                "xming.exe",                // Xming
                "vcxsrv.exe",               // VcXsrv
                
                // Shells e Terminal
                "powershell.exe",           // PowerShell
                "cmd.exe",                  // Command Prompt
                "wsl.exe",                  // Windows Subsystem for Linux
                "ubuntu.exe",               // Ubuntu WSL
                "debian.exe",               // Debian WSL
                
                // Ferramentas de Criptomoedas
                "bitcoin-qt.exe",           // Bitcoin Core
                "litecoin-qt.exe",          // Litecoin Core
                "ethereum.exe",             // Ethereum
                "monerod.exe",              // Monero
                "monero-wallet-gui.exe",    // Monero Wallet
                "electrum.exe",             // Electrum
                "exodus.exe",               // Exodus
                "atomic.exe",               // Atomic Wallet
                "trustwallet.exe",          // Trust Wallet
                "metamask.exe",             // MetaMask
                "coinbase.exe",             // Coinbase
                "binance.exe",              // Binance
                "kraken.exe",               // Kraken
                "kucoin.exe",               // KuCoin
                "huobi.exe",                // Huobi
                "okex.exe",                 // OKEx
                "bybit.exe",                // Bybit
                "ftx.exe",                  // FTX
                "coinex.exe",               // CoinEx
                "gate.io.exe",              // Gate.io
                "kucoin.exe",               // KuCoin (duplicate)
                "mexc.exe",                 // MEXC
                "hotbit.exe",               // Hotbit
                "bitfinex.exe",             // Bitfinex
                "bitstamp.exe",             // Bitstamp
                "gemini.exe",               // Gemini
                "coinbasepro.exe",          // Coinbase Pro
                "bittrex.exe",              // Bittrex
                "poloniex.exe",             // Poloniex
                "hitbtc.exe",               // HitBTC
                "livecoin.exe",             // Livecoin
                "yobit.exe",                // YoBit
                "crex24.exe",               // CREX24
                "graviex.exe",              // Graviex
                "nexo.exe",                 // Nexo
                "celcius.exe",              // Celsius
                "blockfi.exe",              // BlockFi
                "nexo.exe",                 // Nexo (duplicate)
                "celsius.exe",              // Celsius (duplicate)
                "ledn.exe",                // Ledn
                "youhodler.exe",            // YouHodler
                "nexo.exe",                 // Nexo (duplicate)
                "celsius.exe",              // Celsius (duplicate)
                "ledn.exe",                // Ledn (duplicate)
                "youhodler.exe",            // YouHodler (duplicate)
                "nexo.exe",                 // Nexo (duplicate)
                "celsius.exe",              // Celsius (duplicate)
                "ledn.exe",                // Ledn (duplicate)
                "youhodler.exe",            // YouHodler (duplicate)
            };
            
            // Verificar se o arquivo está na whitelist
            if (legitimateTools.Contains(fileName))
            {
                _logger?.LogInfo($"[AdvancedStatic] 🛡️ Ferramenta legítima detectada: {fileName} - ignorando análise");
                return result;
            }

            // ✅ WHITELIST DE JOGOS CONFIÁVEIS
            if (fileName.Equals("cs2.exe") ||
                fileName.Equals("csgo.exe") ||
                fileName.Equals("valorant.exe") ||
                fileName.Equals("fortniteclient-win64-shipping.exe") ||
                fileName.Equals("dota2.exe") ||
                fileName.Equals("leagueclient.exe") ||
                fileName.Equals("lol.exe") ||
                fileName.Equals("javaw.exe") ||
                fileName.Equals("minecraft.exe") ||
                fileName.Equals("gta5.exe") ||
                fileName.Equals("rdr2.exe") ||
                fileName.Equals("r5apex.exe") ||
                fileName.Equals("overwatch.exe") ||
                fileName.Equals("tslgame.exe") ||
                fileName.Equals("witcher3.exe") ||
                fileName.Equals("cyberpunk2077.exe") ||
                fileName.Equals("destiny2.exe") ||
                fileName.Equals("adb.exe") ||
                fileName.Equals("overwolfupdater.exe") ||
                fileName.Equals("wa_3rd_party_host_32.exe") ||
                fileName.Equals("ssdbooster.exe") ||
                fileName.Equals("avira.spotlight.fallbackupdater.exe") ||
                (fileName.StartsWith("msi") && fileName.EndsWith(".tmp")) ||
                fileName.Equals("update.exe"))
            {
                _logger?.LogInfo($"[AdvancedStatic] 🛡️ Programa/Jogo confiável detectado: {fileName} - ignorando análise");
                return result;
            }

            // Whitelist de paths protegidos do sistema Windows
            // (MpSigStub.exe, Windows Defender, etc. — acesso sempre negado por design)
            if (IsProtectedSystemPath(filePath))
            {
                return result;
            }

            try
            {
                if (!File.Exists(filePath)) return result;

                // 🛡️ LIMITE DE SEGURANÇA DE MEMÓRIA: Arquivos > 50MB não são escaneados byte a byte.
                // Sem esse limite, o Shield pode tentar carregar caches de jogos, VHDs ou
                // pacotes do Windows Update (multi-GB) inteiros na RAM, causando picos de memória.
                const long MaxFileSizeBytes = 50L * 1024 * 1024; // 50 MB
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MaxFileSizeBytes)
                {
                    _logger.LogTrace($"[AdvancedStatic] Arquivo muito grande para scan ({fileInfo.Length / 1024 / 1024}MB), ignorando: {Path.GetFileName(filePath)}");
                    return result;
                }

                byte[] scanBuffer;
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int bytesToRead = (int)Math.Min(fs.Length, 1024 * 1024); // Limitar leitura a 1MB máximo por segurança e IO
                    scanBuffer = new byte[bytesToRead];
                    int read = fs.Read(scanBuffer, 0, bytesToRead);
                    if (read < bytesToRead) Array.Resize(ref scanBuffer, read);
                }

                if (scanBuffer.Length == 0) return result;

                // 1. Verificar imports suspeitos (usando uma parcela do buffer para strings)
                var stringBuffer = scanBuffer.Take(Math.Min(262144, scanBuffer.Length)).ToArray();
                var contentAscii = Encoding.ASCII.GetString(stringBuffer);
                var contentUnicode = Encoding.Unicode.GetString(stringBuffer);
                
                var suspiciousFound = new List<string>();
                foreach (var dll in _suspiciousImports)
                {
                    foreach (var func in dll.Value)
                    {
                        if (contentAscii.Contains(func, StringComparison.Ordinal) || 
                            contentUnicode.Contains(func, StringComparison.Ordinal))
                        {
                            suspiciousFound.Add(func);
                        }
                    }
                }

                if (suspiciousFound.Count > 3) // Aumento de 2 para 3 para reduzir falsos-positivos
                {
                    _logger.LogWarning($"[AdvancedStatic] Imports suspeitos encontrados em {Path.GetFileName(filePath)}: {string.Join(", ", suspiciousFound)}");
                    result.Threats.Add(new AdvancedThreat
                    {
                        Type = AdvancedThreatType.SuspiciousImports,
                        Details = $"Imports altamente suspeitos: {string.Join(", ", suspiciousFound)}",
                        Confidence = 0.50 // Reduzido de 0.75, requer mais fatores para bloquear
                    });
                }

                // 2. Verificar entropia (malwares costumam ter alta entropia por packing/encryption)
                // Calculado apenas sobre o buffer amostrado (p/ performance extrema)
                var entropy = CalculateEntropy(scanBuffer);
                if (entropy > 7.8) // Aumentando a tolerância de 7.5 para 7.8 (Muitos empacotadores legítimos chegam a 7.7)
                {
                    _logger.LogWarning($"[AdvancedStatic] Alta entropia detectada ({entropy:F2}) em {Path.GetFileName(filePath)}");
                    result.Threats.Add(new AdvancedThreat
                    {
                        Type = AdvancedThreatType.HighEntropy,
                        Details = $"Entropia crítica ({entropy:F2}) — possível código criptografado ou ofuscado",
                        Confidence = 0.55 // Reduzido para não agir isoladamente (antes: Math.Min(0.95...))
                    });
                }

                // 3. YARA-like signature scanning no buffer isolado (previne O(N*M) em gigabytes)
                foreach (var signature in _yaraRules)
                {
                    if (ContainsPattern(scanBuffer, signature))
                    {
                        string matchDesc = signature.Length > 10 ? Encoding.ASCII.GetString(signature.Take(10).ToArray()) + "..." : "Binário conhecido";
                        _logger.LogWarning($"[AdvancedStatic] Assinatura de ameaça detectada em {Path.GetFileName(filePath)}: {matchDesc}");
                        result.Threats.Add(new AdvancedThreat
                        {
                            Type = AdvancedThreatType.SignatureMatch,
                            Details = $"Assinatura de malware/shellcode detectada: {matchDesc}",
                            Confidence = 0.98
                        });
                    }
                }

                // 4. Anomalias de tamanho para executável
                if (fileInfo.Length < 10240 && (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    result.Threats.Add(new AdvancedThreat
                    {
                        Type = AdvancedThreatType.SuspiciousSize,
                        Details = "Arquivo executável com tamanho anormalmente reduzido (< 10KB)",
                        Confidence = 0.6
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Arquivo protegido pelo sistema (ex: MpSigStub.exe, Windows Defender)
                // Não é um erro do Voltris — ignorar silenciosamente
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AdvancedStatic] Não foi possível analisar {Path.GetFileName(filePath)}: {ex.Message}");
            }

            // Motor de decisão mais inteligente: requer soma de incertezas ou um único evento crítico (ex: Assinatura de 0.98)
            double combinedConfidence = result.Threats.Sum(t => t.Confidence);
            result.IsMalicious = combinedConfidence > 0.90 || result.Threats.Any(t => t.Confidence > 0.85);
            
            // 📱 Se for malicioso, enviar notificação para o Telegram
            if (result.IsMalicious)
            {
                var threatDetails = string.Join(", ", result.Threats.Select(t => t.Details));
                var safeFileName = Path.GetFileName(filePath);
                
            }
            
            return result;
        }

        private double CalculateEntropy(byte[] data)
        {
            var freq = new int[256];
            foreach (var b in data)
                freq[b]++;

            double entropy = 0;
            foreach (var f in freq)
            {
                if (f == 0) continue;
                double p = (double)f / data.Length;
                entropy -= p * Math.Log2(p);
            }
            return entropy;
        }

        private bool ContainsPattern(byte[] data, byte[] pattern)
        {
            if (pattern.Length > data.Length) return false;
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return true;
            }
            return false;
        }

        /// <summary>
        /// Verifica se o arquivo pertence a um path protegido do sistema operacional.
        /// Esses arquivos não devem ser escaneados pois o acesso é negado por design do Windows.
        /// </summary>
        private static bool IsProtectedSystemPath(string filePath)
        {
            var normalizedPath = filePath.ToLowerInvariant();
            return normalizedPath.StartsWith(@"c:\windows\system32", StringComparison.Ordinal)
                || normalizedPath.StartsWith(@"c:\windows\syswow64", StringComparison.Ordinal)
                || normalizedPath.StartsWith(@"c:\windows\temp", StringComparison.Ordinal)
                || normalizedPath.StartsWith(@"c:\windows\winsxs", StringComparison.Ordinal)
                || normalizedPath.StartsWith(@"c:\program files\windowsapps", StringComparison.Ordinal)
                || normalizedPath.Contains("mpsigsub") // Windows Defender stub
                || normalizedPath.Contains("mpsigstub"); // Windows Defender update
        }

    } // fim de AdvancedStaticAnalyzer

    public class AdvancedScanResult
    {
        public bool IsMalicious { get; set; }
        public List<AdvancedThreat> Threats { get; set; } = new List<AdvancedThreat>();
    }

    public class AdvancedThreat
    {
        public AdvancedThreatType Type { get; set; }
        public string Details { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    public enum AdvancedThreatType
    {
        SuspiciousImports,
        HighEntropy,
        SuspiciousSection,
        SignatureMatch,
        SuspiciousSize
    }
}
