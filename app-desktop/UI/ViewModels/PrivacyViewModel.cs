using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

   namespace VoltrisOptimizer.UI.ViewModels
   {
       public class PrivacyViewModel : INotifyPropertyChanged
       {
           private readonly IPrivacyTuningService _privacyService;
           private readonly ILoggingService _logger;
           private const string TAG = "[PrivacyVM]";

        private bool _isTelemetryDisabled;
        public bool IsTelemetryDisabled
        {
            get => _isTelemetryDisabled;
            set 
            { 
                if (_isTelemetryDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} Telemetry toggle: {value}");
                    _isTelemetryDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("Telemetry", value);
                } 
            }
        }

        private bool _isLocationDisabled;
        public bool IsLocationDisabled
        {
            get => _isLocationDisabled;
            set 
            { 
                if (_isLocationDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} Location toggle: {value}");
                    _isLocationDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("Location", value);
                } 
            }
        }

        private bool _isAdvertisingIdDisabled;
        public bool IsAdvertisingIdDisabled
        {
            get => _isAdvertisingIdDisabled;
            set 
            { 
                if (_isAdvertisingIdDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} AdvertisingID toggle: {value}");
                    _isAdvertisingIdDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("AdvertisingID", value);
                } 
            }
        }

        private bool _isCortanaDisabled;
        public bool IsCortanaDisabled
        {
            get => _isCortanaDisabled;
            set 
            { 
                if (_isCortanaDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} Cortana toggle: {value}");
                    _isCortanaDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("Cortana", value);
                } 
            }
        }

        private bool _isCoPilotDisabled;
        public bool IsCoPilotDisabled
        {
            get => _isCoPilotDisabled;
            set 
            { 
                if (_isCoPilotDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} CoPilot toggle: {value}");
                    _isCoPilotDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("CoPilot", value);
                } 
            }
        }
    
        private bool _isRecallDisabled;
        public bool IsRecallDisabled
        {
            get => _isRecallDisabled;
            set 
            { 
                if (_isRecallDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} Recall toggle: {value}");
                    _isRecallDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("Recall", value);
                } 
            }
        }

        private bool _isBluetoothDisabled;
        public bool IsBluetoothDisabled
        {
            get => _isBluetoothDisabled;
            set 
            { 
                if (_isBluetoothDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} BluetoothAdvertising toggle: {value}");
                    _isBluetoothDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("BluetoothAdvertising", value);
                } 
            }
        }

        private bool _isHandwritingDisabled;
        public bool IsHandwritingDisabled
        {
            get => _isHandwritingDisabled;
            set 
            { 
                if (_isHandwritingDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} HandwritingDataSharing toggle: {value}");
                    _isHandwritingDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("HandwritingDataSharing", value);
                } 
            }
        }

        private bool _isTextInputDisabled;
        public bool IsTextInputDisabled
        {
            get => _isTextInputDisabled;
            set 
            { 
                if (_isTextInputDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} TextInputDataCollection toggle: {value}");
                    _isTextInputDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("TextInputDataCollection", value);
                } 
            }
        }

        private bool _isPersonalizationDisabled;
        public bool IsPersonalizationDisabled
        {
            get => _isPersonalizationDisabled;
            set 
            { 
                if (_isPersonalizationDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} InputPersonalization toggle: {value}");
                    _isPersonalizationDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("InputPersonalization", value);
                } 
            }
        }

        private bool _isActivityUploadDisabled;
        public bool IsActivityUploadDisabled
        {
            get => _isActivityUploadDisabled;
            set 
            { 
                if (_isActivityUploadDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} ActivityUploads toggle: {value}");
                    _isActivityUploadDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("ActivityUploads", value);
                } 
            }
        }

        private bool _isClipboardSyncDisabled;
        public bool IsClipboardSyncDisabled
        {
            get => _isClipboardSyncDisabled;
            set 
            { 
                if (_isClipboardSyncDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} ClipboardSync toggle: {value}");
                    _isClipboardSyncDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("ClipboardSync", value);
                } 
            }
        }

        private bool _isDiagToastDisabled;
        public bool IsDiagToastDisabled
        {
            get => _isDiagToastDisabled;
            set 
            { 
                if (_isDiagToastDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} DiagnosticsToast toggle: {value}");
                    _isDiagToastDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("DiagnosticsToast", value);
                } 
            }
        }

        private bool _isOnlineSpeechDisabled;
        public bool IsOnlineSpeechDisabled
        {
            get => _isOnlineSpeechDisabled;
            set 
            { 
                if (_isOnlineSpeechDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} OnlineSpeechPrivacy toggle: {value}");
                    _isOnlineSpeechDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("OnlineSpeechPrivacy", value);
                } 
            }
        }

        private bool _isOneDriveDisabled;
        public bool IsOneDriveDisabled
        {
            get => _isOneDriveDisabled;
            set 
            { 
                if (_isOneDriveDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} OneDrive toggle: {value}");
                    _isOneDriveDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("OneDrive", value);
                } 
            }
        }

        private bool _isActivityFeedDisabled;
        public bool IsActivityFeedDisabled
        {
            get => _isActivityFeedDisabled;
            set 
            { 
                if (_isActivityFeedDisabled != value) 
                { 
                    _logger.LogInfo($"{TAG} ActivityFeed toggle: {value}");
                    _isActivityFeedDisabled = value; 
                    OnPropertyChanged(); 
                    _ = ApplyTweakWithPersistenceAsync("ActivityFeed", value);
                } 
            }
        }

        public PrivacyViewModel(IPrivacyTuningService privacyService, ILoggingService logger)
        {
            _privacyService = privacyService;
            _logger = logger;
            _logger.LogInfo($"{TAG} ViewModel criado");
            // Carregamento instantâneo sem Task.Run desnecessário
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("PrivacyLoadingSettings"), false);
            try
            {
                _logger.LogInfo($"{TAG} Iniciando carregamento das configurações de privacidade...");
                var settings = SettingsService.Instance.Settings;

                // 1. Ler estado REAL do Windows como fonte primária da verdade
                _logger.LogInfo($"{TAG} Lendo estado real do Windows...");
                GlobalProgressService.Instance.UpdateProgress(10, LocalizationService.Instance.GetString("PrivacyReadingTelemetry"));
                var realTelemetry = await _privacyService.GetTweakStateAsync("Telemetry");
                GlobalProgressService.Instance.UpdateProgress(15, LocalizationService.Instance.GetString("PrivacyReadingLocation"));
                var realLocation = await _privacyService.GetTweakStateAsync("Location");
                var realAdvertisingId = await _privacyService.GetTweakStateAsync("AdvertisingID");
                GlobalProgressService.Instance.UpdateProgress(25, LocalizationService.Instance.GetString("PrivacyReadingCortana"));
                var realCortana = await _privacyService.GetTweakStateAsync("Cortana");
                var realCoPilot = await _privacyService.GetTweakStateAsync("CoPilot");
                var realRecall = await _privacyService.GetTweakStateAsync("Recall");
                var realBluetooth = await _privacyService.GetTweakStateAsync("BluetoothAdvertising");
                var realHandwriting = await _privacyService.GetTweakStateAsync("HandwritingDataSharing");
                GlobalProgressService.Instance.UpdateProgress(40, LocalizationService.Instance.GetString("PrivacyReadingInputData"));
                var realTextInput = await _privacyService.GetTweakStateAsync("TextInputDataCollection");
                var realPersonalization = await _privacyService.GetTweakStateAsync("InputPersonalization");
                var realActivityUpload = await _privacyService.GetTweakStateAsync("ActivityUploads");
                GlobalProgressService.Instance.UpdateProgress(55, LocalizationService.Instance.GetString("PrivacyReadingClipboard"));
                var realClipboardSync = await _privacyService.GetTweakStateAsync("ClipboardSync");
                var realDiagToast = await _privacyService.GetTweakStateAsync("DiagnosticsToast");
                var realOnlineSpeech = await _privacyService.GetTweakStateAsync("OnlineSpeechPrivacy");
                GlobalProgressService.Instance.UpdateProgress(70, LocalizationService.Instance.GetString("PrivacyReadingOneDrive"));
                var realOneDrive = await _privacyService.GetTweakStateAsync("OneDrive");
                var realActivityFeed = await _privacyService.GetTweakStateAsync("ActivityFeed");

                GlobalProgressService.Instance.UpdateProgress(80, LocalizationService.Instance.GetString("PrivacySyncingUI"));

                _isTelemetryDisabled = realTelemetry;
                _isLocationDisabled = realLocation;
                _isAdvertisingIdDisabled = realAdvertisingId;
                _isCortanaDisabled = realCortana;
                _isCoPilotDisabled = realCoPilot;
                _isRecallDisabled = realRecall;
                _isBluetoothDisabled = realBluetooth;
                _isHandwritingDisabled = realHandwriting;
                _isTextInputDisabled = realTextInput;
                _isPersonalizationDisabled = realPersonalization;
                _isActivityUploadDisabled = realActivityUpload;
                _isClipboardSyncDisabled = realClipboardSync;
                _isDiagToastDisabled = realDiagToast;
                _isOnlineSpeechDisabled = realOnlineSpeech;
                _isOneDriveDisabled = realOneDrive;
                _isActivityFeedDisabled = realActivityFeed;

                _logger.LogInfo($"{TAG} Estado real carregado: Telemetry={realTelemetry}, Location={realLocation}, AdvertisingID={realAdvertisingId}, Cortana={realCortana}, CoPilot={realCoPilot}, Recall={realRecall}, OneDrive={realOneDrive}, ActivityFeed={realActivityFeed}");

                settings.PrivacyTelemetryDisabled = realTelemetry;
                settings.PrivacyLocationDisabled = realLocation;
                settings.PrivacyAdvertisingIdDisabled = realAdvertisingId;
                settings.PrivacyCortanaDisabled = realCortana;
                settings.PrivacyCoPilotDisabled = realCoPilot;
                settings.PrivacyRecallDisabled = realRecall;
                settings.PrivacyBluetoothDisabled = realBluetooth;
                settings.PrivacyHandwritingDisabled = realHandwriting;
                settings.PrivacyTextInputDisabled = realTextInput;
                settings.PrivacyPersonalizationDisabled = realPersonalization;
                settings.PrivacyActivityUploadDisabled = realActivityUpload;
                settings.PrivacyClipboardSyncDisabled = realClipboardSync;
                settings.PrivacyDiagnosticsToastDisabled = realDiagToast;
                settings.PrivacyOnlineSpeechDisabled = realOnlineSpeech;
                settings.PrivacyOneDriveDisabled = realOneDrive;
                settings.PrivacyActivityFeedDisabled = realActivityFeed;
                SettingsService.Instance.SaveSettings();

                OnPropertyChanged(string.Empty);
                _logger.LogSuccess($"{TAG} Carregamento concluído — {_privacyService.GetType().Name} reflete estado real do Windows");
                GlobalProgressService.Instance.UpdateProgress(100, LocalizationService.Instance.GetString("PrivacyLoaded"));
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("PrivacySettingsLoaded"));
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} Erro ao carregar configurações de privacidade: {ex.Message}", ex);
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("PrivacyErrorLoading"), ex.Message));
            }
        }

        private string GetTagDisplayName(string tag) => tag switch
        {
            "Telemetry" => LocalizationService.Instance.GetString("PrivacyDisplayTelemetry"),
            "Location" => LocalizationService.Instance.GetString("PrivacyDisplayLocation"),
            "AdvertisingID" => LocalizationService.Instance.GetString("PrivacyDisplayAdvertisingID"),
            "Cortana" => LocalizationService.Instance.GetString("PrivacyDisplayCortana"),
            "CoPilot" => LocalizationService.Instance.GetString("PrivacyDisplayCoPilot"),
            "Recall" => LocalizationService.Instance.GetString("PrivacyDisplayRecall"),
            "BluetoothAdvertising" => LocalizationService.Instance.GetString("PrivacyDisplayBluetoothAdvertising"),
            "HandwritingDataSharing" => LocalizationService.Instance.GetString("PrivacyDisplayHandwritingDataSharing"),
            "TextInputDataCollection" => LocalizationService.Instance.GetString("PrivacyDisplayTextInputDataCollection"),
            "InputPersonalization" => LocalizationService.Instance.GetString("PrivacyDisplayInputPersonalization"),
            "ActivityUploads" => LocalizationService.Instance.GetString("PrivacyDisplayActivityUploads"),
            "ClipboardSync" => LocalizationService.Instance.GetString("PrivacyDisplayClipboardSync"),
            "DiagnosticsToast" => LocalizationService.Instance.GetString("PrivacyDisplayDiagnosticsToast"),
            "OnlineSpeechPrivacy" => LocalizationService.Instance.GetString("PrivacyDisplayOnlineSpeechPrivacy"),
            "OneDrive" => LocalizationService.Instance.GetString("PrivacyDisplayOneDrive"),
            "ActivityFeed" => LocalizationService.Instance.GetString("PrivacyDisplayActivityFeed"),
            _ => tag
        };

        private async Task ApplyTweakWithPersistenceAsync(string tag, bool enable)
        {
            var displayName = GetTagDisplayName(tag);
            var opName = string.Format(LocalizationService.Instance.GetString("PrivacyApplyingTweak"), displayName);
            GlobalProgressService.Instance.StartOperation(opName, false);
            try
            {
                _logger.LogInfo($"{TAG} Aplicando tweak '{tag}' com valor '{enable}'");
                
                var success = await _privacyService.ApplyTweakAsync(tag, enable);
                
                if (success)
                {
                    var settings = SettingsService.Instance.Settings;
                    switch (tag)
                    {
                        case "Telemetry":
                            settings.PrivacyTelemetryDisabled = enable;
                            break;
                        case "Location":
                            settings.PrivacyLocationDisabled = enable;
                            break;
                        case "AdvertisingID":
                            settings.PrivacyAdvertisingIdDisabled = enable;
                            break;
                        case "Cortana":
                            settings.PrivacyCortanaDisabled = enable;
                            break;
                        case "CoPilot":
                            settings.PrivacyCoPilotDisabled = enable;
                            break;
                        case "Recall":
                            settings.PrivacyRecallDisabled = enable;
                            break;
                        case "BluetoothAdvertising":
                            settings.PrivacyBluetoothDisabled = enable;
                            break;
                        case "HandwritingDataSharing":
                            settings.PrivacyHandwritingDisabled = enable;
                            break;
                        case "TextInputDataCollection":
                            settings.PrivacyTextInputDisabled = enable;
                            break;
                        case "InputPersonalization":
                            settings.PrivacyPersonalizationDisabled = enable;
                            break;
                        case "ActivityUploads":
                            settings.PrivacyActivityUploadDisabled = enable;
                            break;
                        case "ClipboardSync":
                            settings.PrivacyClipboardSyncDisabled = enable;
                            break;
                        case "DiagnosticsToast":
                            settings.PrivacyDiagnosticsToastDisabled = enable;
                            break;
                        case "OnlineSpeechPrivacy":
                            settings.PrivacyOnlineSpeechDisabled = enable;
                            break;
                        case "OneDrive":
                            settings.PrivacyOneDriveDisabled = enable;
                            break;
                        case "ActivityFeed":
                            settings.PrivacyActivityFeedDisabled = enable;
                            break;
                    }
                    
                    SettingsService.Instance.SaveSettings();
                    _logger.LogSuccess($"{TAG} ✅ Tweak '{tag}' aplicado e persistido com sucesso");
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("PrivacyNotificationTitle"), enable
                        ? string.Format(LocalizationService.Instance.GetString("PrivacyEnabledSuccess"), displayName)
                        : string.Format(LocalizationService.Instance.GetString("PrivacyDisabledSuccess"), displayName));
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("PrivacyTweakApplied"), displayName, enable ? LocalizationService.Instance.GetString("PrivacyEnabled") : LocalizationService.Instance.GetString("PrivacyDisabled")));
                }
                else
                {
                    _logger.LogWarning($"{TAG} ⚠️ Falha ao aplicar tweak '{tag}'");
                    GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("PrivacyNotificationTitle"), string.Format(LocalizationService.Instance.GetString("PrivacyFailedChange"), displayName));
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("PrivacyFailedChangeShort"), displayName));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} Erro ao aplicar tweak '{tag}': {ex.Message}", ex);
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("PrivacyNotificationTitle"), string.Format(LocalizationService.Instance.GetString("PrivacyErrorChanging"), GetTagDisplayName(tag), ex.Message));
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("PrivacyErrorChangingShort"), GetTagDisplayName(tag), ex.Message));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
