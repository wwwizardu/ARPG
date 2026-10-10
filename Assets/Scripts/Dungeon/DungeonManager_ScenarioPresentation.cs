using ARPG.Base;
using ARPG.Component;
using UnityEngine;

namespace ARPG.Dungeon
{
    public partial class DungeonManager
    {
        /// <summary>
        /// 소문 던전의 입구 엔티티에 징후 자막 자식과 판단용 컴포넌트를 붙인다.
        /// 자막 문구는 NPC가 전하는 징후(NarrativeContent.SignText)와 같고, 입구가 있는 동안 계속 쓴다 (NPC_NARRATIVE_CONTENT_DESIGN §3.1).
        /// 표시 핸들러 등록(AutoRegisterChildHandlers)은 호출자가 이어서 한다.
        /// </summary>
        private void AttachEntranceSign(int entityId, EntityBase entity, string dungeonId)
        {
            string text = AR.s.Npc.GetNarrativeSignText(dungeonId);
            if (string.IsNullOrWhiteSpace(text) == true) return;
            var view = new GameObject("Entrance sign");
            view.transform.SetParent(entity.Visual.transform, false);
            view.AddComponent<DungeonEntranceSignView>().Initialize(text);
            _components.AddComponent(entityId, new DungeonEntranceSignComponent());
        }
    }
}
