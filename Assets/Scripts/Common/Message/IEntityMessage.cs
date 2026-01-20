namespace ARPG.Message
{
    /// <summary>
    /// 엔티티 메시지 인터페이스
    /// 모든 메시지 타입은 이 인터페이스를 구현해야 함
    /// </summary>
    public interface IEntityMessage
    {
        /// <summary>
        /// 메시지를 받을 대상 엔티티 ID
        /// </summary>
        int TargetEntityId { get; }
    }
}
