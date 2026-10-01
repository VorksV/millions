using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Resultado detalhado da verificação Authenticode/WHQL de um arquivo de driver.
    /// </summary>
    public sealed class DriverSignatureReport
    {
        /// <summary>True somente quando o WinVerifyTrust retornou S_OK.</summary>
        public bool IsSigned { get; init; }

        /// <summary>Código HRESULT devolvido pelo WinVerifyTrust.</summary>
        public int HResult { get; init; }

        /// <summary>Descrição legível do HRESULT (TRUST_E_NOSIGNATURE, CERT_E_EXPIRED, ...).</summary>
        public string StatusDescription { get; init; } = string.Empty;

        /// <summary>Assunto do certificado assinante, quando válido.</summary>
        public string? SignerSubject { get; init; }

        /// <summary>Publicador extraído do certificado (rótulo O= do DN).</summary>
        public string? Publisher { get; init; }

        /// <summary>Organização (O=) completa do certificado.</summary>
        public string? Organization { get; init; }

        /// <summary>Data de expiração do certificado.</summary>
        public DateTime? NotAfter { get; init; }

        /// <summary>True quando o publicador pertence a um fabricante de driver reconhecido.</summary>
        public bool IsTrustedPublisher { get; init; }

        public string Summary => IsSigned
            ? $"assinatura válida | assinante={SignerSubject} | publicador={Publisher}"
            : $"assinatura INVÁLIDA | {StatusDescription}";
    }

    /// <summary>
    /// Verificação de assinatura Authenticode/WHQL via WinVerifyTrust.
    ///
    /// MUDANÇA DE SEGURANÇA (FASE 2.6):
    /// A implementação anterior aceitava arquivos NÃO ASSINADOS e devolvia "true" sempre que o
    /// <see cref="SecurityContext.GetCurrentSecurityMode"/> era Maintenance ou quando o arquivo
    /// estava no diretório de atualização do próprio processo. Isso anulava na prática a única
    /// barreira de segurança do pipeline de drivers e é exatamente o "bypass de assinatura de
    /// driver" proibido. O estado real da assinatura agora é sempre reportado, sem exceção.
    /// Nenhum mecanismo de segurança do Windows é desativado.
    /// </summary>
    public class DriverSignatureVerifier
    {
        [DllImport("wintrust.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] in Guid actionId, IntPtr pWinTrustData);

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid(
            0xaac56b, 0xcd44, 0x11d0, 0x8c, 0xc2, 0x0, 0xc0, 0x4f, 0xc2, 0x95, 0xee);

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_SAFER_FLAG = 0x00000100;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;
        private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;
        private const uint WTD_REVOCATION_CHECK_NONE = 0x00000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
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
            [MarshalAs(UnmanagedType.LPWStr)] public string? pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        /// <summary>
        /// Verificação estrita. Retorna o estado real da assinatura; não existe caminho que
        /// converta um arquivo não assinado em "assinado".
        /// </summary>
        public DriverSignatureReport VerifyWithPublisher(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                App.LoggingService?.LogError("[DriverSignature] Arquivo não encontrado para verificação de assinatura.");
                return new DriverSignatureReport
                {
                    IsSigned = false,
                    StatusDescription = "arquivo inexistente"
                };
            }

            IntPtr fileInfoPtr = IntPtr.Zero;
            IntPtr wtDataPtr = IntPtr.Zero;

            try
            {
                var fileInfo = new WINTRUST_FILE_INFO
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                    pcwszFilePath = filePath,
                    hFile = IntPtr.Zero,
                    pgKnownSubject = IntPtr.Zero
                };

                var wtData = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = IntPtr.Zero,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = null,
                    // SAFER + cache-only: não baixa revogação da rede (a validação de cadeia
                    // continua happening na pilha do WinVerifyTrust).
                    dwProvFlags = WTD_SAFER_FLAG | WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE,
                    dwUIContext = 0,
                    pSignatureSettings = IntPtr.Zero
                };

                fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
                wtData.pFile = fileInfoPtr;

                wtDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
                Marshal.StructureToPtr(wtData, wtDataPtr, false);

                var actionId = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                int verifyResult = unchecked((int)WinVerifyTrust(IntPtr.Zero, in actionId, wtDataPtr));

                // Fechar o estado do WinVerifyTrust é obrigatório; sem isso o handle de
                // verificação fica retido a cada chamada.
                var closeData = (WINTRUST_DATA)Marshal.PtrToStructure(wtDataPtr, typeof(WINTRUST_DATA))!;
                closeData.dwStateAction = WTD_STATEACTION_CLOSE;
                Marshal.StructureToPtr(closeData, wtDataPtr, false);
                WinVerifyTrust(IntPtr.Zero, in actionId, wtDataPtr);

                bool isSigned = verifyResult == 0;
                string description = DescribeHResult(verifyResult);

                if (!isSigned)
                {
                    App.LoggingService?.LogError(
                        $"[DriverSignature] REJEITADO '{Path.GetFileName(filePath)}': {description} (0x{verifyResult:X8}). " +
                        "Nenhuma exceção de segurança é aplicada: o arquivo será recusado.");
                    return new DriverSignatureReport { IsSigned = false, HResult = verifyResult, StatusDescription = description };
                }

                var cert = ReadSignerCertificate(filePath);
                string? organization = cert?.Subject;
                string? publisher = ExtractPublisher(cert);
                bool trusted = SecureDriverDownloader.IsPublisherAllowed(publisher) ||
                                SecureDriverDownloader.IsPublisherAllowed(organization);

                App.LoggingService?.LogInfo(
                    $"[DriverSignature] VALIDADO '{Path.GetFileName(filePath)}' | publicador={publisher ?? "(desconhecido)"} | " +
                    $"confiavel={trusted} | expira={cert?.NotAfter:yyyy-MM-dd}");

                return new DriverSignatureReport
                {
                    IsSigned = true,
                    HResult = verifyResult,
                    StatusDescription = description,
                    SignerSubject = cert?.Subject,
                    Organization = organization,
                    Publisher = publisher,
                    NotAfter = cert?.NotAfter,
                    IsTrustedPublisher = trusted
                };
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError(
                    $"[DriverSignature] Falha ao verificar assinatura de '{filePath}'. {ex.Describe()}", ex);
                return new DriverSignatureReport { IsSigned = false, StatusDescription = "erro na API de verificação" };
            }
            finally
            {
                if (fileInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPtr);
                if (wtDataPtr != IntPtr.Zero) Marshal.FreeHGlobal(wtDataPtr);
            }
        }

        /// <summary>
        /// Atalho booleano. Mantido para compatibilidade com os consumidores existentes
        /// (SafeDriverInstaller). Não possui nenhum caminho de bypass.
        /// </summary>
        public bool IsDriverSigned(string filePath) => VerifyWithPublisher(filePath).IsSigned;

        private static X509Certificate2? ReadSignerCertificate(string filePath)
        {
            try
            {
                // A folha (subject) é o certificado do assinante; a cadeia completa fica em
                // X509ChainElement, que não é necessário para decidir o publicador.
                using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(filePath);
                return new X509Certificate2(cert);
            }
            catch (CryptographicException ex)
            {
                App.LoggingService?.LogWarning($"[DriverSignature] Não foi possível extrair o certificado: {ex.Message}");
                return null;
            }
        }

        /// <summary>Extrai o campo O= (Organization) do Distinguished Name do certificado.</summary>
        private static string? ExtractPublisher(X509Certificate2? cert)
        {
            if (cert?.Subject is null) return null;
            foreach (var part in cert.Subject.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith("O=", StringComparison.OrdinalIgnoreCase))
                    return trimmed[2..].Trim();
            }
            return null;
        }

        private static string DescribeHResult(int hr) => hr switch
        {
            0 => "S_OK — assinatura válida",
            unchecked((int)0x800B0100) => "TRUST_E_NOSIGNATURE — arquivo não assinado",
            unchecked((int)0x800B0101) => "CERT_E_EXPIRED — certificado expirado",
            unchecked((int)0x800B0102) => "CERT_E_VALIDITYPERIODNESTING — período de validade aninhado inválido",
            unchecked((int)0x800B0103) => "CERT_E_UNTRUSTEDROOT — raiz não confiável",
            unchecked((int)0x800B0109) => "CERT_E_UNTRUSTEDROOT — cadeia não confiável",
            unchecked((int)0x800B010A) => "CERT_E_CHAINING — cadeia de certificação incompleta",
            unchecked((int)0x800B010B) => "CERT_E_WRONG_USAGE — uso do certificado inválido",
            unchecked((int)0x800B010C) => "CERT_E_REVOKED — certificado revogado",
            unchecked((int)0x800B010D) => "CERT_E_REVOCATION_FAILURE — não foi possível verificar revogação",
            unchecked((int)0x800B010F) => "CERT_E_CN_NO_MATCH — nome do certificado não confere",
            unchecked((int)0x800B0111) => "CERT_E_UNTRUSTEDTESTROOT",
            unchecked((int)0x800B0004) => "TRUST_E_SUBJECT_NOT_TRUSTED — subjects não confiáveis",
            unchecked((int)0x800B010E) => "CERT_E_INVALID_POLICY",
            _ => $"HRESULT 0x{hr:X8}"
        };
    }
}
