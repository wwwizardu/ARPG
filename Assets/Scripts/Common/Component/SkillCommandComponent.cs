#nullable enable
using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 스킬 실행 요청을 전달하는 커맨드 컴포넌트
    /// 캐릭터 엔티티에 붙으며, 어떤 스킬 엔티티를 실행할지 지정
    /// 다른 시스템에서 이 컴포넌트를 설정하면 System_Skill이 처리하고 제거함
    /// 한 프레임여 여러 개 커맨드가 입력된다면 가장 마지막 커맨드가 우선됨
    /// </summary>
    public struct SkillCommandComponent 
    {
        /// <summary>실행할 스킬 엔티티 ID</summary>
        public int SkillEntityId;

        /// <summary>타겟 타입 (엔티티 타겟용)</summary>
        public GlobalEnum.SkillTargetType TargetType;

        /// <summary>타겟 엔티티 ID (엔티티 타겟용)</summary>
        public long TargetId;

        /// <summary>타겟 위치 (위치 타겟용)</summary>
        public Vector2 TargetPosition;

        /// <summary>
        /// 커맨드 초기화
        /// </summary>
        public void Reset()
        {
            SkillEntityId = 0;
            TargetType = 0;
            TargetId = 0;
            TargetPosition = Vector2.zero;
        }
    }
}
