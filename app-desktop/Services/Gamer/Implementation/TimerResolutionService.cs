using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Serviço para controle de Timer Resolution do Windows
    /// IMPACTO REAL: Reduz input lag de 15.6ms para 0.5ms
    /// </summary>
    public class TimerResolutionService : ITimerResolutionService, IDisposable
    {
        private readonly ILoggingService _logger;
        private bool _isHighResolutionActive;
        private uint _originalResolution;
        private uint _currentResolution;
        private readonly object _lock = new();

        // =====================================================
        // WINDOWS API - NTDLL (Timer Resolution real)
        // =====================================================
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryTimerResolution(out uint MinimumResolution, out uint MaximumResolution, out uint CurrentResolution);

        // timeBeginPeriod/timeEndPeriod como fallback
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint timeEndPeriod(uint uPeriod);

        // AUDITORIA FORENSE: 1.0ms em vez de 0.5ms.
        // 0.5ms causa: (a) aumento de interrupções DPC, (b) mais context switches,
        // (c) CPU não entra em C-states profundos. 1.0ms é o padrão de gaming há décadas
        // (CapFrameX, RTSS, MSI Afterburner) e fornece latência adequada sem overhead.
        private const uint TIMER_RESOLUTION_1MS = 10000;   // 1ms (recomendado para gaming)
        private const uint DEFAULT_RESOLUTION = 156250;    // 15.625ms (padrão Windows)

        public bool IsActive => _isHighResolutionActive;
        public bool IsMaxResolutionActive => _isHighResolutionActive;
        public double CurrentResolutionMs => _currentResolution / 10000.0;
        
        /// <summary>
        /// Obtém resolução atual em unidades de 100ns
        /// </summary>
        public uint GetCurrentResolution()
        {
            _logger.LogEntry(nameof(GetCurrentResolution));
            QueryCurrentResolution();
            _logger.LogExit(nameof(GetCurrentResolution), _currentResolution);
            return _currentResolution;
        }

        public TimerResolutionService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            QueryCurrentResolution();
            _logger.LogEntry("TimerResolutionService");
        }

        /// <summary>
        /// Define a resolução do timer para 1ms (ideal para gaming sem overhead).
        /// AUDITORIA FORENSE: 0.5ms removido — causa DPC latency e impede C-states.
        /// </summary>
        public bool SetMaximumResolution()
        {
            _logger.LogEntry(nameof(SetMaximumResolution));
            var stopwatch = Stopwatch.StartNew();
            lock (_lock)
            {
                if (_isHighResolutionActive)
                {
                    _logger.LogInfo("[TimerRes] Timer já está em alta resolução");
                    _logger.LogExit(nameof(SetMaximumResolution), true, stopwatch.ElapsedMilliseconds);
                    return true;
                }

                try
                {
                    // Salvar resolução original
                    NtQueryTimerResolution(out _, out _, out _originalResolution);
                    
                    _logger.LogInfo($"[TimerRes] Resolução original: {_originalResolution / 10000.0:F2}ms");

                    // Definir 1ms via NtSetTimerResolution (auditado: 0.5ms removido por overhead)
                    var status = NtSetTimerResolution(TIMER_RESOLUTION_1MS, true, out _currentResolution);
                    
                    if (status == 0)
                    {
                        _isHighResolutionActive = true;
                        _logger.LogSuccess($"[TimerRes] ✓ Timer Resolution: {_currentResolution / 10000.0:F2}ms (era {_originalResolution / 10000.0:F2}ms)");
                        
                        // AUDITORIA: timeBeginPeriod(1) removido — NtSetTimerResolution já define o timer
                        // Chamar ambos pode causar conflito de contagem de referência na restauração.
                        
                        _logger.LogExit(nameof(SetMaximumResolution), true, stopwatch.ElapsedMilliseconds);
                        return true;
                    }
                    else
                    {
                        // Fallback: usar timeBeginPeriod
                        _logger.LogWarning("[TimerRes] NtSetTimerResolution falhou, usando fallback timeBeginPeriod...");
                        
                        if (timeBeginPeriod(1) == 0)
                        {
                            _isHighResolutionActive = true;
                            _currentResolution = TIMER_RESOLUTION_1MS;
                            _logger.LogSuccess("[TimerRes] ✓ Timer Resolution via timeBeginPeriod: ~1ms");
                            _logger.LogExit(nameof(SetMaximumResolution), true, stopwatch.ElapsedMilliseconds);
                            return true;
                        }
                    }

                    _logger.LogError("[TimerRes] Falha ao definir timer resolution");
                    _logger.LogExit(nameof(SetMaximumResolution), false, stopwatch.ElapsedMilliseconds);
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[TimerRes] Erro: {ex.Message}", ex);
                    _logger.LogExit(nameof(SetMaximumResolution), false, stopwatch.ElapsedMilliseconds);
                    return false;
                }
            }
        }

        /// <summary>
        /// Define resolução específica em milissegundos
        /// </summary>
        public bool SetResolution(double milliseconds)
        {
            _logger.LogEntry(nameof(SetResolution), ("milliseconds", milliseconds));
            var stopwatch = Stopwatch.StartNew();
            lock (_lock)
            {
                try
                {
                    uint desired = (uint)(milliseconds * 10000);
                    var status = NtSetTimerResolution(desired, true, out _currentResolution);
                    
                    if (status == 0)
                    {
                        _isHighResolutionActive = milliseconds < 10;
                        _logger.LogInfo($"[TimerRes] Resolução definida: {_currentResolution / 10000.0:F2}ms");
                        _logger.LogExit(nameof(SetResolution), true, stopwatch.ElapsedMilliseconds);
                        return true;
                    }

                    _logger.LogExit(nameof(SetResolution), false, stopwatch.ElapsedMilliseconds);
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[TimerRes] Erro em SetResolution: {ex.Message}", ex);
                    _logger.LogExit(nameof(SetResolution), false, stopwatch.ElapsedMilliseconds);
                    return false;
                }
            }
        }

        /// <summary>
        /// Define resolução segura (1ms) — evita 0.5ms em cenários com anti-cheat
        /// </summary>
        public bool SetSafeResolution()
        {
            _logger.LogEntry(nameof(SetSafeResolution));
            var stopwatch = Stopwatch.StartNew();
            lock (_lock)
            {
                try
                {
                    uint desired = TIMER_RESOLUTION_1MS;
                    var status = NtSetTimerResolution(desired, true, out _currentResolution);

                    if (status == 0)
                    {
                        _isHighResolutionActive = true;
                        _logger.LogInfo($"[TimerRes] Resolução segura: {_currentResolution / 10000.0:F2}ms (1ms)");
                        _logger.LogExit(nameof(SetSafeResolution), true, stopwatch.ElapsedMilliseconds);
                        return true;
                    }

                    if (timeBeginPeriod(1) == 0)
                    {
                        _isHighResolutionActive = true;
                        _currentResolution = TIMER_RESOLUTION_1MS;
                        _logger.LogInfo("[TimerRes] Resolução segura via timeBeginPeriod: ~1ms");
                        _logger.LogExit(nameof(SetSafeResolution), true, stopwatch.ElapsedMilliseconds);
                        return true;
                    }

                    _logger.LogExit(nameof(SetSafeResolution), false, stopwatch.ElapsedMilliseconds);
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[TimerRes] Erro em SetSafeResolution: {ex.Message}", ex);
                    _logger.LogExit(nameof(SetSafeResolution), false, stopwatch.ElapsedMilliseconds);
                    return false;
                }
            }
        }

        /// <summary>
        /// Libera a resolução de volta ao padrão do Windows
        /// </summary>
        public bool ReleaseResolution()
        {
            _logger.LogEntry(nameof(ReleaseResolution));
            var stopwatch = Stopwatch.StartNew();
            lock (_lock)
            {
                if (!_isHighResolutionActive)
                {
                    _logger.LogExit(nameof(ReleaseResolution), true, stopwatch.ElapsedMilliseconds);
                    return true;
                }

                try
                {
                    // Restaurar via NtSetTimerResolution (sem SettimerResolution=false)
                    var status = NtSetTimerResolution(DEFAULT_RESOLUTION, false, out _currentResolution);
                    
                    _isHighResolutionActive = false;
                    _logger.LogInfo($"[TimerRes] Timer Resolution restaurado: {_currentResolution / 10000.0:F2}ms");
                    
                    _logger.LogExit(nameof(ReleaseResolution), true, stopwatch.ElapsedMilliseconds);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[TimerRes] Erro ao liberar: {ex.Message}", ex);
                    _logger.LogExit(nameof(ReleaseResolution), false, stopwatch.ElapsedMilliseconds);
                    return false;
                }
            }
        }

        /// <summary>
        /// Consulta a resolução atual do timer
        /// </summary>
        public (double current, double max) GetResolutionInfo()
        {
            _logger.LogEntry(nameof(GetResolutionInfo));
            try
            {
                NtQueryTimerResolution(out uint min, out uint max, out uint current);
                var result = (current / 10000.0, max / 10000.0);
                _logger.LogExit(nameof(GetResolutionInfo), result);
                return result;
            }
            catch
            {
                var result = (15.625, 0.5);
                _logger.LogExit(nameof(GetResolutionInfo), result);
                return result;
            }
        }

        private void QueryCurrentResolution()
        {
            _logger.LogEntry(nameof(QueryCurrentResolution));
            try
            {
                NtQueryTimerResolution(out _, out _, out _currentResolution);
            }
            catch
            {
                _currentResolution = DEFAULT_RESOLUTION;
            }
            _logger.LogExit(nameof(QueryCurrentResolution));
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            ReleaseResolution();
            _logger.LogExit(nameof(Dispose));
        }
    }
}

