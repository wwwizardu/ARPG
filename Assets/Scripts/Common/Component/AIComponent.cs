using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// AI 기본 컴포넌트
    /// 모든 AI 엔티티가 가지는 기본 정보
    /// </summary>
    public struct AIComponent
    {
        /// <summary>
        /// AI 테이블 ID (AI 행동 패턴 데이터 참조용)
        /// </summary>
        public int AITableID;

        /// <summary>
        /// 현재 타겟 엔티티 ID (-1이면 타겟 없음)
        /// </summary>
        public int TargetEntityId;

        /// <summary>
        /// 타겟의 마지막으로 알려진 위치
        /// 타겟을 시야에서 잃었을 때 이 위치로 이동
        /// </summary>
        public Vector2 LastKnownTargetPos;
    }
}
