using UnityEngine;

namespace ARPG.Component
{
    public struct ColliderComponent
    {
        // 이동/타일 충돌용 반경 (좁은 통로 통과 기준 — 작게 잡음)
        public float MoveRadius;

        // 피격 반경 (몸통 크기 — 발사체/스킬 명중 판정용)
        public float HitRadius;

        // 발 좌표(TransformComponent.Position)에서 몸통 중심까지의 오프셋
        public Vector2 HitOffset;
    }
}
