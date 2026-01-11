#nullable enable

namespace ARPG.Utility
{
    /// <summary>
    /// 버프 엔티티 ID를 생성하고 파싱하는 헬퍼 클래스
    ///
    /// ID 생성 방식: targetEntityId + (buffTableID + 1) * BUFF_TABLE_OFFSET + instanceIndex * BUFF_INSTANCE_OFFSET
    /// 예시:
    /// - Target Entity ID: 12345
    /// - Buff Table ID: 1001 (공격력 버프)
    /// - Instance 0: 12345 + 1002*100000 + 0*10 = 100212345
    /// - Instance 1: 12345 + 1002*100000 + 1*10 = 100212355 (같은 버프 중복 적용 시)
    ///
    /// 이렇게 하면 ID만 봐도 어떤 타겟의 어떤 버프인지 파악 가능
    /// </summary>
    public static class BuffEntityIdHelper
    {
        /// <summary>
        /// 버프 테이블 오프셋 (100,000)
        /// 각 버프 타입마다 충분한 공간 확보
        /// </summary>
        private const int BUFF_TABLE_OFFSET = 100000;

        /// <summary>
        /// 버프 인스턴스 오프셋 (10)
        /// 같은 타입의 버프가 최대 9999개까지 중복 가능 (실제로는 훨씬 적게 사용)
        /// </summary>
        private const int BUFF_INSTANCE_OFFSET = 10;

        /// <summary>
        /// 최대 버프 테이블 ID (안전성을 위한 상한선)
        /// </summary>
        public const int MAX_BUFF_TABLE_ID = 9999;

        /// <summary>
        /// 같은 타입 버프의 최대 인스턴스 수
        /// </summary>
        public const int MAX_BUFF_INSTANCES_PER_TYPE = 9999;

        /// <summary>
        /// 타겟 엔티티 ID, 버프 테이블 ID, 인스턴스 인덱스로 버프 엔티티 ID 계산
        /// </summary>
        /// <param name="targetEntityId">버프를 받는 타겟 엔티티 ID</param>
        /// <param name="buffTableID">버프 테이블 ID</param>
        /// <param name="instanceIndex">같은 타입 버프의 인스턴스 인덱스 (0부터 시작)</param>
        /// <returns>버프 엔티티 ID</returns>
        public static int GetBuffEntityId(int targetEntityId, int buffTableID, int instanceIndex)
        {
            if (buffTableID < 0 || buffTableID > MAX_BUFF_TABLE_ID)
            {
                UnityEngine.Debug.LogError($"[BuffEntityIdHelper] Invalid buff table ID: {buffTableID}. Must be between 0 and {MAX_BUFF_TABLE_ID}");
                return -1;
            }

            if (instanceIndex < 0 || instanceIndex >= MAX_BUFF_INSTANCES_PER_TYPE)
            {
                UnityEngine.Debug.LogError($"[BuffEntityIdHelper] Invalid instance index: {instanceIndex}. Must be between 0 and {MAX_BUFF_INSTANCES_PER_TYPE - 1}");
                return -1;
            }

            return targetEntityId + (buffTableID + 1) * BUFF_TABLE_OFFSET + instanceIndex * BUFF_INSTANCE_OFFSET;
        }

        /// <summary>
        /// 버프 엔티티 ID에서 타겟 엔티티 ID 추출
        /// </summary>
        /// <param name="buffEntityId">버프 엔티티 ID</param>
        /// <returns>타겟 엔티티 ID</returns>
        public static int GetTargetEntityId(int buffEntityId)
        {
            // 인스턴스 오프셋 제거 후 테이블 오프셋으로 나눈 나머지
            int withoutInstance = buffEntityId - (buffEntityId / BUFF_INSTANCE_OFFSET % BUFF_INSTANCE_OFFSET) * BUFF_INSTANCE_OFFSET;
            return withoutInstance % BUFF_TABLE_OFFSET;
        }

        /// <summary>
        /// 버프 엔티티 ID에서 버프 테이블 ID 추출
        /// </summary>
        /// <param name="buffEntityId">버프 엔티티 ID</param>
        /// <returns>버프 테이블 ID</returns>
        public static int GetBuffTableID(int buffEntityId)
        {
            // 인스턴스 오프셋 제거
            int withoutInstance = buffEntityId - (buffEntityId % BUFF_INSTANCE_OFFSET);
            return (withoutInstance / BUFF_TABLE_OFFSET) - 1;
        }

        /// <summary>
        /// 버프 엔티티 ID에서 인스턴스 인덱스 추출
        /// </summary>
        /// <param name="buffEntityId">버프 엔티티 ID</param>
        /// <returns>인스턴스 인덱스</returns>
        public static int GetInstanceIndex(int buffEntityId)
        {
            return (buffEntityId % BUFF_TABLE_OFFSET) / BUFF_INSTANCE_OFFSET;
        }

        /// <summary>
        /// 버프 엔티티 ID가 유효한지 확인
        /// </summary>
        /// <param name="buffEntityId">버프 엔티티 ID</param>
        /// <returns>유효하면 true</returns>
        public static bool IsValidBuffEntityId(int buffEntityId)
        {
            int tableId = GetBuffTableID(buffEntityId);
            int instanceIndex = GetInstanceIndex(buffEntityId);
            return tableId >= 0 && tableId <= MAX_BUFF_TABLE_ID && instanceIndex >= 0 && instanceIndex < MAX_BUFF_INSTANCES_PER_TYPE;
        }

        /// <summary>
        /// 디버그용: 버프 엔티티 ID 정보를 문자열로 반환
        /// </summary>
        public static string GetDebugString(int buffEntityId)
        {
            int targetId = GetTargetEntityId(buffEntityId);
            int tableId = GetBuffTableID(buffEntityId);
            int instanceIndex = GetInstanceIndex(buffEntityId);
            return $"BuffEntityId={buffEntityId} (TargetId={targetId}, TableId={tableId}, InstanceIndex={instanceIndex})";
        }
    }
}
