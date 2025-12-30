namespace ARPG
{
    public class SparseSet<T>
    {
        private int[] _sparse;  // Entity ID -> Dense 배열의 인덱스
        private int[] _dense;   // 연속된 Entity ID들
        private T[] _data;      // 연속된 실제 데이터
        private int _count;

        public int Count => _count;
        public int Capacity => _sparse.Length;

        public SparseSet(int capacity = 1000)
        {
            _sparse = new int[capacity];
            _dense = new int[capacity];
            _data = new T[capacity];
            _count = 0;

            // -1로 초기화 (존재하지 않음 표시)
            System.Array.Fill(_sparse, -1);
        }

        // 동적 확장
        private void Resize(int newCapacity)
        {
            int[] newSparse = new int[newCapacity];
            int[] newDense = new int[newCapacity];
            T[] newData = new T[newCapacity];

            // 기존 데이터 복사
            System.Array.Copy(_sparse, newSparse, _sparse.Length);
            System.Array.Copy(_dense, newDense, _count);
            System.Array.Copy(_data, newData, _count);

            // 새로운 영역 초기화
            for (int i = _sparse.Length; i < newCapacity; i++)
            {
                newSparse[i] = -1;
            }

            _sparse = newSparse;
            _dense = newDense;
            _data = newData;
        }

        // Entity ID 유효성 검사
        private bool IsValidEntityId(int entityId)
        {
            return entityId >= 0 && entityId < _sparse.Length;
        }

        // 추가/업데이트: O(1) - 존재하면 업데이트, 없으면 추가
        public void Add(int entityId, T value)
        {
            // 유효하지 않은 Entity ID
            if (entityId < 0)
                return;

            // 동적 확장 필요 시
            if (entityId >= _sparse.Length)
            {
                int newCapacity = System.Math.Max(_sparse.Length * 2, entityId + 1);
                Resize(newCapacity);
            }

            int denseIndex = _sparse[entityId];

            // 이미 존재하는 경우 -> 값만 업데이트
            if (denseIndex != -1 && denseIndex < _count)
            {
                _data[denseIndex] = value;
                return;
            }

            // 존재하지 않으면 추가
            // Dense 배열 확장 필요 시
            if (_count >= _dense.Length)
            {
                Resize(_sparse.Length * 2);
            }

            _dense[_count] = entityId;
            _data[_count] = value;
            _sparse[entityId] = _count;
            _count++;
        }

        // 업데이트: O(1) - 존재하면 업데이트, 없으면 추가
        public void Set(int entityId, T value)
        {
            // 유효하지 않은 Entity ID
            if (entityId < 0)
                return;

            // 동적 확장 필요 시
            if (entityId >= _sparse.Length)
            {
                int newCapacity = System.Math.Max(_sparse.Length * 2, entityId + 1);
                Resize(newCapacity);
            }

            int denseIndex = _sparse[entityId];

            // 이미 존재하는 경우 -> 값만 업데이트
            if (denseIndex != -1 && denseIndex < _count)
            {
                _data[denseIndex] = value;
                return;
            }

            // 존재하지 않으면 추가
            if (_count >= _dense.Length)
            {
                Resize(_sparse.Length * 2);
            }

            _dense[_count] = entityId;
            _data[_count] = value;
            _sparse[entityId] = _count;
            _count++;
        }
    
        // 조회: O(1)
        public T Get(int entityId)
        {
            // 경계 검사
            if (IsValidEntityId(entityId) == false)
                return default(T);

            int denseIndex = _sparse[entityId];

            // 유효성 검증: sparse가 가리키는 인덱스가 실제로 유효한지 확인
            if (denseIndex == -1 || denseIndex >= _count)
                return default(T);

            return _data[denseIndex];
        }

        // 존재 확인: O(1)
        public bool Contains(int entityId)
        {
            // 경계 검사
            if (IsValidEntityId(entityId) == false)
                return false;

            int denseIndex = _sparse[entityId];

            // 유효성 검증: sparse가 가리키는 인덱스가 실제로 유효한지 확인
            return denseIndex != -1 && denseIndex < _count && _dense[denseIndex] == entityId;
        }
    
        // 제거: O(1)
        public void Remove(int entityId)
        {
            // 경계 검사
            if (IsValidEntityId(entityId) == false)
                return;

            int denseIndex = _sparse[entityId];

            // 존재하지 않는 엔티티
            if (denseIndex == -1 || denseIndex >= _count)
                return;

            // Swap-and-pop: 마지막 요소와 교체 후 제거
            int lastIndex = _count - 1;

            // Edge case: 제거하려는 엔티티가 마지막 요소인 경우
            if (denseIndex != lastIndex)
            {
                int lastEntityId = _dense[lastIndex];

                _dense[denseIndex] = lastEntityId;
                _data[denseIndex] = _data[lastIndex];
                _sparse[lastEntityId] = denseIndex;
            }

            _sparse[entityId] = -1;
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
