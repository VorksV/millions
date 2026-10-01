using System;
using System.Threading;
using VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces;
using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning.Features
{
    public enum FivrPlane
    {
        IaCore    = 0x00,
        Gt        = 0x01,
        Cache     = 0x02,
        Sa        = 0x03,
        AnalogIo  = 0x04,
        DigitalIo = 0x05
    }

    public enum FivrType
    {
        Override     = 0,
        Adaptive     = 1,
        StaticOffset = 2
    }

    public class FivrService
    {
        private const uint MSR_OC_MAILBOX = 0x150;

        private readonly IHardwareBackend _backend;
        private readonly ILoggingService _logger;

        public FivrService(IHardwareBackend backend, ILoggingService logger)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool IsFivrSupported()
        {
            _logger?.LogTrace("[Fivr] Enter IsFivrSupported()");
            if (_backend.GetCpuVendor() != CpuVendor.Intel) return false;
            return _backend.ReadMsr(MSR_OC_MAILBOX, out _);
        }

        private static double GetStepMv(FivrPlane plane)
        {
            return plane <= FivrPlane.Cache ? 2.5 : 5.0;
        }

        public bool SetVoltageOffset(FivrPlane plane, double offsetMv, FivrType type = FivrType.Adaptive)
        {
            _logger?.LogTrace($"[Fivr] Enter SetVoltageOffset(plane={plane}, offsetMv={offsetMv}, type={type})");
            double mvPerStep = GetStepMv(plane);
            int steps = (int)Math.Round(offsetMv / mvPerStep);

            if (steps < -128 || steps > 127)
            {
                _logger.LogWarning($"[FivrService] Offset {offsetMv}mV fora do range (-{128*mvPerStep}~{127*mvPerStep}mV)");
                return false;
            }

            byte offsetByte = (byte)(steps & 0xFF);
            uint command = ((uint)plane & 0x3F) |
                           ((uint)type << 24) |
                           ((uint)offsetByte << 16) |
                           (1u << 31);

            ulong msrValue = command;
            if (!_backend.WriteMsr(MSR_OC_MAILBOX, msrValue))
            {
                _logger.LogDebug($"[FivrService] Falha ao escrever MSR 0x150 para plane {plane} offset {offsetMv}mV");
                return false;
            }

            for (int i = 0; i < 200; i++)
            {
                Thread.SpinWait(50);
                if (_backend.ReadMsr(MSR_OC_MAILBOX, out ulong result))
                {
                    if ((result & (1UL << 31)) == 0)
                    {
                        bool success = (result & (1UL << 4)) == 0;
                        if (success)
                            _logger.LogSuccess($"[FivrService] Plane {plane}: {offsetMv:F1}mV aplicado ({(type == FivrType.Adaptive ? "Adaptive" : type == FivrType.Override ? "Override" : "Static")})");
                        else
                            _logger.LogWarning($"[FivrService] Plane {plane}: MSR retornou erro (bit 4 set)");
                        return success;
                    }
                }
            }

            _logger.LogWarning($"[FivrService] Plane {plane}: timeout aguardando comando completar");
            return false;
        }

        public bool ApplyUndervoltPreset(FivrPlane plane, double offsetMv)
        {
            return SetVoltageOffset(plane, offsetMv, FivrType.Adaptive);
        }

        public bool RemoveUndervolt(FivrPlane plane)
        {
            return SetVoltageOffset(plane, 0, FivrType.Adaptive);
        }
    }
}
