#nullable enable
using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 스킬의 타겟 정보를 저장하는 컴포넌트
    /// </summary>
    public struct SkillTargetComponent
    {
        /// <summary>타겟 위치 (위치 기반 스킬용)</summary>
        public Vector2 TargetPosition;

        /// <summary>회전 제한이 적용된 현재 유효 조준 방향. 전투 판정과 방향 View의 정본</summary>
        public Vector2 TargetDirection;

        /// <summary>
        /// 위치 타겟 설정
        /// </summary>
        public void SetPositionTarget(Vector2 position)
        {
            TargetPosition = position;
        }

        /// <summary>
        /// 방향 타겟 설정
        /// </summary>
        public void SetDirectionTarget(Vector2 direction)
        {
            TargetDirection = direction.normalized;
        }

        /// <summary>
        /// 타겟 정보 초기화
        /// </summary>
        public void Reset()
        {
            TargetPosition = Vector2.zero;
            TargetDirection = Vector2.zero;
        }
    }
}
