using System.Collections.Generic;

namespace ARPG
{
    public class SparseSet<T>
    {
        private readonly Dictionary<int, int> _sparse;  // Entity ID -> Dense 배열의 인덱스
        private int[] _dense;   // 연속된 Entity ID들
        private T[] _data;      // 연속된 실제 데이터
        private int _count;

        public int Count => _count;
        public int Capacity => _data.Length;

        public SparseSet(int capacity = 100)
        {
            _sparse = new Dictionary<int, int>();
            _dense = new int[capacity];
            _data = new T[capacity];
            _count = 0;
        }

        // Dense 배열 동적 확장
        private void Resize(int newCapacity)
        {
            int[] newDense = new int[newCapacity];
            T[] newData = new T[newCapacity];

            // 기존 데이터 복사
            System.Array.Copy(_dense, newDense, _count);
            System.Array.Copy(_data, newData, _count);

            _dense = newDense;
            _data = newData;
        }

        // 추가/업데이트: O(1) - 존재하면 업데이트, 없으면 추가
        public void Add(int entityId, T value)
        {
            // Dictionary를 사용하므로 entityId 범위 제한 없음
            if (_sparse.TryGetValue(entityId, out int denseIndex))
            {
                // 이미 존재하는 경우 -> 값만 업데이트
                _data[denseIndex] = value;
                return;
            }

            // 존재하지 않으면 추가
            // Dense 배열 확장 필요 시
            if (_count >= _dense.Length)
            {
                Resize(_dense.Length * 2);
            }

            _dense[_count] = entityId;
            _data[_count] = value;
            _sparse[entityId] = _count;
            _count++;
        }

        // 업데이트: O(1) - 존재하면 업데이트, 없으면 추가
        public void Set(int entityId, T value)
        {
            // Dictionary를 사용하므로 entityId 범위 제한 없음
            if (_sparse.TryGetValue(entityId, out int denseIndex))
            {
                // 이미 존재하는 경우 -> 값만 업데이트
                _data[denseIndex] = value;
                return;
            }

            // 존재하지 않으면 추가
            if (_count >= _dense.Length)
            {
                Resize(_dense.Length * 2);
            }

            _dense[_count] = entityId;
            _data[_count] = value;
            _sparse[entityId] = _count;
            _count++;
        }
    
        // 조회: O(1)
        public T Get(int entityId)
        {
            // Dictionary에서 인덱스 조회
            if (_sparse.TryGetValue(entityId, out int denseIndex) == false)
                return default(T);

            // 유효성 검증: sparse가 가리키는 인덱스가 실제로 유효한지 확인
            if (denseIndex >= _count)
                return default(T);

            return _data[denseIndex];
        }

        // 존재 확인: O(1)
        public bool Contains(int entityId)
        {
            // Dictionary에서 인덱스 조회
            if (_sparse.TryGetValue(entityId, out int denseIndex) == false)
                return false;

            // 유효성 검증: sparse가 가리키는 인덱스가 실제로 유효한지 확인
            return denseIndex < _count && _dense[denseIndex] == entityId;
        }
    
        // 제거: O(1)
        public void Remove(int entityId)
        {
            // Dictionary에서 인덱스 조회
            if (_sparse.TryGetValue(entityId, out int denseIndex) == false)
                return;

            // 존재하지 않는 엔티티
            if (denseIndex >= _count)
                return;

            // Swap-and-pop: 마지막 요소와 교체 후 제거
            int lastIndex = _count - 1;

            // 제거하려는 엔티티가 마지막 요소가 아닌 경우에만 swap 수행
            if (denseIndex != lastIndex)
            {
                int lastEntityId = _dense[lastIndex];

                _dense[denseIndex] = lastEntityId;
                _data[denseIndex] = _data[lastIndex];
                _sparse[lastEntityId] = denseIndex;
            }

            // Dictionary에서 제거
            _sparse.Remove(entityId);
            _count--;
        }
    
        // 순회: 메모리 연속적이라 캐시 효율 좋음!
        public void ForEach(System.Action<T> action)
        {
            for (int i = 0; i < _count; i++)
            {
                action(_data[i]);
            }
        }

        // EntityId와 Component를 함께 순회
        public void ForEach(System.Action<int, T> action)
        {
            for (int i = 0; i < _count; i++)
            {
                int entityId = _dense[i];
                action(entityId, _data[i]);
            }
        }

        // EntityId 가져오기 (인덱스 기반)
        public int GetEntityId(int index)
        {
            if (index < 0 || index >= _count)
                return -1;

            return _dense[index];
        }

        // 인덱스로 데이터 가져오기
        public T GetByIndex(int index)
        {
            if (index < 0 || index >= _count)
                return default;

            return _data[index];
        }
    }
}
