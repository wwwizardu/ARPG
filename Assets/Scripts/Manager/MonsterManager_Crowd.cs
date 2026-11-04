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

        private void InitializeCrowd()
        {
            _crowdGrid.Initialize();
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

        // 기본 이동(ResolveAxisSeparated)처럼 벽을 따라 미끄러지고 충돌 내부에서 빠져나온다.
        // 막힌 축이 있으면 도착점이 목표와 달라지므로 경로 앞보기의 직선 확인에도 그대로 쓴다.
        private static Vector2 SweepCrowdTerrain(Vector2 position, Vector2 delta, float radius)
        {
            return CollisionUtil.SlideTrajectory(position, position + delta, radius);
        }
    }
}
