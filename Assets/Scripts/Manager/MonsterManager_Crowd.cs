using System;
using ARPG.Component;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Monster
{
    public partial class MonsterManager
    {
        [Header("Monster Crowd Movement")]
        [SerializeField] private MonsterCrowdSettings _crowdSettings = new();
        private readonly MonsterCrowdGrid _crowdGrid = new();
        private MonsterTerrainSweep _crowdTerrainSweep;
        private Func<int, bool> _crowdActivePredicate;

        public MonsterCrowdSettings CrowdSettings => _crowdSettings;
        public MonsterCrowdGrid CrowdGrid => _crowdGrid;
        public MonsterTerrainSweep CrowdTerrainSweep => _crowdTerrainSweep;
        public Func<int, bool> CrowdActivePredicate => _crowdActivePredicate;

        /// <summary>개체별 참여 전환. 태그는 이동과 점유 양쪽에서 제외한다.</summary>
        public void SetCrowdParticipation(int entityId, bool enabled)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.HasComponent<MonsterCrowdComponent>(entityId) == false)
                return;
            if (enabled == true)
                cm.RemoveComponent<MonsterCrowdExcludedTag>(entityId);
            else
                cm.AddComponent(entityId, new MonsterCrowdExcludedTag());
            if (cm.TryGetComponent<MonsterCrowdComponent>(entityId, out var crowd) == true)
            {
                crowd.IsWaiting = false;
                crowd.RetryAt = 0f;
                crowd.AvoidSide = 0;
                crowd.AvoidUntil = 0f;
                cm.SetComponent(entityId, crowd);
            }
        }

        private void InitializeCrowd()
        {
            _crowdGrid.Initialize(_crowdSettings.GridWidth, _crowdSettings.CellSize);
            _crowdTerrainSweep = SweepCrowdTerrain;
            _crowdActivePredicate = IsCrowdEntityActive;
        }

        private bool IsCrowdEntityActive(int entityId)
        {
            if (AR.s.Message.TryGetEntity(entityId, out var entity) == false || entity == null
                || entity.gameObject.activeInHierarchy == false)
                return false;
            ComponentManager cm = AR.s.Component;
            return cm.TryGetComponent<TransformComponent>(entityId, out var transform) == true
                && AR.s.Map.IsChunkActiveAtWorld(transform.Position) == true;
        }

        private static Vector2 SweepCrowdTerrain(Vector2 position, Vector2 delta, float radius)
        {
            return CollisionUtil.ClipTrajectory(position, position + delta, radius);
        }
    }
}
