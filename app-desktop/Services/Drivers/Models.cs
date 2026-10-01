using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Categoria funcional do dispositivo. Usada para ícone, agrupamento e elegibilidade
    /// de atualização. Deriva do ClassGuid/ClassName reais do SetupAPI — nunca de suposição.
    /// </summary>
    public enum DriverCategory
    {
        Unknown = 0,
        Gpu,
        Chipset,
        Audio,
        Network,
        Wifi,
        Bluetooth,
        Storage,
        Usb,
        Input,
        Monitor,
        Printer,
        Camera,
        Battery,
        Firmware
    }

    /// <summary>
    /// Resultado da instalação de um driver, por dispositivo. É a máquina de estados real
    /// da interface: cada valor corresponde a um texto e a uma ação de botão coerentes.
    /// Substitui as comparações de string frágeis que quebravam o botão de instalação.
    /// </summary>
    public enum DriverInstallState
    {
        /// <summary>Varredura ainda não executada.</summary>
        Unknown = 0,
        /// <summary>Varredura executada, driver atual e íntegro.</summary>
        UpToDate,
        /// <summary>Existe uma versão nova verificada e ainda não baixada.</summary>
        UpdateAvailable,
        /// <summary>Download em andamento (com progresso).</summary>
        Downloading,
        /// <summary>Download concluído, validado e aguardando instalação.</summary>
        ReadyToInstall,
        /// <summary>Instalação em andamento.</summary>
        Installing,
        /// <summary>Instalado e validado com sucesso.</summary>
        Installed,
        /// <summary>Falhou; a ação do botão é "Tentar novamente".</summary>
        Failed,
        /// <summary>Sem atualização automática possível; permite instalar um INF manualmente.</summary>
        ManualOnly,
        /// <summary>Dispositivo com problema de driver detectado (triângulo amarelo).</summary>
        AttentionRequired
    }

    public class DeviceInfo
    {
        public string? DeviceInstanceId { get; set; }
        public string? FriendlyName { get; set; }
        public string? Description { get; set; }
        public string? Vendor { get; set; }
        public string? HardwareIds { get; set; }
        public string? CompatibleIds { get; set; }
        public string? Service { get; set; }
        public string? DriverInfPath { get; set; }
        public string? DriverVersion { get; set; }
        public string? DriverDate { get; set; }
        public DateTime? DriverInstallDate { get; set; }
        public string? DriverProvider { get; set; }
        public string? ClassGuid { get; set; }
        public string? ClassName { get; set; }
        public bool IsRunning { get; set; }
        public bool IsProblem { get; set; }
        public string? Category { get; set; }
        public string? DriverStatus { get; set; }
        public bool? IsDriverUpToDate { get; set; }

        /// <summary>Código de problema do CM_Get_DevNode_Status (CM_PROB_*), 0 = OK.</summary>
        public uint ProblemCode { get; set; }

        /// <summary>Categoria funcional derivada do ClassGuid/ClassName reais.</summary>
        public DriverCategory CategoryKind { get; set; } = DriverCategory.Unknown;

        /// <summary>Hardware IDs separados, para validação de compatibilidade.</summary>
        public IReadOnlyList<string> HardwareIdList
        {
            get
            {
                if (string.IsNullOrWhiteSpace(HardwareIds)) return Array.Empty<string>();
                return HardwareIds.Split(';', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        /// <summary>
        /// Versão instalada em formato ordenável, ou null quando o dispositivo não tem driver.
        /// </summary>
        public Version? ParsedDriverVersion =>
            DriverVersion != null && Version.TryParse(NormalizeVersion(DriverVersion), out var v) ? v : null;

        /// <summary>Normaliza "6.1.7601.17514" / "10.0.1" / "1.2.3.4.5" para um formato que Version.TryParse aceite.</summary>
        private static string NormalizeVersion(string raw)
        {
            var parts = raw.Split('.', StringSplitOptions.RemoveEmptyEntries)
                           .Where(p => p.All(char.IsDigit))
                           .Take(4)
                           .ToList();
            while (parts.Count < 2) parts.Add("0");
            return string.Join('.', parts);
        }

        /// <summary>
        /// Descrição técnica do problema do dispositivo, derivada do código real do Windows.
        /// </summary>
        public string ProblemDescription => ProblemCode switch
        {
            0 => string.Empty,
            1 => "Dispositivo desabilitado pelo administrador",
            2 => "Não pôde ser inicializado",
            3 => "Driver ausente",
            4 => "Recurso ausente",
            5 => "Associação de drivers com defeito",
            6 => "Serviço não iniciado",
            7 => "Bloco de memória corrompido",
            8 => "Falha na detecção de hardware",
            9 => "Falha de energia",
            10 => "Driver incompatível",
            11 => "Serviço de driver bloqueado",
            12 => "Dispositivo removido (não presente)",
            13 => "Falha na verificação de assinatura",
            14 => "Controlador de disco com problema",
            15 => "Migração necessária",
            16 => "Driver não suporta este hardware",
            17 => "Falha ao atualizar o driver",
            18 => "Recurso removido",
            19 => "Falha de linha de comando",
            20 => "Falha ao digitalizar",
            21 => "Não pode ser carregado",
            22 => "Identificador de serviço inválido",
            23 => "Provedor de controle de cache corrompido",
            24 => "Objeto de dispositivo removido",
            25 => "Falha de pilha de driver",
            26 => "Objeto de driver removido",
            27 => "Falha de digitação",
            28 => "Driver não assinado",
            29 => "Alteração pendente",
            30 => "Problema de vídeo",
            _ => $"Código de problema {ProblemCode}"
        };

        /// <summary>
        /// Nome de exibição do dispositivo — prioriza FriendlyName, depois Description.
        /// </summary>
        public string DeviceName => FriendlyName ?? Description ?? "Dispositivo Desconhecido";
    }

    /// <summary>
    /// De onde veio a informação de "há uma versão nova" e qual é o grau de confiança.
    ///
    /// CRÍTICO: até esta auditoria, os detectores inventavam versões fixas (ex.:
    /// "Realtek 11.16.2.1", "Intel Bluetooth 24.30.1.1") com ReleaseDate fixo no momento da
    /// instalação e DownloadUrl apontando para a PÁGINA INICIAL do fabricante — não para o
    /// driver. A UI exibia "atualização disponível" e o download baixava um HTML que, corretamente,
    /// era recusado por falta de assinatura. Nenhuma atualização pode ser oferecida sem uma
    /// destas fontes; na ausência de fonte verificável, o campo é <c>None</c> e nada é exibido.
    /// </summary>
    public enum DriverUpdateProvenance
    {
        /// <summary>Nenhuma fonte respondeu. NUNCA gerar atualização neste caso.</summary>
        None = 0,

        /// <summary>
        /// Catalogado pela Microsoft no Windows Update / Microsoft Update Catalog.
        /// Fonte mais confiável: o pacote é servido e assinado pela Microsoft.
        /// </summary>
        MicrosoftUpdateCatalog,

        /// <summary>Obtido do Intel Driver & Support Assistant (ferramenta oficial da Intel).</summary>
        IntelDsa,

        /// <summary>Fabricante expôs um endpoint oficial com JSON/manifest versionado.</summary>
        VendorOfficialApi,

        /// <summary>
        /// Lido do HTML do site do fabricante. Frágil (bloqueio 403, mudança de layout) e
        /// ainda precisa de validação de assinatura no download.
        /// </summary>
        VendorWebScraping
    }

    public class DriverPackage
    {
        public string HardwareId { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public DateTime ReleaseDate { get; set; }
        public string DownloadUrl { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string InfFile { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Changelog { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ReleaseNotes { get; set; } = string.Empty;

        /// <summary>
        /// Verdadeiro quando o driver é real e verificado, mas não há URL direta: ele deve ser
        /// baixado e instalado pelo próprio Windows Update (IUpdateDownloader/IUpdateInstaller).
        ///
        /// Existe porque o WUA não expõe a URL do artefato antes de o arquivo inteiro ter sido
        /// baixado pelo cache do Windows Update. Tratar esse caso como "sem fonte" descartaria
        /// atualizações legítimas — o caminho do WUA é justamente a fonte mais confiável, pois
        /// o Microsoft já fez o matching por Hardware ID.
        /// </summary>
        public bool RequiresWuapiInstall { get; set; }

        /// <summary>Fonte da informação (ver <see cref="DriverUpdateProvenance"/>).</summary>
        public DriverUpdateProvenance Provenance { get; set; } = DriverUpdateProvenance.None;

        /// <summary>Identificador do update na fonte (ex.: UpdateID do catálogo Microsoft).</summary>
        public string SourceReference { get; set; } = string.Empty;

        /// <summary>
        /// Verdadeiro apenas quando a informação veio de uma fonte real e a URL aponta para um
        /// ARQUIVO de driver (não para uma página de navigation), OU quando o download deve
        /// ocorrer pelo próprio Windows Update. A UI usa isso para nunca apresentar como
        /// "atualização garantida" o que é apenas uma página de consulta.
        /// </summary>
        public bool IsVerified =>
            Provenance != DriverUpdateProvenance.None &&
            (RequiresWuapiInstall || LooksLikeDriverArtifact(DownloadUrl));

        /// <summary>Rótulo curto da fonte, exibido na interface.</summary>
        public string ProvenanceLabel => Provenance switch
        {
            DriverUpdateProvenance.MicrosoftUpdateCatalog => RequiresWuapiInstall
                ? "Windows Update (instalado pelo Windows)"
                : "Catálogo Microsoft",
            DriverUpdateProvenance.IntelDsa => "Intel DSA",
            DriverUpdateProvenance.VendorOfficialApi => "API do fabricante",
            DriverUpdateProvenance.VendorWebScraping => "Site do fabricante",
            _ => "Sem fonte"
        };

        /// <summary>
        /// Heurística de "isto é um artefato de driver, não uma página de HTML".
        /// Páginas de consulta (/download-center/home.html, /component-download/audio, raiz do site)
        /// NÃO são pacotes e nunca devem ser apresentadas como download de driver.
        /// </summary>
        private static bool LooksLikeDriverArtifact(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

            string path = uri.AbsolutePath.TrimEnd('/');
            if (path.Length == 0) return false;                 // raiz do site
            if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase)) return false;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".zip" or ".cab" or ".exe" or ".msi" or ".7z" or ".inf";
        }
    }


    public class DriverUpdate
    {
        public DeviceInfo Device { get; set; } = new();
        public DriverPackage NewDriver { get; set; } = new();
        public UpdateReason UpdateReason { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
    }

    public enum UpdateReason
    {
        MajorVersionUpgrade,
        StabilityImprovement,
        MinorUpdate,
        SecurityPatch,
        HardwareRepair,
        NoUpdateNeeded
    }
}
