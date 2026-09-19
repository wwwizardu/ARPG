#nullable enable
using ARPG.Component;
using ARPG.Skill.Combat;
using ARPG.Skill.Telegraph;
using ARPG.View;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 활성 공격 예고 ECS 데이터만 순회하여 중앙 Combined Mesh View에 제출합니다.
    /// 게임플레이 커밋이나 피해는 변경하지 않습니다.
    /// </summary>
    public sealed class System_AttackTelegraph : IUpdateSystem
    {
        private const float CAMERA_MARGIN = 2f;

        public int Priority => 900;
        public float UpdateInterval => 0f;

        private ComponentManager _componentManager = null!;
        private SparseSet<AttackTelegraphComponent> _telegraphPool = null!;
        private SparseSet<SkillComponent> _skillPool = null!;
        private SparseSet<SkillStateComponent> _skillStatePool = null!;
        private SparseSet<SkillTargetComponent> _skillTargetPool = null!;
        private SparseSet<TransformComponent> _transformPool = null!;
        private SparseSet<JumpComponent> _jumpPool = null!;
        private SparseSet<SkillLandingCommitComponent> _landingCommitPool = null!;
        private AttackTelegraphBatchView _view = null!;
        private Camera? _camera;

        public void OnCreate()
        {
            _componentManager = AR.s.Component;
            _telegraphPool = _componentManager.GetComponentPool<AttackTelegraphComponent>();
            _skillPool = _componentManager.GetComponentPool<SkillComponent>();
            _skillStatePool = _componentManager.GetComponentPool<SkillStateComponent>();
            _skillTargetPool = _componentManager.GetComponentPool<SkillTargetComponent>();
            _transformPool = _componentManager.GetComponentPool<TransformComponent>();
            _jumpPool = _componentManager.GetComponentPool<JumpComponent>();
            _landingCommitPool = _componentManager.GetComponentPool<SkillLandingCommitComponent>();

            _view = AR.s.System.GetComponent<AttackTelegraphBatchView>();
            if (_view == null)
            {
                _view = AR.s.System.gameObject.AddComponent<AttackTelegraphBatchView>();
            }

            Debug.Log("[System_AttackTelegraph] Created");
        }

        public void OnReset()
        {
            _view.ClearBatch();
            Debug.Log("[System_AttackTelegraph] Reset");
        }

        public void OnUpdate(float inDeltaTime)
        {
            _view.BeginFrame();
            ResolveCamera();

            if (_camera == null)
            {
                _view.Render(null, Time.time);
                return;
            }

            Rect expandedCameraRect = GetCameraRect(_camera, CAMERA_MARGIN);
            float playerHitRadius = AttackTelegraphRuntimeGeometryResolver.ResolvePlayerHitRadius(_componentManager);

            for (int i = _telegraphPool.Count - 1; i >= 0; i--)
            {
                int skillEntityId = _telegraphPool.GetEntityId(i);
                AttackTelegraphComponent telegraph = _telegraphPool.GetByIndex(i);

                if (ShouldRemove(skillEntityId, telegraph.OwnerEntityId) == true)
                {
                    _componentManager.RemoveComponent<AttackTelegraphComponent>(skillEntityId);
                    continue;
                }

                if (_skillPool.TryGet(skillEntityId, out SkillComponent skill) == false
                    || skill.Table == null
                    || _skillTargetPool.TryGet(skillEntityId, out SkillTargetComponent target) == false
                    || _transformPool.TryGet(telegraph.OwnerEntityId, out TransformComponent ownerTransform) == false)
                {
                    _componentManager.RemoveComponent<AttackTelegraphComponent>(skillEntityId);
                    continue;
                }

                AttackTelegraphGeometry geometry = AttackTelegraphRuntimeGeometryResolver.Resolve(
                    _componentManager,
                    skillEntityId,
                    skill,
                    playerHitRadius);
                if (geometry.Shape == AttackTelegraphShape.None)
                {
                    _componentManager.RemoveComponent<AttackTelegraphComponent>(skillEntityId);
                    continue;
                }

                telegraph.Shape = geometry.Shape;
                telegraph.OriginMode = geometry.OriginMode;
                telegraph.Range = geometry.Range;
                telegraph.HalfAngleDeg = geometry.HalfAngleDeg;
                telegraph.Width = geometry.Width;
                telegraph.ProjectileCount = geometry.ProjectileCount;
                telegraph.ProjectileSpreadAngleDeg = geometry.ProjectileSpreadAngleDeg;
                if (telegraph.OriginMode != AttackTelegraphOriginMode.LandingPosition)
                {
                    telegraph.TargetPosition = target.TargetPosition;
                }
                telegraph.Direction = AttackTelegraphGeometryResolver.NormalizeOrFallback(
                    target.TargetDirection,
                    telegraph.Direction);

                Vector2 origin = ResolveOrigin(skillEntityId, skill, telegraph, ownerTransform);
                float boundsRadius = GetBoundsRadius(telegraph);
                if (CircleIntersectsRect(origin, boundsRadius, expandedCameraRect) == false)
                {
                    _telegraphPool.SetByIndex(i, telegraph);
                    continue;
                }

                _telegraphPool.SetByIndex(i, telegraph);
                AttackTelegraphRenderData renderData = new(
                    telegraph.Shape,
                    origin,
                    telegraph.Direction,
                    telegraph.Range,
                    telegraph.HalfAngleDeg,
                    telegraph.Width,
                    telegraph.ProjectileCount,
                    telegraph.ProjectileSpreadAngleDeg,
                    telegraph.WarningStartFixedTime,
                    telegraph.NextCommitFixedTime,
                    AttackTelegraphRenderDetail.Full);
                _view.Add(renderData);
            }

            _view.Render(_camera, Time.time);
        }

        private void ResolveCamera()
        {
            if (_camera == null || _camera.isActiveAndEnabled == false || _camera.CompareTag("MainCamera") == false)
            {
                _camera = Camera.main;
            }
        }

        private bool ShouldRemove(int skillEntityId, int ownerEntityId)
        {
            if (_componentManager.HasComponent<DestroyTag>(skillEntityId) == true)
                return true;
            if (_componentManager.HasComponent<MonsterTag>(ownerEntityId) == false)
                return true;
            if (_skillStatePool.TryGet(skillEntityId, out SkillStateComponent skillState) == false
                || skillState.IsRunning == false)
                return true;

            // 오너 측 중단 조건은 System_Skill의 취소와 같은 정본을 쓴다.
            return SkillInterruptRule.Evaluate(_componentManager, ownerEntityId) != SkillInterruptReason.None;
        }


        private Vector2 ResolveOrigin(
            int skillEntityId,
            SkillComponent skill,
            AttackTelegraphComponent telegraph,
            TransformComponent ownerTransform)
        {
            switch (telegraph.OriginMode)
            {
                case AttackTelegraphOriginMode.SnapshotOwner:
                    return telegraph.SnapshotOrigin;

                case AttackTelegraphOriginMode.TargetPosition:
                    return telegraph.TargetPosition;

                case AttackTelegraphOriginMode.LandingPosition:
                    if (_landingCommitPool.TryGet(skillEntityId, out SkillLandingCommitComponent landingCommit) == true)
                        return landingCommit.LandingPosition;
                    // 이 스킬이 띄운 점프의 착지 지점만 쓴다. 다른 점프의 EndPosition을 읽으면 예고가 엉뚱한 곳에 뜬다.
                    if (_jumpPool.TryGet(skill.OwnerEntityId, out JumpComponent jump) == true
                        && jump.SkillEntityId == skillEntityId)
                    {
                        return jump.EndPosition;
                    }
                    return telegraph.TargetPosition;

                default:
                    return AttackTelegraphRuntimeGeometryResolver.ResolveOrigin(
                        _componentManager,
                        skill.OwnerEntityId,
                        telegraph.Shape,
                        ownerTransform.Position);
            }
        }

        private static Rect GetCameraRect(Camera camera, float margin)
        {
            if (camera.orthographic == false)
            {
                Vector3 position = camera.transform.position;
                return new Rect(position.x - 1000f, position.y - 1000f, 2000f, 2000f);
            }

            float halfHeight = camera.orthographicSize + margin;
            float halfWidth = camera.orthographicSize * camera.aspect + margin;
            Vector3 cameraPosition = camera.transform.position;
            return new Rect(
                cameraPosition.x - halfWidth,
                cameraPosition.y - halfHeight,
                halfWidth * 2f,
                halfHeight * 2f);
        }

        private static float GetBoundsRadius(AttackTelegraphComponent telegraph)
        {
            if (telegraph.Shape == AttackTelegraphShape.ProjectileLane
                || telegraph.Shape == AttackTelegraphShape.ProjectileFan)
            {
                return telegraph.Range + telegraph.Width * 0.5f;
            }

            return telegraph.Range;
        }

        private static bool CircleIntersectsRect(Vector2 center, float radius, Rect rect)
        {
            float closestX = Mathf.Clamp(center.x, rect.xMin, rect.xMax);
            float closestY = Mathf.Clamp(center.y, rect.yMin, rect.yMax);
            float deltaX = center.x - closestX;
            float deltaY = center.y - closestY;
            return deltaX * deltaX + deltaY * deltaY <= radius * radius;
        }

    }
}
