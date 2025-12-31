using UnityEngine;

namespace ARPG.Component
{
    // Transform 데이터를 저장하는 컴포넌트
    public struct TransformComponent
    {
        public Vector2 Position;  // 렌더링 위치 (Update에서 업데이트)
        public float Rotation;
        public Vector2 Scale;
    }
}
