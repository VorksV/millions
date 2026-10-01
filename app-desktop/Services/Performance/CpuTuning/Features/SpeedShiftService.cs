using System;
using System.Threading;
using VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces;
using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning.Features
{
    public class SpeedShiftService
    {
        private const uint MSR_PM_ENABLER = 0x770;
        private const uint MSR_HWP_REQUEST = 0x774;
        private const uint MSR_HWP_CAPABILITIES = 0x771;
        private const uint MSR_ENERGY_PERF_BIAS = 0x1B0;

        private const int MAX_WRITE_RETRIES = 3;
        private const int RETRY_DELAY_MS = 10;

        private readonly IHardwareBackend _backend;
        private readonly ILoggingService _logger;
        private string _lastOperationContext = "";

        public SpeedShiftService(IHardwareBackend backend, ILoggingService logger)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool IsHwpSupported()
        {
            _logger?.LogTrace("[SpeedShift] Enter IsHwpSupported()");
            if (_backend.GetCpuVendor() != CpuVendor.Intel)
            {
                _logger?.LogDebug("[SpeedShift] CPU vendor not Intel; HWP not supported.");
                return false;
            }
            if (!_backend.ReadMsr(MSR_HWP_CAPABILITIES, out ulong caps))
            {
                _logger?.LogDebug("[SpeedShift] Falha ao ler MSR_HWP_CAPABILITIES (0x771); HWP not supported.");
                return false;
            }
            bool supported = caps != 0;
            _logger?.LogInfo($"[SpeedShift] HWP capabilities MSR value 0x{caps:X}; Supported={supported}");
            return supported;
        }

        public bool IsHwpEnabled()
        {
            _logger?.LogTrace("[SpeedShift] Enter IsHwpEnabled()");
            if (!_backend.ReadMsr(MSR_PM_ENABLER, out ulong current))
                return false;
            return (current & 1UL) != 0;
        }

        /// <summary>
        /// Verifica se o MSR 0x774 (HWP Request) pode ser escrito.
        /// Realiza um teste de escrita com o valor atual para detectar bloqueios de BIOS/driver.
        /// </summary>
        public bool IsHwpRequestMsrWritable()
        {
            var operationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _logger?.LogTrace($"[SpeedShift][{operationId}] Enter IsHwpRequestMsrWritable()");

            // Tentar ler MSR 0x774 primeiro
            if (!_backend.ReadMsr(MSR_HWP_REQUEST, out ulong currentValue))
            {
                _logger.LogWarning($"[SpeedShift][{operationId}] MSR 0x{MSR_HWP_REQUEST:X} não pode ser lido - provável bloqueio BIOS/driver");
                return false;
            }

            _logger.LogInfo($"[SpeedShift][{operationId}] MSR 0x{MSR_HWP_REQUEST:X} valor atual: 0x{currentValue:X}");

            // Tentar escrever o valor atual (teste de escrita não-destrutivo)
            _logger.LogInfo($"[SpeedShift][{operationId}] Testando writability do MSR 0x{MSR_HWP_REQUEST:X}...");
            if (!_backend.WriteMsr(MSR_HWP_REQUEST, currentValue))
            {
                _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ MSR 0x{MSR_HWP_REQUEST:X} bloqueado para escrita");
                _logger.LogWarning($"[SpeedShift][{operationId}] Motivo: Driver rejeitou escrita ou CPU bloqueou o MSR");
                _logger.LogWarning($"[SpeedShift][{operationId}] Possíveis causas:");
                _logger.LogWarning($"[SpeedShift][{operationId}]   - BIOS bloqueou o MSR 0x{MSR_HWP_REQUEST:X}");
                _logger.LogWarning($"[SpeedShift][{operationId}]   - HWP não está habilitado (MSR 0x{MSR_PM_ENABLER:X})");
                _logger.LogWarning($"[SpeedShift][{operationId}]   - Driver não tem permissão para escrever neste MSR");
                _logger.LogWarning($"[SpeedShift][{operationId}] Impacto: Speed Shift HWP Request não pode ser configurado via MSR");
                _logger.LogWarning($"[SpeedShift][{operationId}] Solução: Use EPP via Windows Power API como fallback");
                return false;
            }

            // Verificar se a escrita persistiu
            if (_backend.ReadMsr(MSR_HWP_REQUEST, out ulong verifyValue) && verifyValue == currentValue)
            {
                _logger.LogSuccess($"[SpeedShift][{operationId}] ✅ MSR 0x{MSR_HWP_REQUEST:X} está writable");
                return true;
            }

            _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ Escrita de teste não persistiu - MSR pode estar bloqueado");
            return false;
        }

        public bool EnableHwp()
        {
            _logger?.LogTrace("[SpeedShift] Enter EnableHwp()");
            return SetHwpEnabled(true);
        }

        public bool DisableHwp()
        {
            _logger?.LogTrace("[SpeedShift] Enter DisableHwp()");
            return SetHwpEnabled(false);
        }

        public bool SetHwpEnabled(bool enabled)
        {
            _logger?.LogTrace($"[SpeedShift] Enter SetHwpEnabled({enabled})");
            if (!_backend.ReadMsr(MSR_PM_ENABLER, out ulong current))
            {
                _logger.LogWarning("[SpeedShift] Falha ao ler MSR 0x770");
                return false;
            }

            ulong newVal = enabled ? (current | 1UL) : (current & ~1UL);

            if (newVal == current)
            {
                _logger.LogInfo($"[SpeedShift] HWP já está {(enabled ? "ativado" : "desativado")}");
                return true;
            }

            for (int i = 0; i < MAX_WRITE_RETRIES; i++)
            {
                _logger?.LogDebug($"[SpeedShift] Attempt {i+1}/{MAX_WRITE_RETRIES} writing MSR 0x770 with value 0x{newVal:X}");
                if (_backend.WriteMsr(MSR_PM_ENABLER, newVal))
                {
                    _logger.LogSuccess($"[SpeedShift] HWP {(enabled ? "ativado" : "desativado")}");
                    return true;
                }
                Thread.Sleep(RETRY_DELAY_MS);
            }

            _logger.LogWarning("[SpeedShift] Falha ao escrever MSR 0x770 após 3 tentativas");
            return false;
        }

        public bool SetHwpRequest(int maxRatio, int minRatio, int epp)
        {
            var operationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.UtcNow;
            _lastOperationContext = $"SetHwpRequest_{operationId}";
            
            _logger?.LogTrace($"[SpeedShift][{operationId}] Enter SetHwpRequest(maxRatio={maxRatio}, minRatio={minRatio}, epp={epp})");
            _logger.LogInfo($"[SpeedShift][{operationId}] ============================================");
            _logger.LogInfo($"[SpeedShift][{operationId}] INICIANDO CONFIGURAÇÃO HWP REQUEST");
            _logger.LogInfo($"[SpeedShift][{operationId}] ============================================");
            _logger.LogInfo($"[SpeedShift][{operationId}] Timestamp: {startTime:yyyy-MM-dd HH:mm:ss.fff}");
            _logger.LogInfo($"[SpeedShift][{operationId}] Thread: {Thread.CurrentThread.ManagedThreadId}");
            _logger.LogInfo($"[SpeedShift][{operationId}] Parâmetros: Max={maxRatio}x, Min={minRatio}x, EPP={epp}");

            // 1. VALIDAÇÃO DE PARÂMETROS
            if (maxRatio < 0 || maxRatio > 255 || minRatio < 0 || minRatio > 255 || epp < 0 || epp > 255)
            {
                _logger.LogError($"[SpeedShift][{operationId}] ❌ FALHA: Parâmetros inválidos");
                _logger.LogError($"[SpeedShift][{operationId}] MaxRatio deve ser 0-255 (recebido: {maxRatio})");
                _logger.LogError($"[SpeedShift][{operationId}] MinRatio deve ser 0-255 (recebido: {minRatio})");
                _logger.LogError($"[SpeedShift][{operationId}] EPP deve ser 0-255 (recebido: {epp})");
                return false;
            }
            _logger.LogSuccess($"[SpeedShift][{operationId}] ✅ Parâmetros validados");

            // 2. VERIFICAÇÃO DE SUPORTE HWP
            _logger.LogInfo($"[SpeedShift][{operationId}] Verificando suporte HWP...");
            if (!IsHwpSupported())
            {
                _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ HWP não suportado nesta CPU");
                _logger.LogWarning($"[SpeedShift][{operationId}] Motivo: CPU não tem Intel Speed Shift (HWP) ou não é Intel");
                _logger.LogWarning($"[SpeedShift][{operationId}] Requisito: Intel Skylake (6th gen) ou superior");
                return false;
            }
            _logger.LogSuccess($"[SpeedShift][{operationId}] ✅ HWP suportado");

            // 3. VERIFICAÇÃO DE HWP ENABLED
            _logger.LogInfo($"[SpeedShift][{operationId}] Verificando se HWP está habilitado...");
            if (!IsHwpEnabled())
            {
                _logger.LogError($"[SpeedShift][{operationId}] ❌ FALHA: HWP está desabilitado");
                _logger.LogError($"[SpeedShift][{operationId}] Motivo: MSR 0x{MSR_PM_ENABLER:X} (IA32_HWP_ENABLE) não está setado");
                _logger.LogError($"[SpeedShift][{operationId}] Impacto: Não é possível configurar HWP_REQUEST (MSR 0x{MSR_HWP_REQUEST:X})");
                _logger.LogError($"[SpeedShift][{operationId}] Ação: Chame EnableHwp() primeiro para habilitar Speed Shift");
                return false;
            }
            _logger.LogSuccess($"[SpeedShift][{operationId}] ✅ HWP está habilitado");

            // 3.5. VERIFICAÇÃO DE WRITABILITY DO MSR 0x774
            _logger.LogInfo($"[SpeedShift][{operationId}] Verificando se MSR 0x{MSR_HWP_REQUEST:X} pode ser escrito...");
            if (!IsHwpRequestMsrWritable())
            {
                _logger.LogError($"[SpeedShift][{operationId}] ❌ FALHA: MSR 0x{MSR_HWP_REQUEST:X} não é writable");
                _logger.LogError($"[SpeedShift][{operationId}] Motivo: BIOS, driver ou CPU bloqueou escrita neste MSR");
                _logger.LogError($"[SpeedShift][{operationId}] Impacto: HWP Request não pode ser configurado via MSR");
                _logger.LogError($"[SpeedShift][{operationId}] Ação: Configure EPP via Windows Power API como alternativa");
                _logger.LogError($"[SpeedShift][{operationId}] Recomendação: Verifique configurações da BIOS (HWP, MSR locks)");
                return false;
            }
            _logger.LogSuccess($"[SpeedShift][{operationId}] ✅ MSR 0x{MSR_HWP_REQUEST:X} está writable");

            // 4. LEITURA DAS CAPABILITIES (para validação de range)
            _logger.LogInfo($"[SpeedShift][{operationId}] Lendo HWP Capabilities (MSR 0x{MSR_HWP_CAPABILITIES:X})...");
            if (_backend.ReadMsr(MSR_HWP_CAPABILITIES, out ulong caps))
            {
                int highestPerf = (int)((caps >> 0) & 0xFF);
                int guaranteedPerf = (int)((caps >> 8) & 0xFF);
                int mostEfficientPerf = (int)((caps >> 16) & 0xFF);
                int lowestPerf = (int)((caps >> 24) & 0xFF);
                _logger.LogInfo($"[SpeedShift][{operationId}] HWP Capabilities:");
                _logger.LogInfo($"[SpeedShift][{operationId}]   Highest Performance: {highestPerf}x");
                _logger.LogInfo($"[SpeedShift][{operationId}]   Guaranteed Performance: {guaranteedPerf}x");
                _logger.LogInfo($"[SpeedShift][{operationId}]   Most Efficient Performance: {mostEfficientPerf}x");
                _logger.LogInfo($"[SpeedShift][{operationId}]   Lowest Performance: {lowestPerf}x");
                
                // Validar se os parâmetros estão dentro das capabilities
                if (maxRatio > highestPerf)
                {
                    _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ MaxRatio ({maxRatio}x) excede Highest Performance ({highestPerf}x)");
                    _logger.LogWarning($"[SpeedShift][{operationId}] Ação: Clamping para {highestPerf}x");
                    maxRatio = highestPerf;
                }
                if (minRatio < lowestPerf)
                {
                    _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ MinRatio ({minRatio}x) abaixo de Lowest Performance ({lowestPerf}x)");
                    _logger.LogWarning($"[SpeedShift][{operationId}] Ação: Clamping para {lowestPerf}x");
                    minRatio = lowestPerf;
                }
            }
            else
            {
                _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ Falha ao ler HWP Capabilities");
                _logger.LogWarning($"[SpeedShift][{operationId}] Motivo: MSR 0x{MSR_HWP_CAPABILITIES:X} não pode ser lido");
                _logger.LogWarning($"[SpeedShift][{operationId}] Impacto: Sem validação de range, usando valores solicitados");
            }

            // 5. CONSTRUÇÃO DO VALOR HWP REQUEST
            ulong value = ((ulong)(uint)maxRatio << 56) |
                          ((ulong)(uint)minRatio << 40) |
                          ((ulong)(uint)epp << 24) |
                          (1UL << 16) |
                          (1UL << 8);
            
            _logger.LogInfo($"[SpeedShift][{operationId}] Valor HWP Request calculado: 0x{value:X}");
            _logger.LogInfo($"[SpeedShift][{operationId}]   Max Ratio (bits 56-63): {maxRatio}x");
            _logger.LogInfo($"[SpeedShift][{operationId}]   Min Ratio (bits 40-47): {minRatio}x");
            _logger.LogInfo($"[SpeedShift][{operationId}]   EPP (bits 24-31): {epp}");
            _logger.LogInfo($"[SpeedShift][{operationId}]   Desired Performance Request (bits 16-23): Habilitado");
            _logger.LogInfo($"[SpeedShift][{operationId}]   Maximum Performance Request (bits 8-15): Habilitado");

            // 6. LEITURA DO VALOR ATUAL (para rollback)
            _logger.LogInfo($"[SpeedShift][{operationId}] Lendo HWP Request atual (MSR 0x{MSR_HWP_REQUEST:X})...");
            ulong originalValue = 0;
            bool hasOriginal = _backend.ReadMsr(MSR_HWP_REQUEST, out originalValue);
            if (hasOriginal)
            {
                int origMax = (int)((originalValue >> 56) & 0xFF);
                int origMin = (int)((originalValue >> 40) & 0xFF);
                int origEpp = (int)((originalValue >> 24) & 0xFF);
                _logger.LogInfo($"[SpeedShift][{operationId}] HWP Request atual: Max={origMax}x, Min={origMin}x, EPP={origEpp} (0x{originalValue:X})");
            }
            else
            {
                _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ Falha ao ler HWP Request atual");
            }

            // 7. ESCRITA COM RETRY
            _logger.LogInfo($"[SpeedShift][{operationId}] ============================================");
            _logger.LogInfo($"[SpeedShift][{operationId}] INICIANDO ESCRITA COM RETRY");
            _logger.LogInfo($"[SpeedShift][{operationId}] ============================================");
            
            for (int i = 0; i < MAX_WRITE_RETRIES; i++)
            {
                _logger.LogInfo($"[SpeedShift][{operationId}] Tentativa {i + 1}/{MAX_WRITE_RETRIES}...");
                _logger.LogInfo($"[SpeedShift][{operationId}] Escrevendo MSR 0x{MSR_HWP_REQUEST:X} com valor 0x{value:X}");
                
                if (_backend.WriteMsr(MSR_HWP_REQUEST, value))
                {
                    _logger.LogSuccess($"[SpeedShift][{operationId}] ✅ WriteMsr executado com sucesso");
                    
                    // 8. VALIDAÇÃO DA ESCRITA (DOUBLE CHECK)
                    _logger.LogInfo($"[SpeedShift][{operationId}] Validando escrita...");
                    if (_backend.ReadMsr(MSR_HWP_REQUEST, out ulong verifyValue))
                    {
                        int verifyMax = (int)((verifyValue >> 56) & 0xFF);
                        int verifyMin = (int)((verifyValue >> 40) & 0xFF);
                        int verifyEpp = (int)((verifyValue >> 24) & 0xFF);
                        _logger.LogInfo($"[SpeedShift][{operationId}] HWP Request após escrita: Max={verifyMax}x, Min={verifyMin}x, EPP={verifyEpp} (0x{verifyValue:X})");
                        
                        if (verifyMax == maxRatio && verifyMin == minRatio && verifyEpp == epp)
                        {
                            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                            _logger.LogSuccess($"[SpeedShift][{operationId}] ============================================");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] ✅ HWP REQUEST CONFIGURADO COM SUCESSO");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] ============================================");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] Max Ratio: {maxRatio}x (Confirmado)");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] Min Ratio: {minRatio}x (Confirmado)");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] EPP: {epp} (Confirmado)");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] Tentativas: {i + 1}/{MAX_WRITE_RETRIES}");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] Tempo: {elapsedMs:F0}ms");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] ============================================");
                            return true;
                        }
                        else
                        {
                            _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ Escrita não persistiu corretamente");
                            _logger.LogWarning($"[SpeedShift][{operationId}] Esperado: Max={maxRatio}x, Min={minRatio}x, EPP={epp}");
                            _logger.LogWarning($"[SpeedShift][{operationId}] Confirmado: Max={verifyMax}x, Min={verifyMin}x, EPP={verifyEpp}");
                            _logger.LogWarning($"[SpeedShift][{operationId}] Motivo: BIOS ou firmware pode ter rejeitado o valor");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ Falha na validação (não foi possível ler MSR após escrita)");
                    }
                }
                else
                {
                    _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ WriteMsr falhou na tentativa {i + 1}");
                    _logger.LogWarning($"[SpeedShift][{operationId}] Motivo: Driver rejeitou escrita ou CPU bloqueou o MSR");
                }
                
                if (i < MAX_WRITE_RETRIES - 1)
                {
                    _logger.LogInfo($"[SpeedShift][{operationId}] Aguardando {RETRY_DELAY_MS}ms antes da próxima tentativa...");
                    Thread.Sleep(RETRY_DELAY_MS);
                }
            }

            // 9. FALHA APÓS TODAS AS TENTATIVAS
            _logger.LogError($"[SpeedShift][{operationId}] ❌ FALHA: Não foi possível configurar HWP Request após {MAX_WRITE_RETRIES} tentativas");
            _logger.LogError($"[SpeedShift][{operationId}] Motivo: MSR 0x{MSR_HWP_REQUEST:X} não aceitou o valor solicitado");
            _logger.LogError($"[SpeedShift][{operationId}] Valor solicitado: 0x{value:X}");
            _logger.LogError($"[SpeedShift][{operationId}] Ação: Verifique se HWP está habilitado e se a CPU suporta os valores solicitados");
            return false;
        }

        public bool SetEnergyPerfBias(int epb)
        {
            var operationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.UtcNow;
            _lastOperationContext = $"SetEnergyPerfBias_{operationId}";
            
            _logger?.LogTrace($"[SpeedShift][{operationId}] Enter SetEnergyPerfBias(epb={epb})");
            _logger.LogInfo($"[SpeedShift][{operationId}] ============================================");
            _logger.LogInfo($"[SpeedShift][{operationId}] INICIANDO CONFIGURAÇÃO ENERGY PERF BIAS");
            _logger.LogInfo($"[SpeedShift][{operationId}] ============================================");
            _logger.LogInfo($"[SpeedShift][{operationId}] Timestamp: {startTime:yyyy-MM-dd HH:mm:ss.fff}");
            _logger.LogInfo($"[SpeedShift][{operationId}] Thread: {Thread.CurrentThread.ManagedThreadId}");
            _logger.LogInfo($"[SpeedShift][{operationId}] EPB solicitado: {epb}");

            // 1. VALIDAÇÃO DE PARÂMETROS
            if (epb < 0 || epb > 15)
            {
                _logger.LogError($"[SpeedShift][{operationId}] ❌ FALHA: EPB inválido");
                _logger.LogError($"[SpeedShift][{operationId}] EPB deve ser 0-15 (recebido: {epb})");
                _logger.LogError($"[SpeedShift][{operationId}] Valores válidos: 0=Performance, 15=Power Saving");
                return false;
            }
            _logger.LogSuccess($"[SpeedShift][{operationId}] ✅ Parâmetro validado");

            // 2. LEITURA DO VALOR ATUAL
            _logger.LogInfo($"[SpeedShift][{operationId}] Lendo MSR 0x{MSR_ENERGY_PERF_BIAS:X} (IA32_ENERGY_PERF_BIAS)...");
            if (!_backend.ReadMsr(MSR_ENERGY_PERF_BIAS, out ulong current))
            {
                _logger.LogError($"[SpeedShift][{operationId}] ❌ FALHA: Não foi possível ler MSR 0x{MSR_ENERGY_PERF_BIAS:X}");
                _logger.LogError($"[SpeedShift][{operationId}] Motivo: CPU pode não suportar MSR ou BIOS bloqueou acesso");
                _logger.LogError($"[SpeedShift][{operationId}] Requisito: Intel Sandy Bridge (2nd gen) ou superior");
                return false;
            }
            
            int currentEpb = (int)(current & 0xF);
            _logger.LogInfo($"[SpeedShift][{operationId}] EPB atual: {currentEpb} (Raw: 0x{current:X})");

            // 3. VERIFICAÇÃO SE JÁ ESTÁ NO VALOR DESEJADO
            if (currentEpb == epb)
            {
                _logger.LogSuccess($"[SpeedShift][{operationId}] ✅ EPB já está no valor desejado: {epb}");
                return true;
            }

            // 4. CONSTRUÇÃO DO NOVO VALOR
            ulong newVal = (current & ~0xFUL) | (ulong)(uint)epb;
            _logger.LogInfo($"[SpeedShift][{operationId}] Valor calculado para escrita: 0x{newVal:X}");
            _logger.LogInfo($"[SpeedShift][{operationId}]   EPB (bits 0-3): {epb}");
            _logger.LogInfo($"[SpeedShift][{operationId}]   Bits preservados: 0x{current & ~0xFUL:X}");

            // 5. ESCRITA COM RETRY
            _logger.LogInfo($"[SpeedShift][{operationId}] ============================================");
            _logger.LogInfo($"[SpeedShift][{operationId}] INICIANDO ESCRITA COM RETRY");
            _logger.LogInfo($"[SpeedShift][{operationId}] ============================================");
            
            for (int i = 0; i < MAX_WRITE_RETRIES; i++)
            {
                _logger.LogInfo($"[SpeedShift][{operationId}] Tentativa {i + 1}/{MAX_WRITE_RETRIES}...");
                _logger.LogInfo($"[SpeedShift][{operationId}] Escrevendo MSR 0x{MSR_ENERGY_PERF_BIAS:X} com valor 0x{newVal:X}");
                
                if (_backend.WriteMsr(MSR_ENERGY_PERF_BIAS, newVal))
                {
                    _logger.LogSuccess($"[SpeedShift][{operationId}] ✅ WriteMsr executado com sucesso");
                    
                    // 6. VALIDAÇÃO DA ESCRITA (DOUBLE CHECK)
                    _logger.LogInfo($"[SpeedShift][{operationId}] Validando escrita...");
                    if (_backend.ReadMsr(MSR_ENERGY_PERF_BIAS, out ulong verifyValue))
                    {
                        int verifyEpb = (int)(verifyValue & 0xF);
                        _logger.LogInfo($"[SpeedShift][{operationId}] EPB após escrita: {verifyEpb} (Raw: 0x{verifyValue:X})");
                        
                        if (verifyEpb == epb)
                        {
                            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                            _logger.LogSuccess($"[SpeedShift][{operationId}] ============================================");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] ✅ ENERGY PERF BIAS CONFIGURADO COM SUCESSO");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] ============================================");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] EPB: {epb} (Confirmado)");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] Anterior: {currentEpb}");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] Tentativas: {i + 1}/{MAX_WRITE_RETRIES}");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] Tempo: {elapsedMs:F0}ms");
                            _logger.LogSuccess($"[SpeedShift][{operationId}] ============================================");
                            return true;
                        }
                        else
                        {
                            _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ Escrita não persistiu corretamente");
                            _logger.LogWarning($"[SpeedShift][{operationId}] Esperado: EPB={epb}");
                            _logger.LogWarning($"[SpeedShift][{operationId}] Confirmado: EPB={verifyEpb}");
                            _logger.LogWarning($"[SpeedShift][{operationId}] Motivo: BIOS ou firmware pode ter rejeitado o valor");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ Falha na validação (não foi possível ler MSR após escrita)");
                    }
                }
                else
                {
                    _logger.LogWarning($"[SpeedShift][{operationId}] ⚠️ WriteMsr falhou na tentativa {i + 1}");
                    _logger.LogWarning($"[SpeedShift][{operationId}] Motivo: Driver rejeitou escrita ou CPU bloqueou o MSR");
                }
                
                if (i < MAX_WRITE_RETRIES - 1)
                {
                    _logger.LogInfo($"[SpeedShift][{operationId}] Aguardando {RETRY_DELAY_MS}ms antes da próxima tentativa...");
                    Thread.Sleep(RETRY_DELAY_MS);
                }
            }

            // 7. FALHA APÓS TODAS AS TENTATIVAS
            _logger.LogError($"[SpeedShift][{operationId}] ❌ FALHA: Não foi possível configurar EPB após {MAX_WRITE_RETRIES} tentativas");
            _logger.LogError($"[SpeedShift][{operationId}] Motivo: MSR 0x{MSR_ENERGY_PERF_BIAS:X} não aceitou o valor solicitado");
            _logger.LogError($"[SpeedShift][{operationId}] Valor solicitado: 0x{newVal:X}");
            _logger.LogError($"[SpeedShift][{operationId}] Ação: Verifique se a CPU suporta IA32_ENERGY_PERF_BIAS");
            return false;
        }

        public bool GetHwpCapabilities(out int maxRatio, out int minRatio)
        {
            _logger?.LogTrace("[SpeedShift] Enter GetHwpCapabilities()");
            maxRatio = minRatio = 0;
            if (!_backend.ReadMsr(MSR_HWP_CAPABILITIES, out ulong caps))
                return false;

            maxRatio = (int)((caps >> 56) & 0xFF);
            minRatio = (int)((caps >> 40) & 0xFF);
            return true;
        }
    }
}
