using ARPG.Component;
using ARPG.Message;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 던전 입구의 소문 징후 자막을 켤지 판단한다. 플레이어가 8칸 안에 있으면 켠다. 자막은 소문 던전의 입구가 있는 동안 늘 뜨고,
    /// 대상 몬스터를 잡아도 꺼지지 않는다. 던전이 닫히면 입구와 함께 사라진다 (NPC_NARRATIVE_CONTENT_DESIGN §3.1).
    /// 상태가 바뀔 때만 DungeonEntranceSignMessage를 보내며, 표시(DungeonEntranceSignView)는 받은 대로만 적용한다.
    /// 월드 맵 시스템이라 던전 안에서는 멈춘다. 입구 엔티티도 그때는 청크 해제로 사라진다.
    /// </summary>
    public sealed class System_DungeonEntranceSign : IUpdateSystem, IWorldMapSystem
    {
        private const float NEAR_DISTANCE_SQR = 64f;

        public int Priority => 520;
        public float UpdateInterval => 0.2f;

        public void OnCreate() { }
        public void OnReset() { }

        public void OnUpdate(float deltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<DungeonEntranceSignComponent> signPool = cm.GetComponentPool<DungeonEntranceSignComponent>();
            if (signPool.Count == 0) return;
            if (cm.TryGetComponent<TransformComponent>(AR.s.Data.CurrentPlayerEntityId, out var player) == false) return;

            for (int i = 0; i < signPool.Count; i++)
            {
                int entityId = signPool.GetEntityId(i);
                DungeonEntranceSignComponent sign = signPool.GetByIndex(i);
                if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false ||
                    cm.HasComponent<DungeonEntranceComponent>(entityId) == false) continue;

                bool active = Vector2.SqrMagnitude(player.Position - transform.Position) <= NEAR_DISTANCE_SQR;
                if (active == sign.Active) continue;

                sign.Active = active;
                signPool.SetByIndex(i, sign);
                AR.s.Message.SendToEntity(new DungeonEntranceSignMessage { TargetEntityId = entityId, Active = active });
            }
        }
    }
}
