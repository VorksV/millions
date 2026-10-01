using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Core
{
    public class VpisEventPipeline
    {
        private readonly ConcurrentQueue<PerformanceInsightEvent> _eventQueue = new ConcurrentQueue<PerformanceInsightEvent>();
        private readonly TimeSpan _deduplicationWindow = TimeSpan.FromSeconds(30);
        private const int EventQueueMaxItems = 5000;
        private const int EventQueueTrimCount = 1000;

        public void PublishEvent(PerformanceInsightEvent newEvent)
        {
            // Deduplication logic: prevent spamming the exact same event category and diagnosis within the window
            var recentEvents = GetEventsInWindow(newEvent.Timestamp - _deduplicationWindow, newEvent.Timestamp);
            
            bool isDuplicate = recentEvents.Any(e => 
                e.Category == newEvent.Category && 
                e.Diagnosis == newEvent.Diagnosis);

            if (!isDuplicate)
            {
                _eventQueue.Enqueue(newEvent);

                // Prevenir crescimento infinito: drenar eventos mais antigos se exceder o limite
                if (_eventQueue.Count > EventQueueMaxItems)
                {
                    for (int i = 0; i < EventQueueTrimCount; i++)
                    {
                        _eventQueue.TryDequeue(out _);
                    }
                }
            }
        }

        public IReadOnlyList<PerformanceInsightEvent> GetSessionEvents()
        {
            return _eventQueue.ToList().AsReadOnly();
        }

        public void ClearSession()
        {
            while (_eventQueue.TryDequeue(out _)) { }
        }

        private IEnumerable<PerformanceInsightEvent> GetEventsInWindow(DateTime startTime, DateTime endTime)
        {
            return _eventQueue.Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime);
        }
    }
}
