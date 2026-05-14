using System;
using System.Collections.Generic;

namespace ARPG
{
    /// <summary>
    /// Common component pool interface.
    /// Allows removing/checking an entity without knowing the component type.
    /// </summary>
    public interface IComponentPool
    {
        void Remove(int entityId);
        bool Contains(int entityId);
    }

    public class SparseSet<T> : IComponentPool
    {
        private const int DefaultSparseCapacity = 1024;
        private const int MaxDirectEntityId = 65536;

        // Direct sparse path: entityId -> denseIndex + 1. Zero means missing.
        private int[] _sparse;

        // Large/deterministic IDs stay here so they do not force huge sparse arrays.
        private readonly Dictionary<int, int> _fallbackSparse;

        private int[] _dense;
        private T[] _data;
        private int _count;

        public int Count => _count;
        public int Capacity => _data.Length;

        public SparseSet(int capacity = 100)
        {
            int denseCapacity = Math.Max(1, capacity);
            int sparseCapacity = Math.Min(MaxDirectEntityId, Math.Max(DefaultSparseCapacity, denseCapacity));

            _sparse = new int[sparseCapacity];
            _fallbackSparse = new Dictionary<int, int>();
            _dense = new int[denseCapacity];
            _data = new T[denseCapacity];
            _count = 0;
        }

        private static bool CanUseDirectSparse(int entityId)
        {
            return entityId >= 0 && entityId < MaxDirectEntityId;
        }

        private void EnsureDenseCapacity()
        {
            if (_count < _dense.Length)
                return;

            int newCapacity = _dense.Length > 0 ? _dense.Length * 2 : 1;
            Resize(newCapacity);
        }

        private void EnsureSparseCapacity(int entityId)
        {
            if (entityId < _sparse.Length)
                return;

            int newCapacity = _sparse.Length > 0 ? _sparse.Length : DefaultSparseCapacity;
            while (newCapacity <= entityId && newCapacity < MaxDirectEntityId)
            {
                newCapacity *= 2;
            }

            if (newCapacity > MaxDirectEntityId)
            {
                newCapacity = MaxDirectEntityId;
            }

            if (newCapacity > _sparse.Length)
            {
                Array.Resize(ref _sparse, newCapacity);
            }
        }

        private bool TryGetDenseIndex(int entityId, out int denseIndex)
        {
            if (CanUseDirectSparse(entityId))
            {
                if (entityId >= _sparse.Length)
                {
                    denseIndex = -1;
                    return false;
                }

                int storedIndex = _sparse[entityId];
                if (storedIndex == 0)
                {
                    denseIndex = -1;
                    return false;
                }

                denseIndex = storedIndex - 1;
                return denseIndex >= 0 && denseIndex < _count && _dense[denseIndex] == entityId;
            }

            if (_fallbackSparse.TryGetValue(entityId, out denseIndex) == false)
            {
                denseIndex = -1;
                return false;
            }

            return denseIndex >= 0 && denseIndex < _count && _dense[denseIndex] == entityId;
        }

        private void SetSparseIndex(int entityId, int denseIndex)
        {
            if (CanUseDirectSparse(entityId))
            {
                EnsureSparseCapacity(entityId);
                _sparse[entityId] = denseIndex + 1;
                return;
            }

            _fallbackSparse[entityId] = denseIndex;
        }

        private void RemoveSparseIndex(int entityId)
        {
            if (CanUseDirectSparse(entityId))
            {
                if (entityId < _sparse.Length)
                {
                    _sparse[entityId] = 0;
                }
                return;
            }

            _fallbackSparse.Remove(entityId);
        }

        private void Resize(int newCapacity)
        {
            int[] newDense = new int[newCapacity];
            T[] newData = new T[newCapacity];

            Array.Copy(_dense, newDense, _count);
            Array.Copy(_data, newData, _count);

            _dense = newDense;
            _data = newData;
        }

        public void Add(int entityId, T value)
        {
            AddOrSet(entityId, value);
        }

        public void Set(int entityId, T value)
        {
            AddOrSet(entityId, value);
        }

        private void AddOrSet(int entityId, T value)
        {
            if (TryGetDenseIndex(entityId, out int denseIndex))
            {
                _data[denseIndex] = value;
                return;
            }

            EnsureDenseCapacity();

            int newIndex = _count;
            _dense[newIndex] = entityId;
            _data[newIndex] = value;
            SetSparseIndex(entityId, newIndex);
            _count++;
        }

        public T Get(int entityId)
        {
            if (TryGetDenseIndex(entityId, out int denseIndex) == false)
                return default;

            return _data[denseIndex];
        }

        public bool Contains(int entityId)
        {
            return TryGetDenseIndex(entityId, out _);
        }

        public bool TryGet(int entityId, out T value)
        {
            if (TryGetDenseIndex(entityId, out int denseIndex) == false)
            {
                value = default;
                return false;
            }

            value = _data[denseIndex];
            return true;
        }

        public void Remove(int entityId)
        {
            if (TryGetDenseIndex(entityId, out int denseIndex) == false)
                return;

            int lastIndex = _count - 1;

            if (denseIndex != lastIndex)
            {
                int lastEntityId = _dense[lastIndex];

                _dense[denseIndex] = lastEntityId;
                _data[denseIndex] = _data[lastIndex];
                SetSparseIndex(lastEntityId, denseIndex);
            }

            RemoveSparseIndex(entityId);
            _dense[lastIndex] = 0;
            _data[lastIndex] = default;
            _count--;
        }

        public void ForEach(Action<T> action)
        {
            for (int i = 0; i < _count; i++)
            {
                action(_data[i]);
            }
        }

        public void ForEach(Action<int, T> action)
        {
            for (int i = 0; i < _count; i++)
            {
                int entityId = _dense[i];
                action(entityId, _data[i]);
            }
        }

        public int GetEntityId(int index)
        {
            if (index < 0 || index >= _count)
                return -1;

            return _dense[index];
        }

        public T GetByIndex(int index)
        {
            if (index < 0 || index >= _count)
                return default;

            return _data[index];
        }

        public void SetByIndex(int index, T value)
        {
            if (index < 0 || index >= _count)
                return;

            _data[index] = value;
        }
    }
}
