using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>Natureza do pacote de driver baixado.</summary>
    public enum DriverPackageKind
    {
        Unknown = 0,
        /// <summary>Arquivo .zip contendo INF + payload.</summary>
        Zip,
        /// <summary>Cabine .cab do Windows Update (INF + payload assinado).</summary>
        Cab,
        /// <summary>Instalador .exe/.msi do fabricante — exige confirmação explícita do usuário.</summary>
        VendorInstaller
    }

    /// <summary>Motivo pelo qual uma etapa do pipeline de driver foi recusada.</summary>
    public enum DriverFailureReason
    {
        None = 0,
        InvalidUrl,
        NetworkTimeout,
        NetworkFailure,
        Cancelled,
        IncompleteDownload,
        SizeMismatch,
        HashMismatch,
        SignatureMissing,
        SignatureInvalid,
        PublisherNotAllowed,
        UnsupportedPackageFormat,
        NoDriverPayload,
        IncompatibleArchitecture,
        HardwareIdMismatch,
        VersionNotNewer
    }

    /// <summary>
    /// Resultado do download de um pacote de driver. Substitui o antigo retorno por string
    /// ("MOCK_SUCCESS" ou um caminho), que permitia simular sucesso sem baixar nada.
    /// </summary>
    public sealed class DriverDownloadResult
    {
        public bool Success { get; init; }
        public DriverFailureReason Failure { get; init; } = DriverFailureReason.None;

        /// <summary>Caminho do arquivo baixado e validado (mantido em disco apenas se Success).</summary>
        public string? PackagePath { get; init; }

        /// <summary>Pasta extraída contendo os .INF, quando o pacote é Zip ou Cab.</summary>
        public string? ExtractedFolder { get; init; }

        /// <summary>Arquivos .INF encontrados no pacote.</summary>
        public IReadOnlyList<string> InfFiles { get; init; } = Array.Empty<string>();

        public DriverPackageKind Kind { get; init; } = DriverPackageKind.Unknown;

        /// <summary>Tamanho real do arquivo baixado, em bytes.</summary>
        public long SizeBytes { get; init; }

        /// <summary>SHA-256 calculado localmente sobre o arquivo baixado.</summary>
        public string? Sha256 { get; init; }

        /// <summary>Host de origem efetivamente acessado (auditoria de procedência).</summary>
        public string? SourceHost { get; init; }

        /// <summary>URL final após redirecionamentos.</summary>
        public string? FinalUrl { get; init; }

        /// <summary>Assinante do Authenticode, quando validado.</summary>
        public string? SignerSubject { get; init; }

        /// <summary>Publicador extraído do certificado.</summary>
        public string? Publisher { get; init; }

        public TimeSpan Duration { get; init; }

        /// <summary>Descrição legível da falha, já em PT-BR, pronta para exibição.</summary>
        public string ErrorMessage => Failure switch
        {
            DriverFailureReason.None => string.Empty,
            DriverFailureReason.InvalidUrl => "URL de download inválida.",
            DriverFailureReason.NetworkTimeout => "Tempo limite excedido ao baixar o pacote.",
            DriverFailureReason.NetworkFailure => "Falha de rede ao baixar o pacote.",
            DriverFailureReason.Cancelled => "Operação cancelada pelo usuário.",
            DriverFailureReason.IncompleteDownload => "Download incompleto — arquivo descartado.",
            DriverFailureReason.SizeMismatch => "O tamanho do arquivo não corresponde ao announced pelo fabricante.",
            DriverFailureReason.HashMismatch => "O hash SHA-256 não confere com o anunciado. Arquivo rejeitado.",
            DriverFailureReason.SignatureMissing => "O pacote não possui assinatura digital. Rejeitado por segurança.",
            DriverFailureReason.SignatureInvalid => "A assinatura digital do pacote é inválida. Rejeitado por segurança.",
            DriverFailureReason.PublisherNotAllowed => "O publicador do pacote não é reconhecido como fabricante de drivers.",
            DriverFailureReason.UnsupportedPackageFormat => "Formato de pacote não suportado para instalação automática.",
            DriverFailureReason.NoDriverPayload => "O pacote não contém nenhum descritor de driver (.INF).",
            DriverFailureReason.IncompatibleArchitecture => "O pacote não é compatível com a arquitetura deste sistema.",
            DriverFailureReason.HardwareIdMismatch => "O pacote não declara suporte ao Hardware ID deste dispositivo.",
            DriverFailureReason.VersionNotNewer => "A versão disponível não é superior à instalada.",
            _ => "Falha desconhecida no processamento do driver."
        };

        public static DriverDownloadResult Ok(string? packagePath, string? extractedFolder,
            IReadOnlyList<string> infFiles, DriverPackageKind kind, long size, string? sha256,
            string? sourceHost, string? finalUrl, string? signer, string? publisher, TimeSpan duration) =>
            new()
            {
                Success = true,
                PackagePath = packagePath,
                ExtractedFolder = extractedFolder,
                InfFiles = infFiles,
                Kind = kind,
                SizeBytes = size,
                Sha256 = sha256,
                SourceHost = sourceHost,
                FinalUrl = finalUrl,
                SignerSubject = signer,
                Publisher = publisher,
                Duration = duration
            };

        public static DriverDownloadResult Fail(DriverFailureReason reason, TimeSpan duration) =>
            new() { Success = false, Failure = reason, Duration = duration };
    }
}
