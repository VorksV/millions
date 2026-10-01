using System;

namespace VoltrisOptimizer.Core.Configuration
{
    /// <summary>
    /// Gerencia as Feature Flags para a migração Strangler Fig do Voltris Optimizer.
    /// Permite ativação e desativação dinâmica em tempo real para testes A/B sem restart.
    /// </summary>
    public sealed class VoltrisFeatureFlags
    {
        private static readonly Lazy<VoltrisFeatureFlags> _instance = new(() => new VoltrisFeatureFlags());
        
        public static VoltrisFeatureFlags Instance => _instance.Value;

        // Evento disparado quando qualquer flag for alterada, útil para notificar a UI ou reiniciar coletores
        public event EventHandler<FeatureFlagChangedEventArgs>? FlagChanged;

        private VoltrisFeatureFlags() { }

        // Backing fields voláteis para acesso thread-safe (sem lock na leitura)
        private volatile bool _useUnifiedTelemetry;
        private volatile bool _useUnifiedScheduler;
        private volatile bool _useUnifiedDecisionEngine;
        private volatile bool _useNewRewardEngine;
        private volatile bool _useEvidenceValidator;
        private volatile bool _useAtomicRollback;
        private volatile bool _useHardwareAwareness;
        private volatile bool _useOptimizationCatalog;
        private volatile bool _useEnterpriseScheduler;

        public bool UseEnterpriseScheduler
        {
            get => _useEnterpriseScheduler;
            set 
            {
                if (_useEnterpriseScheduler != value)
                {
                    _useEnterpriseScheduler = value;
                    FlagChanged?.Invoke(this, new FeatureFlagChangedEventArgs(nameof(UseEnterpriseScheduler), value));
                }
            }
        }

        public bool UseUnifiedTelemetry
        {
            get => _useUnifiedTelemetry;
            set
            {
                if (_useUnifiedTelemetry != value)
                {
                    _useUnifiedTelemetry = value;
                    OnFlagChanged(nameof(UseUnifiedTelemetry), value);
                }
            }
        }

        public bool UseUnifiedScheduler
        {
            get => _useUnifiedScheduler;
            set
            {
                if (_useUnifiedScheduler != value)
                {
                    _useUnifiedScheduler = value;
                    OnFlagChanged(nameof(UseUnifiedScheduler), value);
                }
            }
        }

        public bool UseUnifiedDecisionEngine
        {
            get => _useUnifiedDecisionEngine;
            set
            {
                if (_useUnifiedDecisionEngine != value)
                {
                    _useUnifiedDecisionEngine = value;
                    OnFlagChanged(nameof(UseUnifiedDecisionEngine), value);
                }
            }
        }

        public bool UseNewRewardEngine
        {
            get => _useNewRewardEngine;
            set
            {
                if (_useNewRewardEngine != value)
                {
                    _useNewRewardEngine = value;
                    OnFlagChanged(nameof(UseNewRewardEngine), value);
                }
            }
        }

        public bool UseEvidenceValidator
        {
            get => _useEvidenceValidator;
            set
            {
                if (_useEvidenceValidator != value)
                {
                    _useEvidenceValidator = value;
                    OnFlagChanged(nameof(UseEvidenceValidator), value);
                }
            }
        }

        public bool UseAtomicRollback
        {
            get => _useAtomicRollback;
            set
            {
                if (_useAtomicRollback != value)
                {
                    _useAtomicRollback = value;
                    OnFlagChanged(nameof(UseAtomicRollback), value);
                }
            }
        }

        public bool UseHardwareAwareness
        {
            get => _useHardwareAwareness;
            set
            {
                if (_useHardwareAwareness != value)
                {
                    _useHardwareAwareness = value;
                    OnFlagChanged(nameof(UseHardwareAwareness), value);
                }
            }
        }

        public bool UseOptimizationCatalog
        {
            get => _useOptimizationCatalog;
            set
            {
                if (_useOptimizationCatalog != value)
                {
                    _useOptimizationCatalog = value;
                    OnFlagChanged(nameof(UseOptimizationCatalog), value);
                }
            }
        }

        private void OnFlagChanged(string flagName, bool newValue)
        {
            FlagChanged?.Invoke(this, new FeatureFlagChangedEventArgs(flagName, newValue));
        }
    }

    public class FeatureFlagChangedEventArgs : EventArgs
    {
        public string FlagName { get; }
        public bool IsEnabled { get; }

        public FeatureFlagChangedEventArgs(string flagName, bool isEnabled)
        {
            FlagName = flagName;
            IsEnabled = isEnabled;
        }
    }
}
