using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 메시지 처리 시스템
    /// LateUpdate에서 큐에 쌓인 메시지를 일괄 처리
    /// AR.s.Message (MessageManager)에 위임
    /// </summary>
    public class System_EntityMessage : ILateUpdateSystem
    {
        /// <summary>
        /// Priority 900: 다른 시스템 이후 실행
        /// 게임 로직(FixedUpdate)에서 발생한 메시지를 렌더링 전에 처리
        /// </summary>
        public int Priority => 900;

        public void OnCreate()
        {
            Debug.Log("[System_EntityMessage] Created");
        }

        public void OnReset()
        {
            Debug.Log("[System_EntityMessage] Reset");
        }

        public void OnLateUpdate(float inDeltaTime)
        {
            AR.s.Message.ProcessAll();
        }
    }
}
