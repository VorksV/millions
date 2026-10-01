using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>
    /// SignatureVerificationService — Verifica assinatura digital (Authenticode),
    /// calcula entropia de arquivos e analisa PE headers para detecção heurística.
    /// </summary>
    public class SignatureVerificationService
    {
        private readonly ILoggingService _logger;

        /// <summary>
        /// Cache de resultado de assinatura, chaveado por (caminho, mtime, tamanho).
        ///
        /// POR QUE EXISTE: WinVerifyTrust + X509Certificate.CreateFromSignedFile
        /// custa dezenas de milissegundos por arquivo. O CodeInjectionDetector
        /// chamava VerifySignature para CADA processo a cada ciclo de 5 minutos,
        /// sem cache algum, serializado. Com ~300 processos isso e ~30s de
        /// WinVerifyTrust por ciclo, contending com o ProcessCacheService e o
        /// BehavioralMonitor no mesmo thread pool.
        ///
        /// A chave inclui mtime e tamanho: se o arquivo mudar, o cache invalida
        /// sozinho. Nao ha necessidade de TTL para corretude, apenas o TTL evita
        /// crescimento sem limite em uma sessao longa com muitos arquivos
        /// temporarios.
        /// </summary>
        private sealed record SignatureCacheEntry(SignatureResult Result, DateTime CreatedUtc);

        private const int SignatureCacheMaxEntries = 4096;
        private static readonly TimeSpan SignatureCacheTtl = TimeSpan.FromHours(2);

        private readonly ConcurrentDictionary<string, SignatureCacheEntry> _signatureCache = new(StringComparer.OrdinalIgnoreCase);
        private long _signatureCacheHits;
        private long _signatureCacheMisses;

        // Publishers confiáveis — assinaturas destes não geram alerta
        private static readonly string[] TrustedPublishers = {
            "Microsoft Corporation", "Microsoft Windows", "Microsoft Windows Publisher",
            "Microsoft Windows Kits", "Microsoft Windows Software Foundation",
            "NVIDIA Corporation", "Intel Corporation", "Advanced Micro Devices, Inc.",
            "Google LLC", "Mozilla Corporation", "Valve Corporation", "Valve Corp.", "Steam",
            "Discord Inc.", "Spotify AB", "Adobe Inc.", "Oracle Corporation",
            "Apple Inc.", "Samsung Electronics Co., Ltd.", "Realtek Semiconductor Corp.",
            "Logitech", "Corsair", "Epic Games, Inc.", "Riot Games, Inc.",
            "Blizzard Entertainment", "Electronic Arts, Inc.", "Telegram FZ-LLC",
            "AnyDesk Software GmbH", "TeamViewer Germany GmbH",
            "Anomaly Innovations, Inc https://anoma.ly/",
            "CPUID", "Exafunction", ".NET Foundation"
        };

        // Imports de PE suspeitos (usados por malware para injeção/evasão)
        // Ofuscado para evitar falsos positivos no binário do Voltris
        private static readonly string[] SuspiciousPeImports = {
            Decrypt("VmlydHVhbEFsbG9jRXg="), // VirtualAllocEx
            Decrypt("V3JpdGVQcm9jZXNzTWVtb3J5"), // WriteProcessMemory
            Decrypt("Q3JlYXRlUmVtb3RlVGhyZWFk"), // CreateRemoteThread
            Decrypt("TnRVbm1hcFZpZXdPZlNlY3Rpb24="), // NtUnmapViewOfSection
            Decrypt("UnRsQ3JlYXRlVXNlclRocmVhZA=="), // RtlCreateUserThread
            Decrypt("U2V0V2luZG93c0hvb2tFeA=="), // SetWindowsHookEx
            Decrypt("R2V0QXN5bmNLZXlTdGF0ZQ=="), // GetAsyncKeyState
            Decrypt("TWFwVmlld09mRmlsZQ=="), // MapViewOfFile
            Decrypt("QWRqdXN0VG9rZW5Qcml2aWxlZ2Vz"), // AdjustTokenPrivileges
            Decrypt("T3BlblByb2Nlc3NUb2tlbg=="), // OpenProcessToken
            Decrypt("SXNEZWJ1Z2dlclByZXNlbnQ="), // IsDebuggerPresent
            Decrypt("Q2hlY2tSZW1vdGVEZWJ1Z2dlclByZXNlbnQ="), // CheckRemoteDebuggerPresent
            Decrypt("TnRRdWVyeUluZm9ybWF0aW9uUHJvY2Vzcw=="), // NtQueryInformationProcess
            Decrypt("T3V0cHV0RGVidWdTdHJpbmc=") // OutputDebugString
        };

        private static string Decrypt(string base64)
        {
            try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64)); }
            catch { return string.Empty; }
        }

        public SignatureVerificationService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
        }

        private static bool VerifyAuthenticode(string filePath)
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = Marshal.StringToCoTaskMemUni(filePath),
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };
            var fileInfoPointer = IntPtr.Zero;
            var trustDataPointer = IntPtr.Zero;
            var verificationStarted = false;

            try
            {
                fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
                var trustData = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = 2,
                    fdwRevocationChecks = 0,
                    dwUnionChoice = 1,
                    pFile = fileInfoPointer,
                    dwStateAction = 1,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = IntPtr.Zero,
                    dwProvFlags = 0,
                    dwUIContext = 0
                };
                trustDataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
                Marshal.StructureToPtr(trustData, trustDataPointer, false);
                verificationStarted = true;
                return WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, trustDataPointer) == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (trustDataPointer != IntPtr.Zero)
                {
                    if (verificationStarted)
                    {
                        var closeData = Marshal.PtrToStructure<WINTRUST_DATA>(trustDataPointer);
                        closeData.dwStateAction = 2;
                        Marshal.StructureToPtr(closeData, trustDataPointer, false);
                        WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, trustDataPointer);
                    }
                    Marshal.FreeHGlobal(trustDataPointer);
                }

                if (fileInfoPointer != IntPtr.Zero)
                {
                    Marshal.DestroyStructure<WINTRUST_FILE_INFO>(fileInfoPointer);
                    Marshal.FreeHGlobal(fileInfoPointer);
                }

                if (fileInfo.pcwszFilePath != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(fileInfo.pcwszFilePath);
                }
            }
        }

        private static bool HasMzSignature(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 2, FileOptions.None);
                Span<byte> magic = stackalloc byte[2];
                return stream.Read(magic) == 2 && magic[0] == (byte)'M' && magic[1] == (byte)'Z';
            }
            catch
            {
                return false;
            }
        }

        private static bool HasCodeSigningUsage(X509Certificate2 certificate)
        {
            var extension = certificate.Extensions
                .OfType<X509EnhancedKeyUsageExtension>()
                .FirstOrDefault();
            return extension?.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(oid => oid.Value == "1.3.6.1.5.5.7.3.3") == true;
        }

        private static bool IsTrustedPublisherIdentity(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && TrustedPublishers.Any(publisher => string.Equals(value.Trim(), publisher, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Verifica a assinatura Authenticode de um executável.
        /// Resultado memorizado por (caminho, mtime, tamanho).
        /// </summary>
        public SignatureResult VerifySignature(string filePath)
        {
            var cacheKey = BuildCacheKey(filePath);

            if (cacheKey is not null &&
                _signatureCache.TryGetValue(cacheKey, out var cached) &&
                DateTime.UtcNow - cached.CreatedUtc < SignatureCacheTtl)
            {
                Interlocked.Increment(ref _signatureCacheHits);
                return cached.Result;
            }

            Interlocked.Increment(ref _signatureCacheMisses);

            var computed = VerifySignatureCore(filePath);

            if (cacheKey is not null)
            {
                _signatureCache[cacheKey] = new SignatureCacheEntry(computed, DateTime.UtcNow);
                TrimSignatureCache();
            }

            return computed;
        }

        /// <summary>
        /// Chave de cache estavel. Retorna null quando o arquivo nao pode ser
        /// lido (inexistente, sem permissao) — nesses casos nao ha cache, porque
        /// o resultado muda conforme o disco responde.
        /// </summary>
        private static string? BuildCacheKey(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;

            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists) return null;
                return $"{info.FullName}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
            }
            catch
            {
                return null;
            }
        }

        private void TrimSignatureCache()
        {
            if (_signatureCache.Count <= SignatureCacheMaxEntries) return;

            var cutoff = DateTime.UtcNow - SignatureCacheTtl;
            var stale = _signatureCache
                .Where(kvp => kvp.Value.CreatedUtc < cutoff)
                .Select(kvp => kvp.Key)
                .ToArray();

            foreach (var key in stale)
                _signatureCache.TryRemove(key, out _);

            if (_signatureCache.Count <= SignatureCacheMaxEntries) return;

            // Ainda acima do limite apos remover expirados: descarta os mais antigos.
            var overflow = _signatureCache.Count - SignatureCacheMaxEntries;
            foreach (var key in _signatureCache
                         .OrderBy(kvp => kvp.Value.CreatedUtc)
                         .Take(overflow)
                         .Select(kvp => kvp.Key)
                         .ToArray())
            {
                _signatureCache.TryRemove(key, out _);
            }
        }

        /// <summary>Estatisticas do cache, para diagnostico de desempenho.</summary>
        public (long Hits, long Misses, int Entries) SignatureCacheStats =>
            (Interlocked.Read(ref _signatureCacheHits), Interlocked.Read(ref _signatureCacheMisses), _signatureCache.Count);

        private SignatureResult VerifySignatureCore(string filePath)
        {
            var result = new SignatureResult { FilePath = filePath };

            try
            {
                if (!File.Exists(filePath))
                {
                    result.Status = SignatureStatus.FileNotFound;
                    return result;
                }

                // A verificacao de assinatura se aplica ao CONTEUDO, nao ao nome.
                // Um "relatorio.txt" que e na verdade um PE carrega assinatura do
                // mesmo jeito, e um malware costuma renomear para .txt justamente
                // para escapar de um filtro que so olha extensao.
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                var isExecutableExtension = ext is ".exe" or ".dll" or ".sys" or ".msi" or ".ocx" or ".cpl";

                if (!isExecutableExtension && !HasMzSignature(filePath))
                {
                    result.Status = SignatureStatus.NotApplicable;
                    return result;
                }

                var authenticodeValid = VerifyAuthenticode(filePath);
                try
                {
                    var baseCert = X509Certificate.CreateFromSignedFile(filePath);
                    using var cert = new X509Certificate2(baseCert);
                    result.IsSigned = true;
                    result.Publisher = cert.GetNameInfo(X509NameType.SimpleName, false);
                    result.Issuer = cert.GetNameInfo(X509NameType.SimpleName, true);
                    result.ValidFrom = cert.NotBefore;
                    result.ValidTo = cert.NotAfter;
                    result.IsValid = authenticodeValid && HasCodeSigningUsage(cert);
                    result.IsTrustedPublisher = result.IsValid
                        && (IsTrustedPublisherIdentity(result.Publisher) || IsTrustedPublisherIdentity(result.Issuer));
                    result.Status = result.IsValid ? SignatureStatus.Valid : SignatureStatus.Invalid;

                    _logger.LogDebug($"[SignatureVerify] {Path.GetFileName(filePath)}: Signed={result.IsSigned}, " +
                        $"Publisher={result.Publisher}, Valid={result.IsValid}, Trusted={result.IsTrustedPublisher}");
                }
                catch (Exception)
                {
                    if (!authenticodeValid)
                    {
                        result.IsSigned = false;
                        result.Status = SignatureStatus.Unsigned;
                        _logger.LogDebug($"[SignatureVerify] {Path.GetFileName(filePath)}: Não assinado");
                        return result;
                    }

                    var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
                    result.IsSigned = true;
                    result.Publisher = string.IsNullOrWhiteSpace(versionInfo.CompanyName)
                        ? versionInfo.ProductName
                        : versionInfo.CompanyName;
                    result.IsValid = true;
                    result.IsTrustedPublisher = IsTrustedPublisherIdentity(result.Publisher);
                    result.Status = SignatureStatus.Valid;
                    _logger.LogDebug($"[SignatureVerify] {Path.GetFileName(filePath)}: Signed=True, " +
                        $"Publisher={result.Publisher}, Valid=True, Trusted={result.IsTrustedPublisher} (fallback Authenticode)");
                }
            }
            catch (Exception ex)
            {
                result.Status = SignatureStatus.Error;
                _logger.LogWarning($"[SignatureVerify] Erro ao verificar {filePath}: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Calcula a entropia de Shannon de um arquivo.
        /// Alta entropia (> 7.0) indica possível empacotamento, criptografia ou compressão.
        /// Malware frequentemente tem entropia > 7.2 (empacotado com UPX, Themida, etc.)
        /// </summary>
        public double CalculateEntropy(string filePath, int maxBytes = 65536)
        {
            try
            {
                if (!File.Exists(filePath)) return 0;

                using var stream = File.OpenRead(filePath);
                var buffer = new byte[Math.Min(maxBytes, stream.Length)];
                var bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) return 0;

                var frequency = new int[256];
                for (int i = 0; i < bytesRead; i++)
                    frequency[buffer[i]]++;

                double entropy = 0;
                for (int i = 0; i < 256; i++)
                {
                    if (frequency[i] == 0) continue;
                    double p = (double)frequency[i] / bytesRead;
                    entropy -= p * Math.Log2(p);
                }

                return Math.Round(entropy, 3);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Entropy] Erro ao calcular entropia de {filePath}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Analisa PE headers de um executável para detectar imports suspeitos.
        /// Retorna lista de imports potencialmente maliciosos encontrados.
        /// </summary>
        public PeAnalysisResult AnalyzePeHeaders(string filePath)
        {
            var result = new PeAnalysisResult { FilePath = filePath };

            try
            {
                if (!File.Exists(filePath)) return result;

                // Ler primeiros 64KB do arquivo para análise rápida
                byte[] buffer;
                using (var stream = File.OpenRead(filePath))
                {
                    buffer = new byte[Math.Min(65536, stream.Length)];
                    stream.Read(buffer, 0, buffer.Length);
                }

                // Verificar magic number PE
                if (buffer.Length < 64) return result;
                if (buffer[0] != 'M' || buffer[1] != 'Z')
                {
                    result.IsValidPe = false;
                    return result;
                }
                result.IsValidPe = true;

                // Converter para string para busca de imports (simplificado)
                var content = System.Text.Encoding.ASCII.GetString(buffer);

                foreach (var import in SuspiciousPeImports)
                {
                    if (content.Contains(import, StringComparison.Ordinal))
                    {
                        result.SuspiciousImports.Add(import);
                    }
                }

                // Verificar se tem muitas seções com nomes incomuns (indicativo de packer)
                // Seções normais: .text, .data, .rdata, .rsrc, .reloc
                var packerSections = new[] { 
                    Decrypt("VVBY"), // UPX
                    Decrypt("Lm5kYXRh"), // .ndata
                    Decrypt("LnRoZW1pZGE="), // .themida
                    Decrypt("LnZtcA=="), // .vmp
                    Decrypt("LmVuaWdtYQ=="), // .enigma
                    Decrypt("LmFzcGFjaw==") // .aspack
                };
                foreach (var section in packerSections)
                {
                    if (content.Contains(section, StringComparison.OrdinalIgnoreCase))
                    {
                        result.IsPacked = true;
                        result.PackerName = section.TrimStart('.');
                        break;
                    }
                }

                // Metasploit/Meterpreter indicators
                var metasploitIndicators = new[] {
                    Decrypt("bWV0c3J2"), // metsrv
                    Decrypt("bWV0ZXJwcmV0ZXI="), // meterpreter
                    Decrypt("cmV2ZXJzZV90Y3A="), // reverse_tcp
                    Decrypt("cmV2ZXJzZV9odHRw"), // reverse_http
                    Decrypt("YmluZF90Y3A="), // bind_tcp
                    Decrypt("c2hlbGxfcmV2ZXJzZQ=="), // shell_reverse
                    Decrypt("cGF5bG9hZA=="), // payload
                    Decrypt("c3RhZ2VsZXNz"), // stageless
                    Decrypt("UmVmbGVjdGl2ZUxvYWRlcg=="), // ReflectiveLoader
                    Decrypt("c3RkYXBp") // stdapi
                };
                foreach (var indicator in metasploitIndicators)
                {
                    if (content.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                    {
                        result.MetasploitIndicators.Add(indicator);
                    }
                }

                result.HasSuspiciousImports = result.SuspiciousImports.Count > 2;
                result.HasMetasploitIndicators = result.MetasploitIndicators.Count > 0;

                if (result.SuspiciousImports.Count > 0 || result.IsPacked || result.HasMetasploitIndicators)
                {
                    _logger.LogWarning($"[PEAnalysis] {Path.GetFileName(filePath)}: " +
                        $"SuspiciousImports={result.SuspiciousImports.Count}, " +
                        $"Packed={result.IsPacked} ({result.PackerName}), " +
                        $"Metasploit={result.HasMetasploitIndicators}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PEAnalysis] Erro ao analisar {filePath}: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Tamanho do arquivo em bytes, ou 0 se não der para ler.
        ///
        /// Existe porque a avaliação de risco consulta o tamanho em três pontos
        /// distintos, e cada um tratava a falha de leitura por conta própria. Um
        /// arquivo pode mudar ou sumir entre a leitura da assinatura e a do
        /// tamanho — o antivírus pode colocar em quarentena o processo no meio
        /// da análise — e qualquer um desses acessos lançando exceção derrubaria
        /// a avaliação inteira. Esse é justamente o tipo de falha silenciosa que
        /// produz uma ameaça fantasma.
        /// </summary>
        private static long GetFileSizeSafe(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                return info.Exists ? info.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Análise completa de um arquivo: assinatura + entropia + PE headers.
        /// Retorna um score de risco de 0 (seguro) a 100 (altamente suspeito).
        /// </summary>
        public FileRiskAssessment AssessFileRisk(string filePath)
        {
            var assessment = new FileRiskAssessment { FilePath = filePath };

            try
            {
                if (!File.Exists(filePath))
                {
                    assessment.RiskScore = 0;
                    assessment.RiskLevel = LocalizationService.Instance.GetString("RiskAssessmentNone");
                    assessment.Reason = "Arquivo não encontrado durante a avaliação";
                    return assessment;
                }

                _logger.LogInfo($"[RiskAssess] Analisando: {Path.GetFileName(filePath)}");

                // 1. Assinatura digital
                var sig = VerifySignature(filePath);
                assessment.Signature = sig;

                if (sig.IsTrustedPublisher && sig.IsValid)
                {
                    assessment.Flags.Add($"Assinado por publisher confiável: {sig.Publisher}");
                }

                int score = 0;

                // [FIX:FALSO-POSITIVO-JOGOS] ASSINATURA VÁLIDA DIMINUI O RISCO.
                //
                // A pontuação era SOMENTE aditiva, e isso tornava impossível
                // distinguir um binário assinado de confiança de um trojan
                // qualquer: os dois recebiam os mesmos pontos por não ter
                // assinatura — e, depois, os mesmos pontos por estar
                // empacotado, ter entropia alta e ter imports que o scanner
                // considera suspeitos.
                //
                // O que o log do usuário mostrou (29/09, 02:18:29) foram 5
                // "ameaças de alto risco", todas com score EXATAMENTE 70:
                //
                //     DataServer        Score=70
                //     JoinServer        Score=70
                //     ConnectServer     Score=70
                //     GameServer_Normal Score=70
                //     GameServer_CastleSiege Score=70
                //
                // São executáveis de servidores de jogo. Não estão assinados
                // (+25), usam empacotamento — normal em binários de jogo — e
                // importam rotinas que o scanner trata como suspeitas (+20).
                // Somados com mais um sinal, chegam a 70, e o limiar do
                // `ThreatProtectionService` é exatamente 70.
                //
                // Ou seja: o limiar não estava errado. O SCORE é que não
                // distinguia "não assinado" de "inconfiável". Uma assinatura
                // digital VÁLIDA de um publisher conhecido é evidência
                // POSITIVA de procedência, e precisa GErar a pontuação, não
                // apenas não somar.
                if (sig.IsSigned && sig.IsValid)
                {
                    // -30: o offset maior é proposital. Um binário assinado,
                    // íntegro e de publisher confiável não é "um pouco menos
                    // suspeito" — ele sai da categoria de "não se sabe o que
                    // é". Mesmo assim o score não zera, porque assinatura
                    // nenhuma prova que o conteúdo seja inofensivo, e o
                    // comportamento do processo continua sendo analisado em
                    // outra camada.
                    score -= 30;
                    assessment.Flags.Add(
                        $"Assinatura digital valida de publisher confiavel (-30): {sig.Publisher}");
                }
                else if (!sig.IsSigned)
                {
                    // Não assinado = +25 pontos
                    score += 25;
                    assessment.Flags.Add("Executável não assinado digitalmente");
                }
                else
                {
                    // Assinatura inválida = +40 pontos. Este é o pior caso:
                    // o binário AFIRMA ter assinatura e ela não confere, o que
                    // é bem mais suspeito do que nunca ter dito nada.
                    score += 40;
                    assessment.Flags.Add("Assinatura digital inválida ou expirada");
                }

                // 2. Entropia
                var entropy = CalculateEntropy(filePath);
                assessment.Entropy = entropy;

                // [FIX:FALSO-POSITIVO-JOGOS] EMPACOTAMENTO É ESPERADO EM BINÁRIOS GRANDES.
                //
                // A entropia mede quão "embaralhado" o arquivo é. Ela não diz se
                // o conteúdo é malicioso — diz se o arquivo é difícil de ler.
                // Binários legítimos de jogo são propositalmente ofuscados
                // (valores, tabelas, código), e por isso têm entropia alta.
                //
                // A pontuação original somava 30 pontos para qualquer arquivo
                // acima de 7.5, sem considerar o tamanho. Um servidor de jogo
                // de várias centenas de MB passa facilmente disso, e esses 30
                // pontos não distinguem malware de um jogo.
                long tamanhoArquivo = GetFileSizeSafe(filePath);
                bool arquivoGrande = tamanhoArquivo > 20L * 1024 * 1024;

                if (entropy > 7.5)
                {
                    int pontos = arquivoGrande ? 5 : 30;

                    score += pontos;
                    assessment.Flags.Add(arquivoGrande
                        ? $"Entropia alta ({entropy:F2}) em binario grande — comum em jogos (peso reduzido)"
                        : $"Entropia muito alta ({entropy:F2}) — possivel empacotamento/criptografia");
                }
                else if (entropy > 7.0)
                {
                    score += 15;
                    assessment.Flags.Add($"Entropia elevada ({entropy:F2})");
                }

                // 3. PE Analysis
                var pe = AnalyzePeHeaders(filePath);
                assessment.PeAnalysis = pe;

                if (pe.HasMetasploitIndicators)
                {
                    score += 50;
                    assessment.Flags.Add($"Indicadores de Metasploit/Meterpreter: {string.Join(", ", pe.MetasploitIndicators)}");
                }

                if (pe.HasSuspiciousImports)
                {
                    // [FIX:FALSO-POSITIVO-JOGOS] IMPORTS SÓ VALEM SEM ASSINATURA.
                    //
                    // As rotinas que um servidor de jogo importa — acesso a
                    // rede, criação de processo, criptografia — são as MESMAS
                    // que qualquer ferramenta legítima usa. Sem assinatura
                    // válida, são um indício fraco; com assinatura válida de um
                    // publisher conhecido, são a ESPERADA.
                    //
                    // Sem essa distinção, os 20 pontos deste item somavam em
                    // cima de 25 (não assinado) e 15 (empacotado) e levavam um
                    // executável de jogo inteiro ao limiar de ameaça.
                    if (sig.IsSigned && sig.IsValid)
                    {
                        assessment.Flags.Add(
                            $"Imports formalmente suspeitos, mas em binario assinado por {sig.Publisher} (sem peso)");
                    }
                    else
                    {
                        score += 20;
                        assessment.Flags.Add($"Imports suspeitos: {string.Join(", ", pe.SuspiciousImports)}");
                    }
                }

                if (pe.IsPacked)
                {
                    // [FIX:FALSO-POSITIVO-JOGOS] EMPACOTAMENTO TAMBÉM É COMUM EM JOGOS.
                    if (sig.IsSigned && sig.IsValid)
                    {
                        assessment.Flags.Add(
                            $"Empacotado ({pe.PackerName}), mas binario assinado por {sig.Publisher} (sem peso)");
                    }
                    else
                    {
                        score += 15;
                        assessment.Flags.Add($"Empacotado com: {pe.PackerName}");
                    }
                }

                // 4. Tamanho suspeito
                if (tamanhoArquivo > 0)
                {
                    if (tamanhoArquivo < 10240 && (filePath.EndsWith(".exe") || filePath.EndsWith(".dll")))
                    {
                        // [FIX:FALSO-POSITIVO-JOGOS] EXECUTÁVEL PEQUENO E ASSINADO NÃO É AMEAÇA.
                        //
                        // Um binário de poucos KB que se apresenta como
                        // assinado e íntegro não é um downloader: um dropper
                        // depende de baixar outros arquivos para fazer sentido,
                        // e um jogo legítimo não cabe em 10 KB. O tamanho
                        // minúsculo só é indício de risco quando não há
                        // procedência comprovada.
                        if (sig.IsSigned && sig.IsValid)
                        {
                            assessment.Flags.Add(
                                $"Executavel pequeno ({tamanhoArquivo} bytes) porem assinado por {sig.Publisher} (sem peso)");
                        }
                        else
                        {
                            score += 10;
                            assessment.Flags.Add($"Executável muito pequeno ({tamanhoArquivo} bytes)");
                        }
                    }
                }

                // [FIX:FALSO-POSITIVO-JOGOS] SINAIS FRACOS NÃO VIRAM AMEAÇA SOZINHOS.
                //
                // Este é o ponto que fecha o defeito das cinco "ameaças" que o
                // usuário viu, e ele não é sobre nenhum sinal específico — é
                // sobre a REGRA que os combinava.
                //
                // O que os cinco executáveis de servidor de jogo somavam, a
                // cada scan:
                //
                //     não assinado ........... +25
                //     imports suspeitos ...... +20
                //     empacotado ............. +15
                //     entropia elevada ....... +10
                //     --------------------------------
                //     TOTAL ..................  70  (= o limiar de ameaça)
                //
                // E o log mostrava exatamente 70, três scans seguidos, sempre
                // os mesmos cinco processos. Nenhum deles tinha UM sinal forte:
                // nenhum indicator de Metasploit, nenhuma assinatura inválida,
                // nenhum executável minúsculo. Eram quatro sinais fracos, que
                // juntos somavam um número que não distingue nada.
                //
                // A regra correta é: um sinal forte (Metasploit, assinatura que
                // falha ao ser verificada) é suficiente. Sinais fracos — não
                // assinado, empacotado, entropia, imports — são INDICIOS, e
                // podem acumular-se até um limite, mas esse limite tem que ser
                // MENOR que o das ameaças, porque eles se combinam em software
                // legítimo o tempo todo.
                //
                // Sem essa distinção, a pontuação perde o sentido: ela mede
                // "quantos sinais fracos existem", que é uma propriedade da
                // indústria de software, e não "isto é malware".
                bool sinalForte = pe.HasMetasploitIndicators || (sig.IsSigned && !sig.IsValid);

                if (!sinalForte && score >= 70)
                {
                    // Teto de sinais fracos: 55. Um binário que chega aqui tem
                    // vários indicios, e continua SENDO registrado e visível —
                    // apenas não vira "ameaça" automaticamente. A diferença
                    // importa: antes ele disparava notificação, contava na
                    // visão geral e poluía a tela do usuário com falsos
                    // positivos a cada dois minutos.
                    score = 55;
                    assessment.Flags.Add(
                        "Teto de sinais fracos aplicado: nenhum indicador forte (nada confirma malware)");
                }

                assessment.RiskScore = Math.Max(0, Math.Min(score, 100));
                assessment.RiskLevel = score switch
                {
                    >= 70 => LocalizationService.Instance.GetString("RiskAssessmentCritical"),
                    >= 50 => LocalizationService.Instance.GetString("RiskAssessmentHigh"),
                    >= 30 => LocalizationService.Instance.GetString("RiskAssessmentMedium"),
                    >= 10 => LocalizationService.Instance.GetString("RiskAssessmentLow"),
                    _ => LocalizationService.Instance.GetString("RiskAssessmentSafe")
                };
                assessment.Reason = assessment.Flags.Count > 0
                    ? string.Join(" | ", assessment.Flags)
                    : "Nenhum indicador de risco";

                // [FIX:LOG-FLAGS] O LOG PRECISA DIZER POR QUE.
                //
                // A linha registrava `Flags=3` — a CONTAGEM dos sinais, não
                // quais são. Isso tornava o score impossível de auditar: um
                // arquivo com score 70 poderia ser (não assinado + empacotado
                // + executável pequeno) ou (assinatura inválida + entropia
                // alta + Metasploit), e o log tratava os dois como idênticos.
                //
                // Foi exatamente esse buraco que escondeu o defeito dos
                // servidores de jogo: eles somavam 70 pelos motivos errados, e
                // ninguém conseguia ver quais eram sem depurador.
                //
                // A correção é registrar os sinais. O custo é uma linha de log
                // um pouco maior por arquivo avaliado, e o benefício é que
                // qualquer decisão de ameaça passa a ser auditável por leitura.
                _logger.LogInfo(
                    $"[RiskAssess] {Path.GetFileName(filePath)}: Score={assessment.RiskScore}, " +
                    $"Level={assessment.RiskLevel} :: {string.Join(" | ", assessment.Flags)}");

                return assessment;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RiskAssess] Erro ao avaliar {filePath}", ex);
                assessment.RiskScore = 0;
                assessment.RiskLevel = LocalizationService.Instance.GetString("RiskAssessmentError");
                return assessment;
            }
        }
    }

    #region Models

    public class SignatureResult
    {
        public string FilePath { get; set; } = string.Empty;
        public bool IsSigned { get; set; }
        public bool IsValid { get; set; }
        public bool IsTrustedPublisher { get; set; }
        public string? Publisher { get; set; }
        public string? Issuer { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public SignatureStatus Status { get; set; }
    }

    public enum SignatureStatus
    {
        Valid, Invalid, Unsigned, NotApplicable, FileNotFound, Error
    }

    public class PeAnalysisResult
    {
        public string FilePath { get; set; } = string.Empty;
        public bool IsValidPe { get; set; }
        public bool IsPacked { get; set; }
        public string PackerName { get; set; } = string.Empty;
        public bool HasSuspiciousImports { get; set; }
        public bool HasMetasploitIndicators { get; set; }
        public List<string> SuspiciousImports { get; set; } = new();
        public List<string> MetasploitIndicators { get; set; } = new();
    }

    public class FileRiskAssessment
    {
        public string FilePath { get; set; } = string.Empty;
        public int RiskScore { get; set; }
        public string RiskLevel { get; set; } = "Desconhecido";
        public string Reason { get; set; } = string.Empty;
        public double Entropy { get; set; }
        public List<string> Flags { get; set; } = new();
        public SignatureResult? Signature { get; set; }
        public PeAnalysisResult? PeAnalysis { get; set; }
    }

    #endregion
}
