#nullable enable
using ARPG.Base;
using ARPG.Component;
using ARPG.Map;

namespace ARPG.Systems
{
    /// <summary>
    /// 시야 밖 이동 엔티티(몬스터·NPC·적 투사체)의 Visual을 숨긴다 (FOG_OF_WAR_DESIGN §5).
    /// FogVisibilityComponent 풀만 돌며 칸 조회·거리 비교로 판정하고, 상태가 바뀐 엔티티에만 적용한다.
    /// 진영이 Player면(동행 NPC) 항상 보인다. 루트 GameObject는 System_EntityActivation·청크 비활성화가 쓰므로 건드리지 않는다.
    /// 투사체는 MessageManager에 등록되지 않아 메시지가 닿지 않으므로, System_Render·SuspendExceptPlayer처럼
    /// EntityBase를 찾아 Visual을 직접 켜고 끈다. 같은 LateUpdate 안이라 이번 프레임 그리기 전에 반영된다.
    /// </summary>
    public sealed class System_FogEntityVisibility : ILateUpdateSystem
    {
        public int Priority => 890;
        public float UpdateInterval => 0f;

        private SparseSet<FogVisibilityComponent> _visibilityPool = null!;
        private SparseSet<TransformComponent> _transformPool = null!;
        private SparseSet<FactionComponent> _factionPool = null!;
        private System_Render? _render;

        public void OnCreate()
        {
            ComponentManager cm = AR.s.Component;
            _visibilityPool = cm.GetComponentPool<FogVisibilityComponent>();
            _transformPool = cm.GetComponentPool<TransformComponent>();
            _factionPool = cm.GetComponentPool<FactionComponent>();
        }

        public void OnReset() { }

        public void OnLateUpdate(float deltaTime)
        {
            if (_visibilityPool.Count == 0) return;

            FogOfWarManager fog = AR.s.Fog;
            bool fogActive = fog.IsActive;
            // 맵 전환 직후 시야를 계산하기 전에는 화면이 어둡게 덮여 있다. 상태를 바꾸지 않고 다음 계산을 기다린다.
            if (fogActive == true && fog.HasVision == false) return;

            for (int i = 0; i < _visibilityPool.Count; i++)
            {
                int entityId = _visibilityPool.GetEntityId(i);
                FogVisibilityComponent state = _visibilityPool.GetByIndex(i);
                bool visible = true;
                if (fogActive == true &&
                    (_factionPool.TryGet(entityId, out FactionComponent faction) == false || faction.FactionId != Faction.Player))
                {
                    if (_transformPool.TryGet(entityId, out TransformComponent transform) == false) continue;
                    visible = fog.IsEntityVisible(transform.Position, state.IsVisible);
                }

                if (visible == state.IsVisible) continue;
                // 투사체 프리팹이 아직 로드 중이면 다음 프레임에 다시 시도한다. 로드 뒤에는 ProjectileHelper가 현재 상태를 적용한다.
                if (ApplyVisual(entityId, visible) == false) continue;

                state.IsVisible = visible;
                _visibilityPool.SetByIndex(i, state);
            }
        }

        private bool ApplyVisual(int entityId, bool visible)
        {
            if (AR.s.Message.TryGetEntity(entityId, out EntityBase? entity) == false)
            {
                _render ??= AR.s.System.GetSystem<System_Render>();
                entity = _render?.GetEntity(entityId);
            }
            if (entity == null || entity.Visual == null) return false;

            if (entity.Visual.activeSelf != visible) entity.Visual.SetActive(visible);
            return true;
        }
    }
}
