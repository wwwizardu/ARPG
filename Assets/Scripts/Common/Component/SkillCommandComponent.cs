#nullable enable
using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 스킬 실행 요청을 전달하는 커맨드 컴포넌트
    /// 다른 시스템에서 이 컴포넌트를 설정하면 System_Skill이 처리
    /// </summary>
    public struct SkillCommandComponent
    {
        /// <summary>스킬 커맨드 타입</summary>
        public SkillCommandType CommandType;

        /// <summary>타겟 타입 (엔티티 타겟용)</summary>
        public byte TargetType;

        /// <summary>타겟 엔티티 ID (엔티티 타겟용)</summary>
        public long TargetId;

        /// <summary>타겟 위치 (위치 타겟용)</summary>
        public Vector2 TargetPosition;

        /// <summary>타겟 방향 (방향 타겟용)</summary>
        public Vector2 TargetDirection;

        /// <summary>커맨드가 처리되었는지 여부 (중복 처리 방지)</summary>
        public bool IsProcessed;

        /// <summary>
        /// 엔티티 타겟 커맨드 설정
        /// </summary>
        public void SetEntityCommand(byte targetType, long targetId)
        {
            CommandType = SkillCommandType.StartWithEntityTarget;
            TargetType = targetType;
            TargetId = targetId;
            IsProcessed = false;
        }

        /// <summary>
        /// 위치 타겟 커맨드 설정
        /// </summary>
        public void SetPositionCommand(Vector2 position)
        {
            CommandType = SkillCommandType.StartWithPositionTarget;
            TargetPosition = position;
            IsProcessed = false;
        }

        /// <summary>
        /// 방향 타겟 커맨드 설정
        /// </summary>
        public void SetDirectionCommand(Vector2 direction)
        {
            CommandType = SkillCommandType.StartWithDirectionTarget;
            TargetDirection = direction.normalized;
            IsProcessed = false;
        }

        /// <summary>
        /// 스킬 중단 커맨드 설정
        /// </summary>
        public void SetStopCommand()
        {
            CommandType = SkillCommandType.Stop;
            IsProcessed = false;
        }

        /// <summary>
        /// 커맨드 초기화
        /// </summary>
        public void Reset()
        {
            CommandType = SkillCommandType.None;
            TargetType = 0;
            TargetId = 0;
            TargetPosition = Vector2.zero;
            TargetDirection = Vector2.zero;
            IsProcessed = false;
        }
    }

    /// <summary>
    /// 스킬 커맨드 타입
    /// </summary>
    public enum SkillCommandType : byte
    {
        /// <summary>커맨드 없음</summary>
        None,
        /// <summary>엔티티 타겟으로 스킬 시작</summary>
        StartWithEntityTarget,
        /// <summary>위치 타겟으로 스킬 시작</summary>
        StartWithPositionTarget,
        /// <summary>방향 타겟으로 스킬 시작</summary>
        StartWithDirectionTarget,
        /// <summary>스킬 중단</summary>
        Stop,
    }
}
