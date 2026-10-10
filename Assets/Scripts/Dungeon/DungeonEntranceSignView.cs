using ARPG.Base;
using ARPG.Message;
using TMPro;
using UnityEngine;

namespace ARPG.Dungeon
{
    /// <summary>
    /// 던전 입구 엔티티의 소문 징후 자막. 판단은 System_DungeonEntranceSign이 하고,
    /// 이 표시는 DungeonEntranceSignMessage를 받은 대로 켜고 끄기만 한다. 엔티티와 함께 생성·제거된다.
    /// 소리·바닥 흔적 연출은 퀄리티를 올릴 때 다시 검토한다.
    /// </summary>
    public sealed class DungeonEntranceSignView : MonoBehaviour, IEntityMessageHandler
    {
        private GameObject _subtitleRoot;

        public void Initialize(string text)
        {
            _subtitleRoot = new GameObject("Entrance subtitle", typeof(Canvas));
            _subtitleRoot.transform.SetParent(transform, false);
            _subtitleRoot.transform.localPosition = new Vector3(0f, 1.2f);
            _subtitleRoot.transform.localScale = Vector3.one * 0.008f;
            var canvas = _subtitleRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 80;
            var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(_subtitleRoot.transform, false);
            label.GetComponent<RectTransform>().sizeDelta = new Vector2(520, 90);
            var tmp = label.GetComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 23; tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.92f, 0.93f, 0.83f); tmp.outlineWidth = 0.2f;
            tmp.raycastTarget = false;
            _subtitleRoot.SetActive(false);
        }

        public void RegisterTo(EntityBase entity)
        {
            entity.RegisterMessageHandler<DungeonEntranceSignMessage>(OnSign);
        }

        private void OnSign(DungeonEntranceSignMessage message)
        {
            if (_subtitleRoot != null) _subtitleRoot.SetActive(message.Active);
        }
    }
}
