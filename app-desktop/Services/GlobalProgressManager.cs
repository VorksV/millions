using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VoltrisOptimizer.Services
{
    public enum TaskState
    {
        Waiting,
        Initializing,
        Running,
        Paused,
        Cancelled,
        Completed,
        Failed
    }

    public class ActiveTaskInfo
    {
        public string TaskId { get; }
        public string Name { get; }
        public string Page { get; }
        public int Priority { get; }
        public int Progress { get; }
        public string Message { get; }
        public TaskState State { get; }
        public DateTime StartTime { get; }
        public TimeSpan Elapsed => DateTime.UtcNow - StartTime;

        public ActiveTaskInfo(string taskId, string name, string page, int priority, int progress, string message, TaskState state, DateTime startTime)
        {
            TaskId = taskId; Name = name; Page = page; Priority = priority;
            Progress = progress; Message = message; State = state; StartTime = startTime;
        }
    }

    public sealed class TaskToken : IDisposable
    {
        private readonly string _taskId;
        private readonly GlobalProgressManager _manager;
        private int _completed;

        internal string TaskId => _taskId;
        public string Name { get; }
        public bool IsCompleted => _completed == 1;

        internal TaskToken(string taskId, GlobalProgressManager manager, string name)
        {
            _taskId = taskId;
            _manager = manager;
            Name = name;
        }

        public void UpdateProgress(int percentage, string? message = null)
        {
            if (_completed == 1) return;
            _manager.InternalUpdate(_taskId, percentage, message);
        }

        public void Complete(string? finalMessage = null)
        {
            if (Interlocked.CompareExchange(ref _completed, 1, 0) == 0)
                _manager.InternalComplete(_taskId, finalMessage);
        }

        public void Fail(string? errorMessage = null)
        {
            if (Interlocked.CompareExchange(ref _completed, 1, 0) == 0)
                _manager.InternalFail(_taskId, errorMessage);
        }

        public void Cancel()
        {
            if (Interlocked.CompareExchange(ref _completed, 1, 0) == 0)
                _manager.InternalCancel(_taskId);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _completed, 1, 0) == 0)
                _manager.InternalComplete(_taskId, null);
        }
    }

    public class GlobalProgressManager
    {
        private static GlobalProgressManager? _instance;
        private static readonly object _singletonLock = new();

        public static GlobalProgressManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_singletonLock)
                        _instance ??= new GlobalProgressManager();
                }
                return _instance;
            }
        }

        public event EventHandler<ProgressEventArgs>? ProgressChanged;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? OperationCompleted;
        public event EventHandler<TaskStateChangedEventArgs>? TaskStateChanged;

        private readonly List<TaskEntry> _tasks = new();
        private readonly object _tasksLock = new object();
        private string? _displayedTaskId;
        private ILoggingService? Logger => _logger ??= TryGetLogger();
        private ILoggingService? _logger;
        private static ILoggingService? TryGetLogger()
        {
            try { return App.LoggingService; } catch { return null; }
        }

        private const int CompletionDisplayMs = 2500;

        private class TaskEntry
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public string Name { get; set; } = "";
            public string Page { get; set; } = "";
            public int Priority { get; set; }
            public TaskState State { get; set; } = TaskState.Waiting;
            public int Progress { get; set; }
            public string Message { get; set; } = "";
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? CompletionTime { get; set; }

        /// <summary>
        /// Última vez que a tarefa recebeu progresso/mensagem.
        /// O reaper usa isto para encerrar tarefas que foram abandonadas
        /// (ex.: o usuário fechou a tela no meio), que antes ficavam vivas
        /// para sempre e travavam o texto do rodapé.
        /// </summary>
        public DateTime LastUpdateTime { get; set; } = DateTime.UtcNow;
            public bool IsCompleted =>
                State == TaskState.Completed || State == TaskState.Failed ||
                State == TaskState.Cancelled;
        }

        /// <summary>
        /// Minutos sem receber progresso antes de o reaper encerrar a tarefa.
        /// Generoso de propósito: uma desfragmentação em HD ou uma limpeza
        /// pesada pode ficar vários minutos sem mudar de percentual, e
        /// encerrá-las seria pior que o defeito que corrigimos.
        /// </summary>
        private static readonly TimeSpan StaleTaskThreshold = TimeSpan.FromMinutes(4);

        private Timer? _reaper;

        private GlobalProgressManager()
        {
            // Rede de segurança: nenhuma tarefa abandonada pode prender o
            // texto do rodapé indefinidamente. O reaper só encerra tarefas que
            // estão paradas há mais de StaleTaskThreshold, ou seja, tarefas
            // que o código esqueceu de concluir.
            _reaper = new Timer(_ => ReapStaleTasks(), null, StaleTaskThreshold, StaleTaskThreshold);
        }

        private void ReapStaleTasks()
        {
            try
            {
                var cutoff = DateTime.UtcNow - StaleTaskThreshold;
                var stale = new List<string>();
                lock (_tasksLock)
                {
                    foreach (var t in _tasks)
                    {
                        if (t.IsCompleted) continue;
                        if (t.LastUpdateTime < cutoff) stale.Add(t.Id);
                    }
                }

                foreach (var id in stale)
                {
                    Logger?.LogWarning($"[GlobalProgressManager] REAP: tarefa parada ha mais de {StaleTaskThreshold.TotalMinutes:F0} min sera encerrada para nao travar o rodape. ID: {id}");
                    InternalComplete(id, null);
                }
            }
            catch { }
        }


        public TaskToken RegisterTask(string name, string page = "", int priority = 0)
        {
            var entry = new TaskEntry
            {
                Name = name,
                Page = page,
                Priority = priority,
                State = TaskState.Running,
                Message = name
            };

            lock (_tasksLock) {
                _tasks.Add(entry);
                Logger?.LogInfo($"[GlobalProgressManager] ▶ TASK STARTED: '{name}' | Page: '{page}' | Priority: {priority} | ID: {entry.Id}");
                Logger?.LogInfo($"[GlobalProgressManager]   Task Count: {_tasks.Count(t => !t.IsCompleted)}");
                EvaluateDisplay();
            }
            

            FireEvent(0, entry.Message);
            return new TaskToken(entry.Id, this, name);
        }

        public void UpdateProgress(string taskId, int percentage, string? message)
        {
            lock (_tasksLock) {
                var task = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (task == null || task.IsCompleted) return;

                task.Progress = Math.Max(0, Math.Min(100, percentage));
                if (message != null) task.Message = message;
                // Marca como "viva": o reaper não encerra tarefas que ainda
                // recebem progresso (ex.: desfragmentação lenta em HD).
                task.LastUpdateTime = DateTime.UtcNow;

                if (_displayedTaskId == taskId)
                    FireEvent(task.Progress, message);
            }
            
        }

        internal void InternalUpdate(string taskId, int percentage, string? message)
        {
            UpdateProgress(taskId, percentage, message);
        }

        internal void InternalComplete(string taskId, string? finalMessage)
        {
            string? name = null;
            lock (_tasksLock) {
                var task = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (task == null || task.IsCompleted) return;

                task.State = TaskState.Completed;
                task.Progress = 100;
                task.CompletionTime = DateTime.UtcNow;
                if (finalMessage != null) task.Message = finalMessage;
                name = task.Name;

                Logger?.LogInfo($"[GlobalProgressManager] ✔ TASK COMPLETED: '{task.Name}' ({task.Page}) | Duration: {(DateTime.UtcNow - task.StartTime).TotalSeconds:F1}s");

                if (_displayedTaskId == taskId)
                    FireEvent(100, finalMessage ?? $"{name} concluído");
            }
            

            TaskStateChanged?.Invoke(this, new TaskStateChangedEventArgs(name ?? "", TaskState.Completed));

            _ = DelayedCleanup(taskId, name ?? "");
        }

        internal void InternalFail(string taskId, string? errorMessage)
        {
            string? name = null;
            lock (_tasksLock) {
                var task = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (task == null || task.IsCompleted) return;

                task.State = TaskState.Failed;
                task.Progress = 100;
                task.CompletionTime = DateTime.UtcNow;
                if (errorMessage != null) task.Message = errorMessage;
                name = task.Name;

                Logger?.LogWarning($"[GlobalProgressManager] ❌ TASK FAILED: '{task.Name}' | Error: {errorMessage}");

                if (_displayedTaskId == taskId)
                    FireEvent(100, errorMessage ?? $"{name} falhou");
            }
            

            TaskStateChanged?.Invoke(this, new TaskStateChangedEventArgs(name ?? "", TaskState.Failed));

            _ = DelayedCleanup(taskId, name ?? "");
        }

        internal void InternalCancel(string taskId)
        {
            lock (_tasksLock) {
                var task = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (task == null || task.IsCompleted) return;

                task.State = TaskState.Cancelled;
                task.CompletionTime = DateTime.UtcNow;
                Logger?.LogInfo($"[GlobalProgressManager] ✕ TASK CANCELLED: '{task.Name}'");

                if (_displayedTaskId == taskId)
                    FireEvent(0, $"{task.Name} cancelado");
            }
            

            TaskStateChanged?.Invoke(this, new TaskStateChangedEventArgs("", TaskState.Cancelled));

            _ = DelayedCleanup(taskId, "");
        }

        public void PauseTask(string taskId)
        {
            lock (_tasksLock) {
                var task = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (task == null || task.IsCompleted) return;
                task.State = TaskState.Paused;
                Logger?.LogInfo($"[GlobalProgressManager] ⏸ TASK PAUSED: '{task.Name}'");
                TaskStateChanged?.Invoke(this, new TaskStateChangedEventArgs(task.Name, TaskState.Paused));
            }
            
        }

        public void ResumeTask(string taskId)
        {
            lock (_tasksLock) {
                var task = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (task == null || task.State != TaskState.Paused) return;
                task.State = TaskState.Running;
                Logger?.LogInfo($"[GlobalProgressManager] ▶ TASK RESUMED: '{task.Name}'");
                TaskStateChanged?.Invoke(this, new TaskStateChangedEventArgs(task.Name, TaskState.Running));
            }
            
        }

        private async Task DelayedCleanup(string taskId, string taskName)
        {
            try
            {
                await Task.Delay(CompletionDisplayMs);

                lock (_tasksLock) {
                    _tasks.RemoveAll(t => t.Id == taskId);
                    Logger?.LogInfo($"[GlobalProgressManager] 🗑 TASK REMOVED: '{taskName}' | Remaining active: {_tasks.Count(t => !t.IsCompleted)}");
                    EvaluateDisplay();
                }
                
            }
            catch { }
        }

        private void EvaluateDisplay()
        {
            var active = _tasks.Where(t => !t.IsCompleted).ToList();

            if (active.Count == 0)
            {
                _displayedTaskId = null;
                FireEvent(0, "");
                Application.Current?.Dispatcher.InvokeAsync(() => OperationCompleted?.Invoke(this, EventArgs.Empty));
                return;
            }

            var top = active.OrderByDescending(t => t.Priority)
                           .ThenByDescending(t => t.StartTime)
                           .First();

            if (_displayedTaskId != top.Id)
            {
                var prevName = _tasks.FirstOrDefault(t => t.Id == _displayedTaskId)?.Name ?? "(none)";
                Logger?.LogInfo($"[GlobalProgressManager] 🔄 AUTO-SWITCH: '{prevName}' → '{top.Name}' (Priority: {top.Priority}, State: {top.State})");
                _displayedTaskId = top.Id;
            }

            FireEvent(top.Progress, top.Message);
        }

        private void FireEvent(int percentage, string? message)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    ProgressChanged?.Invoke(this, new ProgressEventArgs(percentage));
                    if (message != null)
                        StatusChanged?.Invoke(this, message);
                }
                catch { }
            }, System.Windows.Threading.DispatcherPriority.Normal);
        }

        public bool IsOperationRunning
        {
            get
            {
                lock (_tasksLock) { return _tasks.Any(t => !t.IsCompleted); }
                
            }
        }

        public string CurrentOperation
        {
            get
            {
                lock (_tasksLock) {
                    var task = _tasks.FirstOrDefault(t => t.Id == _displayedTaskId);
                    return task?.Name ?? "";
                }
                
            }
        }

        public int ActiveTaskCount
        {
            get
            {
                lock (_tasksLock) { return _tasks.Count(t => !t.IsCompleted); }
                
            }
        }

        public List<ActiveTaskInfo> GetActiveTasks()
        {
            lock (_tasksLock) {
                return _tasks.Where(t => !t.IsCompleted)
                    .Select(t => new ActiveTaskInfo(
                        t.Id, t.Name, t.Page, t.Priority,
                        t.Progress, t.Message, t.State, t.StartTime))
                    .ToList();
            }
            
        }

        public void ResetAll()
        {
            lock (_tasksLock) {
                _tasks.Clear();
                _displayedTaskId = null;
                Logger?.LogInfo("[GlobalProgressManager] 🔄 ALL TASKS RESET");
                FireEvent(0, "");
                Application.Current?.Dispatcher.InvokeAsync(() => OperationCompleted?.Invoke(this, EventArgs.Empty));
            }
            
        }

        public void Dispose() { }
    }

    public class TaskStateChangedEventArgs : EventArgs
    {
        public string TaskName { get; }
        public TaskState NewState { get; }
        public TaskStateChangedEventArgs(string taskName, TaskState newState)
        {
            TaskName = taskName;
            NewState = newState;
        }
    }
}

