#nullable enable
using ARPG.Component;

namespace ARPG.Systems
{
    /// <summary>
    /// 관계 패시브 변동 시스템
    /// RelationshipComponent 풀을 순회하며 시간 경과에 따른 관계 변동 처리
    /// </summary>
    public class System_Relationship : IFixedUpdateSystem
    {
        public int Priority => 58;
        public float UpdateInterval => 3.0f;

        public void OnCreate() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<RelationshipComponent> pool = cm.GetComponentPool<RelationshipComponent>();

            for (int i = 0; i < pool.Count; i++)
            {
                RelationshipComponent rel = pool.GetByIndex(i);

                // TODO: 패시브 관계 변동 로직 구현
                // 예: 시간 경과에 따른 호감도 자연 감소/증가, 신뢰도 정규화 등

                pool.SetByIndex(i, rel);
            }
        }

        public void OnReset() { }
    }
}
