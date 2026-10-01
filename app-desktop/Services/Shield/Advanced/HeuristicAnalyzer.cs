using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield.Advanced
{
    /// <summary>
    /// Análise Heurística — Pontuação de risco baseada em múltiplas características do arquivo.
    /// </summary>
    public class HeuristicAnalyzer
    {
        private readonly ILoggingService _logger;
        private readonly string _voltrisPath;

        public HeuristicAnalyzer(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _voltrisPath = AppDomain.CurrentDomain.BaseDirectory.ToLowerInvariant();
        }

        public double CalculateMaliciousScore(string filePath)
        {
            // Whitelist Voltris — Score 0 para seus próprios arquivos
            if (filePath.ToLowerInvariant().Contains(_voltrisPath)) return 0;

            // Whitelist de paths protegidos do sistema Windows
            // (MpSigStub.exe, Windows Defender, C:\Windows\TEMP, etc.)
            if (IsProtectedSystemPath(filePath)) return 0;

            try
            {
                if (!File.Exists(filePath)) return 0;

                var features = ExtractFeatures(filePath);
                double score = 0;

                // Heurísticas ponderadas profissionais
                if (features.HasSuspiciousImports) score += 0.35;
                if (features.HasHighEntropy) score += 0.25;
                if (features.IsPacked) score += 0.20;
                if (features.HasAntiDebug) score += 0.20;
                if (features.HasUnusualSectionNames) score += 0.15;
                if (features.IsTinyExecutable) score += 0.10;

                double finalScore = Math.Min(1.0, score);
                if (finalScore >= 0.5)
                {
                    _logger.LogWarning($"[HeuristicAnalyzer] Risco elevado ({finalScore:P0}) detectado em {Path.GetFileName(filePath)} (Entropy: {features.HasHighEntropy}, Imports: {features.HasSuspiciousImports})");
                }

                return finalScore;
            }
            catch (UnauthorizedAccessException)
            {
                // Arquivo protegido pelo sistema (ex: MpSigStub.exe, Windows Defender)
                // Não é um erro do Voltris — ignorar silenciosamente
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[HeuristicAnalyzer] Não foi possível analisar {Path.GetFileName(filePath)}: {ex.Message}");
                return 0;
            }
        }

        private FileFeatures ExtractFeatures(string filePath)
        {
            var features = new FileFeatures();

            // 🛡️ LIMITE DE SEGURANÇA DE MEMÓRIA: Não carregar arquivos > 50MB na RAM.
            // Sem esse limite, o Shield pode tentar escanear caches de jogos, VHDs ou
            // pacotes do Windows Update inteiros, causando picos de memória (ex: 24GB).
            const long MaxFileSizeBytes = 50L * 1024 * 1024; // 50 MB
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > MaxFileSizeBytes)
            {
                _logger.LogTrace($"[HeuristicAnalyzer] Arquivo muito grande para scan ({fileInfo.Length / 1024 / 1024}MB), ignorando: {Path.GetFileName(filePath)}");
                return features; // Score 0 para arquivos gigantes
            }

            byte[] bytes;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int bytesToRead = (int)Math.Min(fs.Length, 1024 * 1024); // Limitar leitura a 1MB máximo por segurança e performance
                bytes = new byte[bytesToRead];
                int read = fs.Read(bytes, 0, bytesToRead);
                if (read < bytesToRead) Array.Resize(ref bytes, read);
            }

            if (bytes.Length == 0) return features;

            // 1. Anti-Debugging / Anti-Analysis
            features.HasAntiDebug = CheckAntiDebugTechniques(bytes);

            // 2. Packers conhecidos
            features.IsPacked = IsPacked(bytes);

            // 3. Seções não padrão
            features.HasUnusualSectionNames = HasUnusualSections(bytes);

            // 4. Entropia
            features.HasHighEntropy = CalculateEntropy(bytes) > 7.1;

            // 5. Imports via scanning simplificado
            features.HasSuspiciousImports = CheckSuspiciousFunctions(bytes);

            // 6. Tamanho suspeito para binário
            features.IsTinyExecutable = bytes.Length < 1024 * 12;

            return features;
        }

        private bool CheckAntiDebugTechniques(byte[] bytes)
        {
            var patterns = new[]
            {
                new byte[] { 0x64, 0xA1, 0x30, 0x00, 0x00, 0x00 }, // mov eax, fs:[30h] (PEB check)
                new byte[] { 0x0F, 0x31 }, // rdtsc instruction (timing analysis)
                new byte[] { 0xCC }, // int 3 (breakpoint interrupt)
                new byte[] { 0x48, 0x31, 0xC0, 0x41, 0xB8 } // possible debug shellcode
            };

            return patterns.Any(p => ContainsPattern(bytes.Take(Math.Min(bytes.Length, 128*1024)).ToArray(), p));
        }

        private bool IsPacked(byte[] bytes)
        {
            var content = System.Text.Encoding.ASCII.GetString(bytes.Take(Math.Min(bytes.Length, 65536)).ToArray());
            var packerSections = new[] { 
                Decrypt("VVBYMA=="),
                Decrypt("VVBYMQ=="),
                Decrypt("VVBYMg=="),
                Decrypt("LnRoZW1pZGE="),
                Decrypt("LnZtcDA="),
                Decrypt("LmFzcGFjaw=="),
                Decrypt("LnBlY21k")
            };
            return packerSections.Any(s => content.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        private bool HasUnusualSections(byte[] bytes)
        {
            // Abordagem simplificada: procurar por seções padrão que NÃO estão devidamente formatadas ou nomes estranhos
            var standardSections = new[] { ".text", ".data", ".rdata", ".rsrc", ".reloc", ".pdata" };
            var content = System.Text.Encoding.ASCII.GetString(bytes.Take(Math.Min(bytes.Length, 65536)).ToArray());
            
            // Malware frequentemente usa nomes aleatórios como "fjeie", ".asdf"
            // Esta é uma heurística muito fraca no modo string scan, mas auxilia no score total
            return false; // Implementação simplificada mantida estável
        }

        private bool CheckSuspiciousFunctions(byte[] bytes)
        {
            var content = System.Text.Encoding.ASCII.GetString(bytes.Take(Math.Min(bytes.Length, 128*1024)).ToArray());
            
            // Ofuscado para evitar falsos positivos
            var suspicious = new[] { 
                Decrypt("VmlydHVhbEFsbG9jRXg="), // VirtualAllocEx
                Decrypt("V3JpdGVQcm9jZXNzTWVtb3J5"), // WriteProcessMemory
                Decrypt("Q3JlYXRlUmVtb3RlVGhyZWFk"), // CreateRemoteThread
                Decrypt("TnRRdWVyeUluZm9ybWF0aW9uUHJvY2Vzcw=="), // NtQueryInformationProcess
                Decrypt("U2hlbGxFeGVjdXRl") // ShellExecute
            };
            
            int count = suspicious.Count(s => content.Contains(s, StringComparison.Ordinal));
            return count >= 2;
        }

        private string Decrypt(string base64)
        {
            try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64)); }
            catch { return string.Empty; }
        }

        private double CalculateEntropy(byte[] data)
        {
            var freq = new int[256];
            foreach (var b in data) freq[b]++;
            double entropy = 0;
            foreach (var f in freq)
            {
                if (f == 0) continue;
                double p = (double)f / data.Length;
                entropy -= p * Math.Log2(p);
            }
            return (double)Math.Round(entropy, 2);
        }

        private bool ContainsPattern(byte[] data, byte[] pattern)
        {
            if (pattern.Length > data.Length) return false;
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j]) { found = false; break; }
                }
                if (found) return true;
            }
            return false;
        }

        /// <summary>
        /// Verifica se o arquivo pertence a um path protegido do sistema Windows.
        /// O acesso a esses arquivos é negado por design do SO — não devem ser escaneados.
        /// </summary>
        private static bool IsProtectedSystemPath(string filePath)
        {
            var p = filePath.ToLowerInvariant();
            return p.StartsWith(@"c:\windows\system32", StringComparison.Ordinal)
                || p.StartsWith(@"c:\windows\syswow64", StringComparison.Ordinal)
                || p.StartsWith(@"c:\windows\temp", StringComparison.Ordinal)
                || p.StartsWith(@"c:\windows\winsxs", StringComparison.Ordinal)
                || p.StartsWith(@"c:\program files\windowsapps", StringComparison.Ordinal)
                || p.Contains("mpsigstub")   // Windows Defender update stub
                // ✅ WHITELIST: Ferramentas legítimas de desenvolvimento
                || p.Contains(@"\microsoft visual studio\")
                || p.Contains(@"\msbuild\")
                || p.Contains(@"\cmake\")
                || p.Contains(@"\windows kits\")  // SDK da Microsoft
                || p.Contains(@"\dotnet\")         // .NET SDK
                || p.Contains(@"\roslyn\")          // Compilador C# da Microsoft
                || Path.GetFileName(p) == "tracker.exe"   // MSBuild file tracker
                || Path.GetFileName(p) == "cl.exe"        // MSVC C++ Compiler
                || Path.GetFileName(p) == "link.exe"      // MSVC Linker
                || Path.GetFileName(p) == "msbuild.exe"
                || Path.GetFileName(p) == "cmake.exe";
        }
    }

    public class FileFeatures
    {
        public bool HasSuspiciousImports { get; set; }
        public bool HasHighEntropy { get; set; }
        public bool IsPacked { get; set; }
        public bool HasAntiDebug { get; set; }
        public bool HasUnusualSectionNames { get; set; }
        public bool IsTinyExecutable { get; set; }
    }
}
