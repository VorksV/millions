using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Core.Intelligence;

public class CircularBuffer<T>
{
	private readonly T[] _buffer;

	private int _head;

	private int _tail;

	private int _count;

	public int Capacity => _buffer.Length;

	public int Count => _count;

	public CircularBuffer(int capacity)
	{
		if (capacity <= 0)
		{
			throw new ArgumentOutOfRangeException("capacity", "A capacidade deve ser maior que zero.");
		}
		_buffer = new T[capacity];
		_head = 0;
		_tail = 0;
		_count = 0;
	}

	public void Add(T item)
	{
		_buffer[_tail] = item;
		_tail = (_tail + 1) % Capacity;
		if (_count < Capacity)
		{
			_count++;
		}
		else
		{
			_head = (_head + 1) % Capacity;
		}
	}

	public IEnumerable<T> GetItems()
	{
		for (int i = 0; i < _count; i++)
		{
			yield return _buffer[(_head + i) % Capacity];
		}
	}

	public T[] ToArray()
	{
		T[] array = new T[_count];
		for (int i = 0; i < _count; i++)
		{
			array[i] = _buffer[(_head + i) % Capacity];
		}
		return array;
	}

	public void Clear()
	{
		_head = 0;
		_tail = 0;
		_count = 0;
		Array.Clear(_buffer, 0, _buffer.Length);
	}
}
