using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 버프 업데이트 시스템
    /// 모든 버프의 남은 시간을 감소시키고, 만료된 버프를 제거합니다.
    ///
    /// 실행 흐름:
    /// 1. BuffInstance 컴포넌트를 가진 모든 Entity 순회
    /// 2. RemainTime 감소
    /// 3. RemainTime이 0 이하가 되면 버프 제거
    /// </summary>
    public partial struct System_BuffUpdate : IUpdateSystem
    {
        public int Priority => 40;  // 스킬(200) 이전, 입력(0) 이후 실행

        public void OnCreate()
        {
            Debug.Log("[System_BuffUpdate] Created");
        }

        public void OnReset()
        {
            Debug.Log("[System_BuffUpdate] Reset called");
        }

        public readonly void OnUpdate(float inDeltaTime)
        {
            // BuffInstance 컴포넌트 풀 가져오기
            SparseSet<BuffInstance> buffPool = AR.s.Component.GetComponentPool<BuffInstance>();
            if (buffPool == null || buffPool.Count == 0)
                return;

            // 역순으로 순회 (제거 중에도 안전)
            for (int i = buffPool.Count - 1; i >= 0; i--)
            {
                int buffEntityId = buffPool.GetEntityId(i);
                BuffInstance buff = buffPool.GetByIndex(i);

                // 남은 시간 감소
                buff.RemainTime -= inDeltaTime;

                // 버프 만료 확인
                if (buff.RemainTime <= 0f)
                {
                    // 버프 제거 (BuffSystem이 타겟의 버프 리스트도 정리해줌)
                    BuffSystem.RemoveBuff(buffEntityId);
                }
                else
                {
                    // 시간만 업데이트
                    AR.s.Component.SetComponent(buffEntityId, buff);
                }
            }
        }
    }
}
