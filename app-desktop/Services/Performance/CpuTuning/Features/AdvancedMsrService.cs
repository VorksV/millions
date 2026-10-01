using System;
using VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces;
using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning.Features
{
    public class AdvancedMsrService
    {
        private const uint MSR_PMG_CST_CONFIG_CONTROL = 0xE2;
        private const uint MSR_TURBO_RATIO_LIMIT       = 0x1AD;
        private const uint MSR_TURBO_RATIO_LIMIT1      = 0x1AE;
        private const uint MSR_PL4_LIMIT               = 0x601;
        private const uint MSR_PP0_POWER_LIMIT         = 0x638;
        private const uint MSR_PP1_POWER_LIMIT         = 0x640;
        private const uint MSR_CONFIG_TDP_NOMINAL      = 0x648;
        private const uint MSR_CONFIG_TDP_LEVEL1       = 0x649;
        private const uint MSR_CONFIG_TDP_LEVEL2       = 0x64A;
        private const uint MSR_CONFIG_TDP_CONTROL      = 0x64B;
        private const uint MSR_TURBO_ACTIVATION_RATIO  = 0x64C;

        private readonly IHardwareBackend _backend;
        private readonly ILoggingService _logger;

        public AdvancedMsrService(IHardwareBackend backend, ILoggingService logger)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool SetCStateLimit(int maxCState)
        {
            if (maxCState < 0 || maxCState > 10)
            {
                _logger.LogWarning($"[AdvMsr] C-State {maxCState} inválido (0-10)");
                return false;
            }

            if (!_backend.ReadMsr(MSR_PMG_CST_CONFIG_CONTROL, out ulong current))
            {
                _logger.LogWarning("[AdvMsr] Falha ao ler MSR 0xE2");
                return false;
            }

            ulong newVal = (current & ~0x7UL) | (ulong)(uint)(maxCState & 0x7);

            if (!_backend.WriteMsr(MSR_PMG_CST_CONFIG_CONTROL, newVal))
            {
                _logger.LogWarning("[AdvMsr] Falha ao escrever MSR 0xE2");
                return false;
            }

            _logger.LogSuccess($"[AdvMsr] C-State limit = C{maxCState}");
            return true;
        }

        public bool SetTurboRatioLimit(int ratio2, int ratio3, int ratio4,
            int ratio1 = -1, int ratio5 = -1, int ratio6 = -1, int ratio7 = -1, int ratio8 = -1)
        {
            if (!_backend.ReadMsr(MSR_TURBO_RATIO_LIMIT, out ulong current))
            {
                _logger.LogWarning("[AdvMsr] Falha ao ler MSR 0x1AD");
                return false;
            }

            ulong newVal = current;
            newVal = (newVal & ~(0xFFUL << 8)) | ((ulong)(byte)(ratio2 & 0xFF) << 8);
            newVal = (newVal & ~(0xFFUL << 16)) | ((ulong)(byte)(ratio3 & 0xFF) << 16);
            newVal = (newVal & ~(0xFFUL << 24)) | ((ulong)(byte)(ratio4 & 0xFF) << 24);

            if (ratio1 >= 0)
                newVal = (newVal & ~0xFFUL) | (ulong)(byte)(ratio1 & 0xFF);

            if (!_backend.WriteMsr(MSR_TURBO_RATIO_LIMIT, newVal))
            {
                _logger.LogWarning("[AdvMsr] Falha ao escrever MSR 0x1AD");
                return false;
            }

            if (ratio5 >= 0 || ratio6 >= 0 || ratio7 >= 0 || ratio8 >= 0)
            {
                if (!_backend.ReadMsr(MSR_TURBO_RATIO_LIMIT1, out ulong current1))
                    return false;

                ulong newVal1 = current1;
                if (ratio5 >= 0) newVal1 = (newVal1 & ~0xFFUL) | (ulong)(byte)(ratio5 & 0xFF);
                if (ratio6 >= 0) newVal1 = (newVal1 & ~(0xFFUL << 8)) | ((ulong)(byte)(ratio6 & 0xFF) << 8);
                if (ratio7 >= 0) newVal1 = (newVal1 & ~(0xFFUL << 16)) | ((ulong)(byte)(ratio7 & 0xFF) << 16);
                if (ratio8 >= 0) newVal1 = (newVal1 & ~(0xFFUL << 24)) | ((ulong)(byte)(ratio8 & 0xFF) << 24);

                _backend.WriteMsr(MSR_TURBO_RATIO_LIMIT1, newVal1);
            }

            _logger.LogSuccess($"[AdvMsr] Turbo ratio configurado");
            return true;
        }

        public bool SetPl4Limit(double watts)
        {
            if (watts < 0 || watts > 16384)
            {
                _logger.LogWarning($"[AdvMsr] PL4 {watts}W inválido (0-16384)");
                return false;
            }

            if (!_backend.ReadMsr(MSR_PL4_LIMIT, out ulong current))
            {
                _logger.LogWarning("[AdvMsr] Falha ao ler MSR 0x601");
                return false;
            }

            uint pl4Value = (uint)(watts * 2);
            ulong newVal = (current & ~0x7FFFUL) | (pl4Value & 0x7FFF);

            if (!_backend.WriteMsr(MSR_PL4_LIMIT, newVal))
            {
                _logger.LogWarning("[AdvMsr] Falha ao escrever PL4");
                return false;
            }

            _logger.LogSuccess($"[AdvMsr] PL4 = {watts:F0}W");
            return true;
        }

        public bool SetPp0PowerLimit(double watts)
        {
            if (!_backend.ReadMsr(MSR_PP0_POWER_LIMIT, out ulong current))
            {
                _logger.LogWarning("[AdvMsr] Falha ao ler MSR 0x638");
                return false;
            }

            uint pp0Value = (uint)(watts / 0.125);
            ulong newVal = (current & ~0x7FFFUL) | (pp0Value & 0x7FFF);

            if (!_backend.WriteMsr(MSR_PP0_POWER_LIMIT, newVal))
            {
                _logger.LogWarning("[AdvMsr] Falha ao escrever PP0");
                return false;
            }

            _logger.LogSuccess($"[AdvMsr] PP0 limit = {watts:F0}W");
            return true;
        }

        public bool SetPp1PowerLimit(double watts)
        {
            if (!_backend.ReadMsr(MSR_PP1_POWER_LIMIT, out ulong current))
            {
                _logger.LogWarning("[AdvMsr] Falha ao ler MSR 0x640");
                return false;
            }

            uint pp1Value = (uint)(watts / 0.125);
            ulong newVal = (current & ~0x7FFFUL) | (pp1Value & 0x7FFF);

            if (!_backend.WriteMsr(MSR_PP1_POWER_LIMIT, newVal))
            {
                _logger.LogWarning("[AdvMsr] Falha ao escrever PP1");
                return false;
            }

            _logger.LogSuccess($"[AdvMsr] PP1 limit = {watts:F0}W");
            return true;
        }

        public bool SetConfigTdpLevel(int level)
        {
            if (level < 0 || level > 2)
            {
                _logger.LogWarning($"[AdvMsr] Config TDP level {level} inválido (0-2)");
                return false;
            }

            ulong value = (ulong)(uint)(level & 0x3);
            if (!_backend.WriteMsr(MSR_CONFIG_TDP_CONTROL, value))
            {
                _logger.LogWarning("[AdvMsr] Falha ao escrever Config TDP Control");
                return false;
            }

            _logger.LogSuccess($"[AdvMsr] Config TDP level = {level}");
            return true;
        }
    }
}
