using System;
using System.Threading;
using VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces;

namespace VoltrisOptimizer.Services.Performance.CpuTuning.Features
{
    public class ProchotService
    {
        private const uint MSR_POWER_CTL = 0x1FC;
        private const int MAX_WRITE_RETRIES = 3;
        private const int RETRY_DELAY_MS = 50;

        private readonly IHardwareBackend _backend;
        private readonly ILoggingService _logger;
        private string _lastOperationContext = "";
        private bool _msrUnsupportedLogged;

        /// <summary>
        /// Indica que o MSR de BD PROCHOT e inacessivel nesta CPU/BIOS.
        /// Nesse caso o recurso e opcional e deve ser ignorado silenciosamente.
        /// </summary>
        public bool IsHardwareSupported { get; private set; } = true;

        public ProchotService(IHardwareBackend backend, ILoggingService logger)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool ReadPowerCtl(out ulong value)
        {
            return _backend.ReadMsr(MSR_POWER_CTL, out value);
        }

        public bool SetBdProchot(bool enabled)
        {
            var operationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.UtcNow;
            _lastOperationContext = $"SetBdProchot_{operationId}";
            
            _logger?.LogTrace($"[ProchotService][{operationId}] Enter SetBdProchot(enabled={enabled})");
            _logger.LogInfo($"[ProchotService][{operationId}] ============================================");
            _logger.LogInfo($"[ProchotService][{operationId}] INICIANDO CONFIGURAÇÃO BD PROCHOT");
            _logger.LogInfo($"[ProchotService][{operationId}] ============================================");
            _logger.LogInfo($"[ProchotService][{operationId}] Timestamp: {startTime:yyyy-MM-dd HH:mm:ss.fff}");
            _logger.LogInfo($"[ProchotService][{operationId}] Thread: {Thread.CurrentThread.ManagedThreadId}");
            _logger.LogInfo($"[ProchotService][{operationId}] Ação: {(enabled ? "Habilitar" : "Desabilitar")} BD PROCHOT");

            // 1. LEITURA DO VALOR ATUAL
            _logger.LogInfo($"[ProchotService][{operationId}] Lendo MSR 0x{MSR_POWER_CTL:X} (IA32_POWER_CTL)...");
            if (!_backend.ReadMsr(MSR_POWER_CTL, out ulong current))
            {
                IsHardwareSupported = false;
                if (!_msrUnsupportedLogged)
                {
                    _msrUnsupportedLogged = true;
                    _logger.LogInfo($"[ProchotService][{operationId}] BD PROCHOT indisponivel nesta CPU/BIOS (MSR 0x{MSR_POWER_CTL:X} inacessivel). Recurso opcional sera ignorado.");
                }
                return false;
            }
            IsHardwareSupported = true;
            
            bool currentEnabled = (current & (1UL << 0)) != 0;
            _logger.LogInfo($"[ProchotService][{operationId}] BD PROCHOT atual: {(currentEnabled ? "Habilitado" : "Desabilitado")} (Raw: 0x{current:X})");

            // 2. VERIFICAÇÃO SE JÁ ESTÁ NO ESTADO DESEJADO
            if (currentEnabled == enabled)
            {
                _logger.LogSuccess($"[ProchotService][{operationId}] ✅ BD PROCHOT já está no estado desejado: {(enabled ? "Habilitado" : "Desabilitado")}");
                return true;
            }

            // 3. CONSTRUÇÃO DO NOVO VALOR
            ulong newVal = enabled ? (current | (1UL << 0)) : (current & ~(1UL << 0));
            _logger.LogInfo($"[ProchotService][{operationId}] Valor calculado para escrita: 0x{newVal:X}");
            _logger.LogInfo($"[ProchotService][{operationId}]   Bit 0 (BD PROCHOT): {(enabled ? "1" : "0")}");
            _logger.LogInfo($"[ProchotService][{operationId}]   Outros bits: 0x{current & ~1UL:X} (preservados)");

            // 4. ESCRITA COM RETRY
            _logger.LogInfo($"[ProchotService][{operationId}] ============================================");
            _logger.LogInfo($"[ProchotService][{operationId}] INICIANDO ESCRITA COM RETRY");
            _logger.LogInfo($"[ProchotService][{operationId}] ============================================");
            
            for (int i = 0; i < MAX_WRITE_RETRIES; i++)
            {
                _logger.LogInfo($"[ProchotService][{operationId}] Tentativa {i + 1}/{MAX_WRITE_RETRIES}...");
                _logger.LogInfo($"[ProchotService][{operationId}] Escrevendo MSR 0x{MSR_POWER_CTL:X} com valor 0x{newVal:X}");
                
                if (_backend.WriteMsr(MSR_POWER_CTL, newVal))
                {
                    _logger.LogSuccess($"[ProchotService][{operationId}] ✅ WriteMsr executado com sucesso");
                    
                    // 5. VALIDAÇÃO DA ESCRITA (DOUBLE CHECK)
                    _logger.LogInfo($"[ProchotService][{operationId}] Validando escrita...");
                    if (_backend.ReadMsr(MSR_POWER_CTL, out ulong verify))
                    {
                        bool verifyEnabled = (verify & (1UL << 0)) != 0;
                        _logger.LogInfo($"[ProchotService][{operationId}] BD PROCHOT após escrita: {(verifyEnabled ? "Habilitado" : "Desabilitado")} (Raw: 0x{verify:X})");
                        
                        if (verify == newVal)
                        {
                            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                            _logger.LogSuccess($"[ProchotService][{operationId}] ============================================");
                            _logger.LogSuccess($"[ProchotService][{operationId}] ✅ BD PROCHOT CONFIGURADO COM SUCESSO");
                            _logger.LogSuccess($"[ProchotService][{operationId}] ============================================");
                            _logger.LogSuccess($"[ProchotService][{operationId}] Estado: {(enabled ? "Habilitado" : "Desabilitado")} (Confirmado)");
                            _logger.LogSuccess($"[ProchotService][{operationId}] Anterior: {(currentEnabled ? "Habilitado" : "Desabilitado")}");
                            _logger.LogSuccess($"[ProchotService][{operationId}] Tentativas: {i + 1}/{MAX_WRITE_RETRIES}");
                            _logger.LogSuccess($"[ProchotService][{operationId}] Tempo: {elapsedMs:F0}ms");
                            _logger.LogSuccess($"[ProchotService][{operationId}] ============================================");
                            return true;
                        }
                        else
                        {
                            _logger.LogWarning($"[ProchotService][{operationId}] ⚠️ Escrita não persistiu corretamente");
                            _logger.LogWarning($"[ProchotService][{operationId}] Esperado: 0x{newVal:X}");
                            _logger.LogWarning($"[ProchotService][{operationId}] Confirmado: 0x{verify:X}");
                            _logger.LogWarning($"[ProchotService][{operationId}] Motivo: BIOS bloqueou escrita ou hardware forçou valor diferente");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"[ProchotService][{operationId}] ⚠️ Falha na validação (não foi possível ler MSR após escrita)");
                    }
                }
                else
                {
                    _logger.LogWarning($"[ProchotService][{operationId}] ⚠️ WriteMsr falhou na tentativa {i + 1}");
                    _logger.LogWarning($"[ProchotService][{operationId}] Motivo: Driver rejeitou escrita ou CPU bloqueou o MSR");
                }
                
                if (i < MAX_WRITE_RETRIES - 1)
                {
                    _logger.LogInfo($"[ProchotService][{operationId}] Aguardando {RETRY_DELAY_MS}ms antes da próxima tentativa...");
                    System.Threading.Thread.Sleep(RETRY_DELAY_MS);
                }
            }

            // 6. FALHA APÓS TODAS AS TENTATIVAS
            _logger.LogError($"[ProchotService][{operationId}] ❌ FALHA: Não foi possível configurar BD PROCHOT após {MAX_WRITE_RETRIES} tentativas");
            _logger.LogError($"[ProchotService][{operationId}] Motivo: MSR 0x{MSR_POWER_CTL:X} não aceitou o valor solicitado");
            _logger.LogError($"[ProchotService][{operationId}] Valor solicitado: 0x{newVal:X}");
            _logger.LogError($"[ProchotService][{operationId}] Ação: Verifique se a CPU suporta BD PROCHOT e se a BIOS não bloqueou o MSR");
            return false;
        }

