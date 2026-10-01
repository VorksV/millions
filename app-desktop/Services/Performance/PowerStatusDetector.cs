using System;
using System.Management;
using System.Runtime.InteropServices; // Adicionado
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Detecta status de energia (AC/DC) de forma confiável usando WMI
    /// CORREÇÃO: RISCO #5 - Detecção AC/DC não confiável
    /// </summary>
    public class PowerStatusDetector
    {
        private readonly ILoggingService _logger;
        private PowerStatus _lastKnownStatus = PowerStatus.Unknown;
        private int _consecutiveReadings = 0;
        private const int HYSTERESIS_THRESHOLD = 2; // Exigir 2 leituras consistentes

        public PowerStatusDetector(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Detecta se está conectado à energia AC (com hysteresis)
        /// </summary>
        public bool IsOnACPower()
        {
            var currentStatus = DetectPowerStatusInternal();

            // Implementar hysteresis: exigir leituras consistentes
            if (currentStatus == _lastKnownStatus)
            {
                _consecutiveReadings++;
            }
            else
            {
                _consecutiveReadings = 1;
                _lastKnownStatus = currentStatus;
            }

            // Se ainda não temos leituras consistentes, usar último estado conhecido
            if (_consecutiveReadings < HYSTERESIS_THRESHOLD)
            {
                _logger.LogDebug($"[PowerStatus] Aguardando leitura consistente ({_consecutiveReadings}/{HYSTERESIS_THRESHOLD})");
                
                // Fallback conservador: assumir bateria se incerto
                if (_lastKnownStatus == PowerStatus.Unknown)
                {
                    _logger.LogWarning("[PowerStatus] Status desconhecido - assumindo BATERIA por segurança");
                    return false;
                }
            }

            bool isAC = _lastKnownStatus == PowerStatus.AC;
            _logger.LogDebug($"[PowerStatus] Status: {(isAC ? "AC (Plugado)" : "DC (Bateria)")}");
            return isAC;
        }

        /// <summary>
        /// Detecta mudança de AC para DC ou vice-versa
        /// </summary>
        public bool HasPowerStatusChanged(out bool isNowOnAC)
        {
            var previousStatus = _lastKnownStatus;
            isNowOnAC = IsOnACPower();
            var currentStatus = _lastKnownStatus;

            bool changed = previousStatus != PowerStatus.Unknown && 
                          currentStatus != PowerStatus.Unknown && 
                          previousStatus != currentStatus &&
                          _consecutiveReadings >= HYSTERESIS_THRESHOLD;

            if (changed)
            {
                _logger.LogInfo($"[PowerStatus] Mudança detectada: {previousStatus} → {currentStatus}");
            }

            return changed;
        }

        private PowerStatus DetectPowerStatusInternal()
        {
            try
            {
                // PERFORMANCE ENTERPRISE: GetSystemPowerStatus é ~50x mais rápido que WMI
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                {
                    // ACLineStatus: 0=Offline, 1=Online, 255=Unknown
                    if (status.ACLineStatus == 1) return PowerStatus.AC;
                    if (status.ACLineStatus == 0) return PowerStatus.DC;
                }

                _logger.LogWarning("[PowerStatus] Não foi possível determinar status de energia via Win32 API.");
                return PowerStatus.Unknown;
            }
            catch (Exception ex)
            {
                _logger.LogError("[PowerStatus] Erro ao detectar status de energia", ex);
                return PowerStatus.Unknown;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }
    }

    public enum PowerStatus
    {
        Unknown,
        AC,  // Plugado na tomada
        DC   // Bateria
    }
}
