using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VoltrisOptimizer.Services.Drivers;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Modelo de linha da página de Drivers.
    ///
    /// Substitui a comparação de strings traduzidas que governava o botão de instalação.
    /// A causa raiz do botão invisível era dupla:
    ///  a) o code-behind gravava "Atualizao Disponvel" (sem acentos) enquanto o XAML
    ///     comparava com "Atualização Disponível";
    ///  b) outros estados usavam LocalizationService.GetString("Error")/("ProcessingStatus"),
    ///     que não coincidem com "Erro"/"Processando" esperado pelos triggers.
    /// Nenhum binding agora depende de texto traduzido para decidir visibilidade ou ação.
    /// </summary>
    public sealed class DriverItemViewModel : INotifyPropertyChanged
    {
        private static readonly Geometry? FallbackGeometry =
            DriverIconResolver.GetCategoryGeometry(DriverCategory.Unknown);

        private string _deviceName = string.Empty;
        private string _vendor = string.Empty;
        private string _category = string.Empty;
        private string _driverVersion = "—";
        private string _driverDate = "—";
        private string _provider = "—";
        private string _statusText = string.Empty;
        private string _operationText = string.Empty;
        private string _availableVersion = "—";
        private string _releaseDate = "—";
        private string _sourceUrl = string.Empty;
        private string _fileSize = "—";
        private string _problemDescription = string.Empty;
        private DriverInstallState _state = DriverInstallState.Unknown;
        private DriverCategory _categoryKind = DriverCategory.Unknown;
        private double _downloadProgress;
        private bool _isBusy;
        private ImageSource? _shellIcon;
        private Geometry? _categoryIcon;
        private DriverUpdate? _driverUpdate;
        private DeviceInfo? _device;

        public DriverItemViewModel()
        {
            _categoryIcon = FallbackGeometry;
        }

        // ---------------------------------------------------------------- identidade

        public string DeviceName
        {
            get => _deviceName;
            set => Set(ref _deviceName, value);
        }

        public string Vendor
        {
            get => _vendor;
            set => Set(ref _vendor, value);
        }

        /// <summary>Rótulo do grupo (usado no GroupStyle do ItemsControl).</summary>
        public string Category
        {
            get => _category;
            set => Set(ref _category, value);
        }

        public DriverCategory CategoryKind
        {
            get => _categoryKind;
            set
            {
                if (Set(ref _categoryKind, value))
                    CategoryIcon = DriverIconResolver.GetCategoryGeometry(value) ?? FallbackGeometry;
            }
        }

        public string? HardwareId { get; set; }
        public string? HardwareIds { get; set; }
        public DeviceInfo? Device
        {
            get => _device;
            set => Set(ref _device, value);
        }

        public DriverUpdate? DriverUpdate
        {
            get => _driverUpdate;
            set
            {
                if (Set(ref _driverUpdate, value))
                {
                    if (value?.NewDriver != null)
                    {
                        AvailableVersion = string.IsNullOrWhiteSpace(value.NewDriver.Version) ? "—" : value.NewDriver.Version;
                        ReleaseDate = value.NewDriver.ReleaseDate == default
                            ? "—"
                            : value.NewDriver.ReleaseDate.ToString("dd/MM/yyyy");
                        SourceUrl = value.NewDriver.SourceUrl ?? value.NewDriver.DownloadUrl ?? string.Empty;
                        FileSize = value.NewDriver.FileSize > 0
                            ? $"{value.NewDriver.FileSize / 1024d / 1024d:0.0} MB"
                            : "—";
                        if (!string.IsNullOrWhiteSpace(value.NewDriver.Vendor)) Vendor = value.NewDriver.Vendor;
                        RaiseProvenance();
                    }
                }
            }
        }

        // -------------------------------------------------- proveniência / auditoria

        private string _provenanceLabel = string.Empty;
        private string _provenanceDetail = string.Empty;
        private string _auditNote = string.Empty;
        private bool _isVerified;

        /// <summary>
        /// Texto aud explicando por que este dispositivo tem ou não tem atualização.
        /// Deixa explícito qual fonte foi consultada e qual foi o resultado, para que o
        /// usuário nunca atribua ao VOLTRIS uma limitação que é do canal de distribuição.
        /// </summary>
        public string AuditNote
        {
            get => _auditNote;
            set => Set(ref _auditNote, value);
        }

        /// <summary>Estado real do catálogo local (fonte primária, como no Driver Booster).</summary>
        public string CatalogState => VoltrisOptimizer.Services.Drivers.DriverCatalog.Instance.DescribeState();

        /// <summary>Fonte da informação exibida ao usuário (ex.: "Catálogo Microsoft").</summary>
        public string ProvenanceLabel
        {
            get => _provenanceLabel;
            private set => Set(ref _provenanceLabel, value);
        }

        /// <summary>Detalhe auditável da fonte (host, referência, motivo da recusa).</summary>
        public string ProvenanceDetail
        {
            get => _provenanceDetail;
            private set => Set(ref _provenanceDetail, value);
        }

        /// <summary>
        /// True somente quando a informação vem de uma fonte real E a URL é o artefato do
        /// driver. A interface usa isso para nunca apresentar como "garantido" algo que não é.
        /// </summary>
        public bool IsVerified
        {
            get => _isVerified;
            private set => Set(ref _isVerified, value);
        }

        private void RaiseProvenance()
        {
            var pkg = DriverUpdate?.NewDriver;
            if (pkg == null)
            {
                ProvenanceLabel = string.Empty;
                ProvenanceDetail = string.Empty;
                IsVerified = false;
                return;
            }

            ProvenanceLabel = pkg.ProvenanceLabel;
            IsVerified = pkg.IsVerified;

            string host = string.Empty;
            if (Uri.TryCreate(pkg.DownloadUrl, UriKind.Absolute, out var uri)) host = uri.Host;
            if (string.IsNullOrEmpty(host) && Uri.TryCreate(pkg.SourceUrl, UriKind.Absolute, out var suri)) host = suri.Host;

            ProvenanceDetail = pkg.IsVerified
                ? $"Fonte: {pkg.ProvenanceLabel} | host: {host}" +
                  (string.IsNullOrEmpty(pkg.SourceReference) ? "" : $" | ref: {pkg.SourceReference}")
                : $"Informação de {pkg.ProvenanceLabel}, mas sem link direto para o pacote (host: {host}). " +
                  "A assinatura do download é verificada antes de qualquer instalação.";
        }

        // -------------------------------------------------------------------- dados

        public string DriverVersion
        {
            get => _driverVersion;
            set => Set(ref _driverVersion, value);
        }

        public string DriverDate
        {
            get => _driverDate;
            set => Set(ref _driverDate, value);
        }

        public string Provider
        {
            get => _provider;
            set => Set(ref _provider, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }

        /// <summary>
        /// Linha de progresso da operação em curso (download, instalação, validação).
        /// Quando vazia, a UI exibe <see cref="StatusText"/>.
        /// </summary>
        public string OperationText
        {
            get => _operationText;
            set
            {
                if (Set(ref _operationText, value))
                    OnPropertyChanged(nameof(EffectiveStatusText));
            }
        }

        /// <summary>Texto efetivamente exibido na linha de status.</summary>
        public string EffectiveStatusText =>
            string.IsNullOrWhiteSpace(_operationText) ? _statusText : _operationText;

        public string AvailableVersion
        {
            get => _availableVersion;
            set => Set(ref _availableVersion, value);
        }

        public string ReleaseDate
        {
            get => _releaseDate;
            set => Set(ref _releaseDate, value);
        }

        public string SourceUrl
        {
            get => _sourceUrl;
            set => Set(ref _sourceUrl, value);
        }

        public string FileSize
        {
            get => _fileSize;
            set => Set(ref _fileSize, value);
        }

        public string ProblemDescription
        {
            get => _problemDescription;
            set => Set(ref _problemDescription, value);
        }

        // -------------------------------------------------------------------- ícones

        /// <summary>Ícone nativo do Windows (pode ser nulo — o fallback cobre esse caso).</summary>
        public ImageSource? ShellIcon
        {
            get => _shellIcon;
            set => Set(ref _shellIcon, value);
        }

        /// <summary>Ícone vetorial da categoria. Nunca nulo: garante que a linha nunca fique vazia.</summary>
        public Geometry? CategoryIcon
        {
            get => _categoryIcon;
            set => Set(ref _categoryIcon, value);
        }

        /// <summary>True quando existe ícone nativo do fabricante para exibir por cima do fallback.</summary>
        public bool HasShellIcon => _shellIcon != null;

        // -------------------------------------------------------------------- estado

        /// <summary>
        /// Estado real do fluxo. Toda a apresentação (tag, cor, texto e ação do botão) deriva
        /// deste enumerador, nunca de texto traduzido.
        /// </summary>
        public DriverInstallState State
        {
            get => _state;
            set
            {
                if (!Set(ref _state, value)) return;
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(IsActionAvailable));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsTerminalSuccess));
                OnPropertyChanged(nameof(ShowDownloadProgress));
                OnPropertyChanged(nameof(ShowVersionBlock));
                OnPropertyChanged(nameof(IsIndeterminateProgress));
            }
        }

        /// <summary>Texto da etiqueta de status.</summary>
        public string StateText => _state switch
        {
            DriverInstallState.Unknown => "Não verificado",
            DriverInstallState.UpToDate => "Atualizado",
            DriverInstallState.UpdateAvailable => "Atualização disponível",
            DriverInstallState.Downloading => "Baixando…",
            DriverInstallState.ReadyToInstall => "Instalando…",
            DriverInstallState.Installing => "Instalando…",
            DriverInstallState.Installed => "Instalado",
            DriverInstallState.Failed => "Erro — tentar novamente",
            DriverInstallState.ManualOnly => "Instalação manual",
            DriverInstallState.AttentionRequired => "Atenção necessária",
            _ => "—"
        };

        /// <summary>Texto do botão de ação. O botão NUNCA desaparece por falha de binding.</summary>
        public string ActionText => _state switch
        {
            DriverInstallState.UpdateAvailable => "Baixar",
            DriverInstallState.Downloading => "Baixando…",
            DriverInstallState.ReadyToInstall => "Instalando…",
            DriverInstallState.Installing => "Instalando…",
            DriverInstallState.Installed => "Instalado",
            DriverInstallState.Failed => "Tentar novamente",
            DriverInstallState.ManualOnly => "Instalar INF",
            DriverInstallState.AttentionRequired => "Reparar",
            DriverInstallState.UpToDate => "Verificar",
            DriverInstallState.Unknown => "Verificar",
            _ => "Verificar"
        };

        /// <summary>
        /// O botão fica sempre visível. A única razão para desabilitá-lo é uma operação em
        /// curso — e mesmo assim ele permanece visível e apenas não clicável.
        /// </summary>
        public bool IsActionAvailable => true;

        public bool IsTerminalSuccess => _state == DriverInstallState.Installed;

        public bool ShowVersionBlock =>
            _state is DriverInstallState.UpdateAvailable or DriverInstallState.ReadyToInstall
                  or DriverInstallState.Installing or DriverInstallState.Installed
                  or DriverInstallState.Failed;

        public bool ShowDownloadProgress =>
            _state is DriverInstallState.Downloading or DriverInstallState.ReadyToInstall
                  or DriverInstallState.Installing;

        public bool IsIndeterminateProgress => _state == DriverInstallState.Installing;

        /// <summary>True enquanto uma operação está em curso (botão desabilitado, sem sumir).</summary>
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (Set(ref _isBusy, value))
                    OnPropertyChanged(nameof(IsActionEnabled));
            }
        }

        public bool IsActionEnabled => !_isBusy;

        /// <summary>Progresso 0..1 do download. 0 enquanto não há download em curso.</summary>
        public double DownloadProgress
        {
            get => _downloadProgress;
            set
            {
                if (Set(ref _downloadProgress, value))
                    OnPropertyChanged(nameof(DownloadProgressText));
            }
        }

        public string DownloadProgressText => _downloadProgress > 0
            ? $"{_downloadProgress * 100:0}%"
            : string.Empty;

        // ---------------------------------------------------------------- notificação

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        /// <summary>
        /// Cria a linha a partir de um dispositivo real detectado pelo SetupAPI.
        /// Chamado na UI thread em lote: os ícones são resolvidos por cache (≈25 classes
        /// e ≈15 categorias), não por dispositivo.
        /// </summary>
        public static DriverItemViewModel FromDevice(DeviceInfo device)
        {
            var item = new DriverItemViewModel
            {
                Device = device,
                HardwareId = device.DeviceInstanceId,
                HardwareIds = device.HardwareIds,
                DeviceName = device.DeviceName,
                Vendor = string.IsNullOrWhiteSpace(device.Vendor) ? "Desconhecido" : device.Vendor,
                CategoryKind = device.CategoryKind,
                Category = string.IsNullOrWhiteSpace(device.Category) ? "Outros" : device.Category,
                DriverVersion = string.IsNullOrWhiteSpace(device.DriverVersion) ? "—" : device.DriverVersion,
                DriverDate = FormatDriverDate(device.DriverDate),
                Provider = string.IsNullOrWhiteSpace(device.DriverProvider) ? "—" : device.DriverProvider,
                ShellIcon = DriverIconResolver.GetShellIcon(device.ClassGuid)
            };

            item.StatusText = BuildStatusText(device);
            item.ProblemDescription = device.ProblemDescription;
            item.State = device.IsProblem ? DriverInstallState.AttentionRequired : DriverInstallState.UpToDate;
            return item;
        }

        private static string FormatDriverDate(string? rawDriverDate)
        {
            if (string.IsNullOrWhiteSpace(rawDriverDate)) return "—";

            // DriverDate no registro vem como "MMddyyyy" (ex.: 09242024).
            if (rawDriverDate.Length == 8 && rawDriverDate.All(char.IsDigit))
            {
                return $"{rawDriverDate.Substring(0, 2)}/{rawDriverDate.Substring(2, 2)}/{rawDriverDate.Substring(4, 4)}";
            }
            return DateTime.TryParse(rawDriverDate, out var parsed) ? parsed.ToString("dd/MM/yyyy") : rawDriverDate;
        }

        private static string BuildStatusText(DeviceInfo device)
        {
            if (device.IsProblem && !string.IsNullOrEmpty(device.ProblemDescription))
                return $"Problema: {device.ProblemDescription}";

            if (device.IsProblem)
                return "Problema detectado no dispositivo";

            if (device.IsRunning)
                return "Ativo e operando normalmente";

            return "Instalado, porém inativo";
        }
    }
}
