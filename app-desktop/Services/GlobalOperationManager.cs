using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VoltrisOptimizer.Services
{
    public enum OperationPriority { High, Normal, Low }

    public class QueuedOperation : INotifyPropertyChanged
    {
        private bool _isPaused;
        private int _progressPercentage;
        private string _progressMessage = string.Empty;

        public string Id { get; }
        public string Name { get; }
        public OperationPriority Priority { get; }
        public CancellationTokenSource Cts { get; }
        internal Func<CancellationToken, Task>? Operation { get; set; }
        internal Func<IProgress<int>, CancellationToken, Task>? OperationWithProgress { get; set; }
        internal IProgress<int>? AdditionalProgress { get; set; }
        internal int SavedProgress { get; set; }
        internal string SavedMessage { get; set; } = string.Empty;
        internal bool IsRunning { get; set; }

        public bool IsPaused
        {
            get => _isPaused;
            set { if (_isPaused != value) { _isPaused = value; OnPropertyChanged(); } }
        }

        public int ProgressPercentage
        {
            get => _progressPercentage;
            set { if (_progressPercentage != value) { _progressPercentage = value; OnPropertyChanged(); } }
        }

        public string ProgressMessage
        {
            get => _progressMessage;
            set { if (_progressMessage != value) { _progressMessage = value; OnPropertyChanged(); } }
        }

        public QueuedOperation(string id, string name, OperationPriority priority)
        {
            Id = id;
            Name = name;
            Priority = priority;
            Cts = new CancellationTokenSource();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Estados possíveis de operações globais
    /// </summary>
    public enum GlobalOperationState
    {
        Idle,
        PreparingPc,
        CleaningSystem,
        OptimizingPerformance,
        OptimizingNetwork,
        Other
    }

    /// <summary>
    /// Informações de progresso de uma operação
    /// </summary>
    public class OperationProgress
    {
        public double Percentage { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class GlobalOperationManager : INotifyPropertyChanged
    {
        private static GlobalOperationManager? _instance;
        private static readonly object _lock = new object();

        public static GlobalOperationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new GlobalOperationManager();
                    }
                }
                return _instance;
            }
        }

        private readonly ILoggingService? _logger;
        private readonly SemaphoreSlim _executionLock;
        private readonly ConcurrentDictionary<string, QueuedOperation> _operationsById;
        private readonly List<QueuedOperation> _priorityQueue;
        private readonly ObservableCollection<QueuedOperation> _operationQueue;
        private readonly object _queueLock;

        private CancellationTokenSource? _currentCancellationTokenSource;
        private QueuedOperation? _currentOperation;
        private QueuedOperation? _pausedOperation;
        private bool _isProcessingQueue;

        private GlobalOperationState _currentState;
        private string _currentOperationName;
        private double _globalProgress;
        private string _globalStatusMessage;

        public event PropertyChangedEventHandler? PropertyChanged;

        private GlobalOperationManager()
        {
            _logger = App.LoggingService;
            _executionLock = new SemaphoreSlim(1, 1);
            _operationsById = new ConcurrentDictionary<string, QueuedOperation>();
            _priorityQueue = new List<QueuedOperation>();
            _operationQueue = new ObservableCollection<QueuedOperation>();
            _queueLock = new object();
            _currentState = GlobalOperationState.Idle;
            _currentOperationName = string.Empty;
            _globalProgress = 0;
            _globalStatusMessage = string.Empty;
            _isProcessingQueue = false;
        }

        #region Public Properties

        public GlobalOperationState CurrentState
        {
            get => _currentState;
            private set
            {
                if (_currentState != value)
                {
                    _currentState = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsOperationRunning));
                }
            }
        }

        public bool IsOperationRunning => CurrentState != GlobalOperationState.Idle;

        public string CurrentOperationName
        {
            get => _currentOperationName;
            private set
            {
                if (_currentOperationName != value)
                {
                    _currentOperationName = value;
                    OnPropertyChanged();
                }
            }
        }

        public double GlobalProgress
        {
            get => _globalProgress;
            private set
            {
                if (Math.Abs(_globalProgress - value) > 0.01)
                {
                    _globalProgress = value;
                    OnPropertyChanged();
                }
            }
        }

        public string GlobalStatusMessage
        {
            get => _globalStatusMessage;
            private set
            {
                if (_globalStatusMessage != value)
                {
                    _globalStatusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<QueuedOperation> OperationQueue => _operationQueue;

        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            private set
            {
                if (_isPaused != value)
                {
                    _isPaused = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(QueueStatusText));
                    OnPropertyChanged(nameof(HasQueuedItems));
                }
            }
        }

        private int _queueDepth;
        public int QueueDepth
        {
            get => _queueDepth;
            private set
            {
                if (_queueDepth != value)
                {
                    _queueDepth = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasQueuedItems));
                    OnPropertyChanged(nameof(QueueStatusText));
                }
            }
        }

        public string QueueStatusText
        {
            get
            {
                var parts = new List<string>();
                if (_queueDepth > 0)
                    parts.Add(string.Format(LocalizationService.Instance.GetString("QueueOperationsCount"), _queueDepth));
                if (_isPaused)
                    parts.Add(LocalizationService.Instance.GetString("OnePaused"));
                return parts.Count > 0 ? string.Join(" \u2022 ", parts) : string.Empty;
            }
        }

        public bool HasQueuedItems => _queueDepth > 0 || _isPaused;

        private bool _canCancelCurrent;
        public bool CanCancelCurrent
        {
            get => _canCancelCurrent;
            private set
            {
                if (_canCancelCurrent != value)
                {
                    _canCancelCurrent = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region New Public Methods

        public async Task<string> EnqueueAsync(
            string operationName,
            OperationPriority priority,
            Func<CancellationToken, Task> operation)
        {
            var opId = Guid.NewGuid().ToString("N");
            var queued = new QueuedOperation(opId, operationName, priority)
            {
                Operation = operation
            };

            _operationsById.TryAdd(opId, queued);
            InsertIntoQueue(queued);

            _logger?.LogInfo($"[GlobalOperation] Enfileirada '{operationName}' (ID: {opId}, Prioridade: {priority})");

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _operationQueue.Add(queued);
                QueueDepth = _operationQueue.Count(q => !q.IsRunning && !q.IsPaused);
                CanCancelCurrent = _currentOperation != null || _pausedOperation != null;
                OnPropertyChanged(nameof(QueueStatusText));
                OnPropertyChanged(nameof(HasQueuedItems));
            });

            _ = ProcessQueueAsync();
            return opId;
        }

        public async Task<string> EnqueueAsync<T>(
            string operationName,
            OperationPriority priority,
            Func<IProgress<T>, CancellationToken, Task> operation,
            IProgress<T> additionalProgress)
        {
            var opId = Guid.NewGuid().ToString("N");
            var queued = new QueuedOperation(opId, operationName, priority)
            {
                OperationWithProgress = async (progress, ct) =>
                {
                    await operation(additionalProgress, ct);
                }
            };

            _operationsById.TryAdd(opId, queued);
            InsertIntoQueue(queued);

            _logger?.LogInfo($"[GlobalOperation] Enfileirada '{operationName}' (ID: {opId}, Prioridade: {priority})");

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _operationQueue.Add(queued);
                QueueDepth = _operationQueue.Count(q => !q.IsRunning && !q.IsPaused);
                CanCancelCurrent = _currentOperation != null || _pausedOperation != null;
                OnPropertyChanged(nameof(QueueStatusText));
                OnPropertyChanged(nameof(HasQueuedItems));
            });

            _ = ProcessQueueAsync();
            return opId;
        }

        public void CancelOperation(string operationId)
        {
            if (_operationsById.TryGetValue(operationId, out var op))
            {
                _logger?.LogInfo($"[GlobalOperation] Cancelando opera\u00e7\u00e3o '{op.Name}' (ID: {operationId})");

                if (!op.Cts.IsCancellationRequested)
                    op.Cts.Cancel();

                RemoveFromQueue(op);

                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _operationQueue.Remove(op);
                    QueueDepth = _operationQueue.Count(q => !q.IsRunning && !q.IsPaused);
                    UpdateCanCancelCurrent();
                    OnPropertyChanged(nameof(QueueStatusText));
                    OnPropertyChanged(nameof(HasQueuedItems));
                });
            }
        }

        public void CancelCurrent()
        {
            if (_currentOperation != null && !_currentOperation.Cts.IsCancellationRequested)
            {
                _logger?.LogInfo($"[GlobalOperation] Cancelando opera\u00e7\u00e3o atual: {_currentOperation.Name}");
                _currentOperation.Cts.Cancel();
            }

            if (_pausedOperation != null && !_pausedOperation.Cts.IsCancellationRequested)
            {
                _logger?.LogInfo($"[GlobalOperation] Cancelando opera\u00e7\u00e3o pausada: {_pausedOperation.Name}");
                _pausedOperation.Cts.Cancel();
            }
        }

        public void CancelAll()
        {
            _logger?.LogInfo("[GlobalOperation] Cancelando todas as opera\u00e7\u00f5es");

            CancelCurrent();

            foreach (var kvp in _operationsById)
            {
                if (!kvp.Value.Cts.IsCancellationRequested)
                    kvp.Value.Cts.Cancel();
            }

            lock (_queueLock)
            {
                _priorityQueue.Clear();
                _operationsById.Clear();
            }

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _operationQueue.Clear();
                _currentOperation = null;
                _pausedOperation = null;
                IsPaused = false;
                QueueDepth = 0;
                CanCancelCurrent = false;
                CurrentState = GlobalOperationState.Idle;
                CurrentOperationName = string.Empty;
                GlobalProgress = 0;
                GlobalStatusMessage = string.Empty;
                OnPropertyChanged(nameof(QueueStatusText));
                OnPropertyChanged(nameof(HasQueuedItems));
                GlobalProgressService.Instance.ResetQueue();
            });
        }

        public void PauseCurrent()
        {
            if (_currentOperation == null || _pausedOperation != null) return;

            _logger?.LogInfo($"[GlobalOperation] Pausando opera\u00e7\u00e3o: {_currentOperation.Name}");

            _currentOperation.SavedProgress = _currentOperation.ProgressPercentage;
            _currentOperation.SavedMessage = _currentOperation.ProgressMessage;
            _currentOperation.IsPaused = true;
            _currentOperation.IsRunning = false;

            GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("PausedOperation"), _currentOperation.Name));

            _pausedOperation = _currentOperation;
            _currentOperation = null;

            if (_currentCancellationTokenSource != null)
            {
                _currentCancellationTokenSource.Cancel();
                _currentCancellationTokenSource.Dispose();
                _currentCancellationTokenSource = null;
            }

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsPaused = true;
                CurrentState = GlobalOperationState.Idle;
                CurrentOperationName = string.Empty;
                GlobalProgress = 0;
                GlobalStatusMessage = string.Empty;
                CanCancelCurrent = true;
                QueueDepth = _operationQueue.Count(q => !q.IsRunning && !q.IsPaused);
                OnPropertyChanged(nameof(QueueStatusText));
                OnPropertyChanged(nameof(HasQueuedItems));
            });

            _ = ResumeFromPauseOrProcessQueue();
        }

        public void ResumeCurrent()
        {
            if (_pausedOperation == null) return;
            _ = ResumePausedOperationAsync();
        }

        #endregion

        #region Existing Public Methods (Backward Compatibility)

        public void EnqueueOperation(
            GlobalOperationState operationState,
            string operationName,
            Func<IProgress<OperationProgress>, CancellationToken, Task> operation)
        {
            var opId = Guid.NewGuid().ToString("N");
            var queued = new QueuedOperation(opId, operationName, OperationPriority.Normal);
            var capturedOp = queued;
            queued.Operation = async ct =>
            {
                var progress = new Progress<OperationProgress>(p =>
                {
                    capturedOp.ProgressPercentage = (int)Math.Round(p.Percentage);
                    capturedOp.ProgressMessage = p.Message;
                    GlobalProgressService.Instance.UpdateProgress((int)Math.Round(p.Percentage), p.Message);
                });
                await operation(progress, ct);
            };

            _operationsById.TryAdd(opId, queued);
            InsertIntoQueue(queued);

            _logger?.LogInfo($"[GlobalOperation] Opera\u00e7\u00e3o '{operationName}' adicionada \u00e0 fila. Total na fila: {GetQueueCount()}");

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _operationQueue.Add(queued);
                QueueDepth = _operationQueue.Count(q => !q.IsRunning && !q.IsPaused);
                OnPropertyChanged(nameof(QueueStatusText));
                OnPropertyChanged(nameof(HasQueuedItems));
            });

            _ = ProcessQueueAsync();
        }

        public int GetQueueCount() => _priorityQueue.Count(q => !q.IsRunning);

        public bool HasQueuedOperations() => GetQueueCount() > 0;

        public async Task<bool> TryStartOperationAsync(
            GlobalOperationState operationState,
            string operationName,
            Func<IProgress<OperationProgress>, CancellationToken, Task> operation,
            bool isBackground = false)
        {
            if (await _executionLock.WaitAsync(0))
            {
                _executionLock.Release();
                return await ExecuteLegacyOperationAsync(operationState, operationName, operation);
            }

            if (isBackground)
            {
                _logger?.LogInfo($"[GlobalOperation] Background operation '{operationName}' blocked by '{_currentOperation?.Name ?? "none"}' - Auto-enqueuing.");
                EnqueueOperation(operationState, operationName, operation);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    NotificationManager.ShowInfo(
                        LocalizationService.Instance.GetString("AutoScheduled"),
                        string.Format(LocalizationService.Instance.GetString("OperationEnqueued"), operationName)
                    );
                });
                return false;
            }

            _logger?.LogWarning($"[GlobalOperation] Tentativa de iniciar '{operationName}' bloqueada - opera\u00e7\u00e3o '{_currentOperation?.Name ?? "none"}' j\u00e1 em execu\u00e7\u00e3o");

            var choice = await ShowOperationQueueDialogAsync(operationName);

            switch (choice)
            {
                case UI.Windows.OperationQueueDialog.UserChoice.AddToQueue:
                    EnqueueOperation(operationState, operationName, operation);
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        NotificationManager.ShowSuccess(
                            LocalizationService.Instance.GetString("AddedToQueue"),
                            string.Format(LocalizationService.Instance.GetString("WillExecuteAfterCurrent"), operationName)
                        );
                    });
                    return false;

                case UI.Windows.OperationQueueDialog.UserChoice.ExecuteNow:
                    _logger?.LogWarning($"[GlobalOperation] Usu\u00e1rio optou por cancelar '{_currentOperation?.Name ?? "none"}' e executar '{operationName}' imediatamente");
                    CancelCurrent();
                    await Task.Delay(1000);
                    return await TryStartOperationAsync(operationState, operationName, operation, isBackground);

                case UI.Windows.OperationQueueDialog.UserChoice.ScheduleLater:
                    _logger?.LogInfo($"[GlobalOperation] Usu\u00e1rio optou por agendar '{operationName}' para depois");
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        NotificationManager.ShowInfo(
                            LocalizationService.Instance.GetString("OperationScheduled"),
                            string.Format(LocalizationService.Instance.GetString("ExecuteFromMenu"), operationName)
                        );
                    });
                    return false;

                case UI.Windows.OperationQueueDialog.UserChoice.Cancel:
                default:
                    return false;
            }
        }

        public bool CanStartOperation(string requestedOperationName)
        {
            if (IsOperationRunning || _pausedOperation != null)
            {
                _logger?.LogWarning($"[GlobalOperation] Tentativa de iniciar '{requestedOperationName}' bloqueada - opera\u00e7\u00e3o '{_currentOperation?.Name ?? _pausedOperation?.Name}' j\u00e1 em execu\u00e7\u00e3o");

                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    NotificationManager.ShowWarning(
                        LocalizationService.Instance.GetString("OperationInProgress"),
                        string.Format(LocalizationService.Instance.GetString("InitialPrepInProgress"), CurrentOperationName)
                    );
                });

                return false;
            }

            return true;
        }

        public void CancelCurrentOperation()
        {
            CancelCurrent();
        }

        public void UpdateProgress(double percentage, string message)
        {
            if (_currentOperation == null && _pausedOperation == null)
            {
                _logger?.LogWarning("[GlobalOperation] Tentativa de atualizar progresso sem opera\u00e7\u00e3o ativa - propagando mesmo assim para GlobalProgressService");
                GlobalProgressService.Instance.UpdateProgress((int)Math.Round(percentage), message);
                return;
            }

            var target = _currentOperation ?? _pausedOperation;
            if (target != null)
            {
                target.ProgressPercentage = (int)Math.Round(percentage);
                target.ProgressMessage = message;
            }

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                GlobalProgress = percentage;
                GlobalStatusMessage = message;
                GlobalProgressService.Instance.UpdateProgress((int)Math.Round(percentage), message);
            });
        }

        #endregion

        #region Private Methods

        private void InsertIntoQueue(QueuedOperation op)
        {
            lock (_queueLock)
            {
                var insertAt = _priorityQueue.FindIndex(q =>
                    q.Priority < op.Priority);
                if (insertAt < 0)
                    _priorityQueue.Add(op);
                else
                    _priorityQueue.Insert(insertAt, op);
            }
        }

        private QueuedOperation? DequeueHighestPriority()
        {
            lock (_queueLock)
            {
                if (_priorityQueue.Count == 0) return null;
                var next = _priorityQueue[0];
                _priorityQueue.RemoveAt(0);
                return next;
            }
        }

        private bool RemoveFromQueue(QueuedOperation op)
        {
            lock (_queueLock)
            {
                return _priorityQueue.Remove(op);
            }
        }

        private async Task ProcessQueueAsync()
        {
            if (_isProcessingQueue) return;
            _isProcessingQueue = true;

            try
            {
                while (true)
                {
                    QueuedOperation? next = null;

                    lock (_queueLock)
                    {
                        if (_priorityQueue.Count > 0)
                            next = _priorityQueue[0];
                    }

                    if (next == null) break;

                    // If we have a current operation and the next one is higher priority, pause current
                    if (_currentOperation != null && next.Priority > _currentOperation.Priority)
                    {
                        PauseCurrent();
                        // After pause, the queue will be re-processed via ResumeFromPauseOrProcessQueue
                        break;
                    }

                    await ExecuteOperationAsync(next);
                }
            }
            finally
            {
                _isProcessingQueue = false;
            }
        }

        private async Task ExecuteOperationAsync(QueuedOperation op)
        {
            if (!_operationsById.TryGetValue(op.Id, out _)) return;

            if (op.Cts.IsCancellationRequested) return;

            await _executionLock.WaitAsync();
            try
            {
                if (op.Cts.IsCancellationRequested) return;

                _currentOperation = op;
                op.IsRunning = true;
                _currentCancellationTokenSource = op.Cts;
                RemoveFromQueue(op);

                _ = Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _operationQueue.Remove(op);
                    CurrentState = GlobalOperationState.Other;
                    CurrentOperationName = op.Name;
                    GlobalProgress = op.SavedProgress;
                    GlobalStatusMessage = op.SavedMessage;
                    QueueDepth = _operationQueue.Count(q => !q.IsRunning && !q.IsPaused);
                    CanCancelCurrent = true;
                    OnPropertyChanged(nameof(QueueStatusText));
                    OnPropertyChanged(nameof(HasQueuedItems));
                });

                var gpsOp = GlobalProgressService.Instance.BeginOperation(
                    op.Name,
                    isPriority: op.Priority == OperationPriority.High);

                var progress = new Progress<int>(p =>
                {
                    op.ProgressPercentage = p;
                    gpsOp.UpdateProgress(p);
                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        GlobalProgress = p;
                    });
                });

                _logger?.LogInfo($"=== [GlobalOperation] Iniciando: {op.Name} ===");

                try
                {
                    if (op.Operation != null)
                    {
                        await op.Operation(op.Cts.Token);
                    }
                    else if (op.OperationWithProgress != null)
                    {
                        await op.OperationWithProgress(progress, op.Cts.Token);
                    }

                    gpsOp.Complete(string.Format(LocalizationService.Instance.GetString("OperationCompleted"), op.Name));

                    _logger?.LogInfo($"=== [GlobalOperation] Conclu\u00edda: {op.Name} ===");

                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        GlobalProgress = 100;
                        GlobalStatusMessage = LocalizationService.Instance.GetString("Completed");
                    });

                    await Task.Delay(2000);
                }
                catch (OperationCanceledException)
                {
                    gpsOp.Complete(string.Format(LocalizationService.Instance.GetString("CancelledOperation"), op.Name));
                    _logger?.LogWarning($"[GlobalOperation] Cancelada: {op.Name}");

                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        GlobalStatusMessage = LocalizationService.Instance.GetString("OperationCancelled");
                    });

                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    gpsOp.Complete(string.Format(LocalizationService.Instance.GetString("ErrorOperation"), ex.Message));
                    _logger?.LogError($"[GlobalOperation] Erro em '{op.Name}': {ex.Message}", ex);

                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        GlobalStatusMessage = string.Format(LocalizationService.Instance.GetString("ErrorMessage"), ex.Message);
                    });

                    await Task.Delay(3000);
                }
                finally
                {
                    _currentOperation = null;
                    _currentCancellationTokenSource = null;

                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (_pausedOperation == null)
                        {
                            CurrentState = GlobalOperationState.Idle;
                            CurrentOperationName = string.Empty;
                            GlobalProgress = 0;
                            GlobalStatusMessage = string.Empty;
                        }
                        CanCancelCurrent = _pausedOperation != null;
                        OnPropertyChanged(nameof(QueueStatusText));
                        OnPropertyChanged(nameof(HasQueuedItems));
                    });
                }
            }
            finally
            {
                _executionLock.Release();
            }

            // Auto-resume paused operation if exists
            if (_pausedOperation != null)
            {
                await ResumePausedOperationAsync();
            }
            else
            {
                _ = ProcessQueueAsync();
            }
        }

        private async Task ResumePausedOperationAsync()
        {
            if (_pausedOperation == null) return;

            var op = _pausedOperation;
            _pausedOperation = null;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsPaused = false;
                OnPropertyChanged(nameof(QueueStatusText));
                OnPropertyChanged(nameof(HasQueuedItems));
            });

            op.IsPaused = false;
            op.IsRunning = false;

            // Re-enqueue with its original state, then process
            lock (_queueLock)
            {
                _priorityQueue.Insert(0, op);
            }

            _ = ProcessQueueAsync();
        }

        private async Task ResumeFromPauseOrProcessQueue()
        {
            await Task.Delay(100);
            _ = ProcessQueueAsync();
        }

        private async Task<bool> ExecuteLegacyOperationAsync(
            GlobalOperationState operationState,
            string operationName,
            Func<IProgress<OperationProgress>, CancellationToken, Task> operation)
        {
            var opId = Guid.NewGuid().ToString("N");
            var legacyOp = new QueuedOperation(opId, operationName, OperationPriority.Normal)
            {
                Operation = async ct =>
                {
                    var progress = new Progress<OperationProgress>(p =>
                    {
                        GlobalProgress = p.Percentage;
                        GlobalStatusMessage = p.Message;
                        GlobalProgressService.Instance.UpdateProgress((int)Math.Round(p.Percentage), p.Message);
                    });
                    await operation(progress, ct);
                }
            };

            _operationsById.TryAdd(opId, legacyOp);

            await _executionLock.WaitAsync();
            bool result = false;
            try
            {
                if (legacyOp.Cts.IsCancellationRequested) return false;

                _currentOperation = legacyOp;
                legacyOp.IsRunning = true;
                _currentCancellationTokenSource = legacyOp.Cts;

                CurrentState = operationState;
                CurrentOperationName = operationName;
                GlobalProgress = 0;
                GlobalStatusMessage = LocalizationService.Instance.GetString("StartingEllipsis");

                _logger?.LogInfo($"=== [GlobalOperation] Iniciando: {operationName} ===");

                var gpsOp = GlobalProgressService.Instance.BeginOperation(operationName, isPriority: false);

                try
                {
                    await legacyOp.Operation(legacyOp.Cts.Token);

                    gpsOp.Complete(string.Format(LocalizationService.Instance.GetString("OperationCompleted"), operationName));
                    _logger?.LogInfo($"=== [GlobalOperation] Conclu\u00edda: {operationName} ===");

                    GlobalProgress = 100;
                    GlobalStatusMessage = LocalizationService.Instance.GetString("Completed");
                    await Task.Delay(2000);

                    result = true;
                }
                catch (OperationCanceledException)
                {
                    gpsOp.Complete(string.Format(LocalizationService.Instance.GetString("CancelledOperation"), operationName));
                    _logger?.LogWarning($"[GlobalOperation] Cancelada: {operationName}");
                    GlobalStatusMessage = LocalizationService.Instance.GetString("OperationCancelled");
                    await Task.Delay(1000);
                    result = false;
                }
                catch (Exception ex)
                {
                    gpsOp.Complete(string.Format(LocalizationService.Instance.GetString("ErrorOperation"), ex.Message));
                    _logger?.LogError($"[GlobalOperation] Erro em '{operationName}': {ex.Message}", ex);
                    GlobalStatusMessage = string.Format(LocalizationService.Instance.GetString("ErrorMessage"), ex.Message);
                    await Task.Delay(3000);
                    result = false;
                }
                finally
                {
                    _currentOperation = null;
                    _currentCancellationTokenSource = null;

                    CurrentState = GlobalOperationState.Idle;
                    CurrentOperationName = string.Empty;
                    GlobalProgress = 0;
                    GlobalStatusMessage = string.Empty;

                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        CanCancelCurrent = _pausedOperation != null;
                        OnPropertyChanged(nameof(QueueStatusText));
                        OnPropertyChanged(nameof(HasQueuedItems));
                    });
                }
            }
            finally
            {
                _executionLock.Release();
            }

            _ = ProcessQueueAsync();
            return result;
        }

        private void UpdateCanCancelCurrent()
        {
            CanCancelCurrent = _currentOperation != null || _pausedOperation != null;
        }

        private async Task<UI.Windows.OperationQueueDialog.UserChoice> ShowOperationQueueDialogAsync(string requestedOperationName)
        {
            var choice = UI.Windows.OperationQueueDialog.UserChoice.Cancel;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new UI.Windows.OperationQueueDialog(
                    _currentOperation?.Name ?? _pausedOperation?.Name ?? string.Empty,
                    GlobalProgress,
                    requestedOperationName
                );

                dialog.ShowDialog();
                choice = dialog.Result;
            });

            return choice;
        }

        #endregion

        #region INotifyPropertyChanged

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