        public bool SetProchotOffset(int offset)
        {
            if (offset < 0 || offset > 31)
            {
                _logger.LogWarning($"[ProchotService] Offset PROCHOT {offset} inválido (0-31)");
                return false;
            }

            if (!_backend.ReadMsr(MSR_POWER_CTL, out ulong current))
            {
                _logger.LogDebug("[ProchotService] Falha ao ler MSR 0x1FC");
                return false;
            }

            ulong newVal = (current & ~(0x1FUL << 24)) | ((ulong)(uint)offset << 24);

            if (!_backend.WriteMsr(MSR_POWER_CTL, newVal))
            {
                _logger.LogDebug("[ProchotService] Falha ao escrever offset PROCHOT");
                return false;
            }

            _logger.LogSuccess($"[ProchotService] PROCHOT offset = {offset}°C");
            return true;
        }

        public bool DisableBdProchot()
        {
            return SetBdProchot(false);
        }

        public bool EnableBdProchot()
        {
            return SetBdProchot(true);
        }

        public async System.Threading.Tasks.Task<bool> DisableBdProchotAsync()
        {
            return await System.Threading.Tasks.Task.Run(() => SetBdProchot(false));
        }

        public async System.Threading.Tasks.Task<bool> EnableBdProchotAsync()
        {
            return await System.Threading.Tasks.Task.Run(() => SetBdProchot(true));
        }

        public bool ClearProchotLock()
        {
            if (!_backend.ReadMsr(MSR_POWER_CTL, out ulong current))
                return false;

            ulong newVal = current & ~(1UL << 1);

            if (!_backend.WriteMsr(MSR_POWER_CTL, newVal))
                return false;

            _logger.LogSuccess("[ProchotService] PROCHOT lock cleared");
            return true;
        }

        public (bool bdProchotDisabled, int prochotOffset, bool isLocked) GetStatus()
        {
            if (!_backend.ReadMsr(MSR_POWER_CTL, out ulong val))
                return (false, 0, false);

            return (
                (val & (1UL << 0)) == 0,
                (int)((val >> 24) & 0x1F),
                (val & (1UL << 1)) != 0
            );
        }
    }
}
