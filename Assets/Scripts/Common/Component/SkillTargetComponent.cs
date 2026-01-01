#nullable enable
using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 스킬의 타겟 정보를 저장하는 컴포넌트
    /// </summary>
    public struct SkillTargetComponent
    {
        /// <summary>타겟 타입 (플레이어, 몬스터, NPC 등)</summary>
        public byte TargetType;

        /// <summary>타겟 엔티티 ID</summary>
        public long TargetId;

        /// <summary>타겟 위치 (위치 기반 스킬용)</summary>
        public Vector2 TargetPosition;

        /// <summary>타겟 방향 (방향 기반 스킬용)</summary>
        public Vector2 TargetDirection;

        /// <summary>
        /// 타겟이 유효한지 여부
        /// </summary>
        public readonly bool HasTarget => TargetId != 0 || TargetPosition != Vector2.zero;

        /// <summary>
        /// 엔티티 타겟 설정
        /// </summary>
        public void SetEntityTarget(byte targetType, long targetId)
        {
            TargetType = targetType;
            TargetId = targetId;
        }

        /// <summary>
        /// 위치 타겟 설정
        /// </summary>
        public void SetPositionTarget(Vector2 position)
        {
            TargetPosition = position;
            TargetId = 0;
        }

        /// <summary>
        /// 방향 타겟 설정
        /// </summary>
        public void SetDirectionTarget(Vector2 direction)
        {
            TargetDirection = direction.normalized;
            TargetId = 0;
        }

        /// <summary>
        /// 타겟 정보 초기화
        /// </summary>
        public void Reset()
        {
            TargetType = 0;
            TargetId = 0;
            TargetPosition = Vector2.zero;
            TargetDirection = Vector2.zero;
        }
    }
}
