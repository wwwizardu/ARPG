using ARPG.Base;

namespace ARPG.Message
{
    /// <summary>
    /// 비주얼 컴포넌트(HP바, 이펙트 등)가 구현하는 메시지 핸들러 인터페이스
    /// EntityBase.AutoRegisterChildHandlers()에서 자식 GameObject를 탐색하여 자동 등록
    /// </summary>
    public interface IEntityMessageHandler
    {
        /// <summary>
        /// EntityBase에 자신의 메시지 핸들러를 등록
        /// </summary>
        void RegisterTo(EntityBase entity);
    }
}
