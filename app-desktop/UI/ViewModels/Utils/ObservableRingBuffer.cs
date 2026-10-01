using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace VoltrisOptimizer.UI.ViewModels.Utils
{
    /// <summary>
    /// Ring buffer focado em performance para logs. 
    /// Dispara eventos agrupados para não travar a UI (Backpressure mitigation).
    /// </summary>
    public class ObservableRingBuffer<T> : INotifyCollectionChanged, IEnumerable<T>
    {
        private readonly T[] _buffer;
        private int _head;
        private int _tail;
        private int _count;
        private readonly int _capacity;

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public ObservableRingBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentException("Capacity must be > 0");
            _capacity = capacity;
            _buffer = new T[capacity];
        }

        public void Add(T item)
        {
            if (_count == _capacity)
                _head = (_head + 1) % _capacity;
            else
                _count++;

            _buffer[_tail] = item;
            _tail = (_tail + 1) % _capacity;

            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) return;
            
            bool added = false;
            foreach (var item in items)
            {
                added = true;
                if (_count == _capacity)
                    _head = (_head + 1) % _capacity;
                else
                    _count++;

                _buffer[_tail] = item;
                _tail = (_tail + 1) % _capacity;
            }

            if (added)
            {
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        public void Clear()
        {
            _head = 0;
            _tail = 0;
            _count = 0;
            Array.Clear(_buffer, 0, _capacity);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public int Count => _count;
        public int Capacity => _capacity;

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return _buffer[(_head + i) % _capacity];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
