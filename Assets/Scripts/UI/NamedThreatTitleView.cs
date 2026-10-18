#nullable enable
using ARPG.Base;
using ARPG.Component;
using ARPG.Message;
using TMPro;
using UnityEngine;

namespace ARPG.UI
{
    /// <summary>
    /// 네임드 위협 칭호 이름표 (NAMED_THREAT_DESIGN §5.6). HP바 캔버스(UICanvas) 루트에 붙어 HP바 위에 칭호 한 줄을 보여 준다.
    /// 존재 시스템이 네임드 엔티티를 만든 직후 이 컴포넌트를 붙이고 RegisterTo를 부른다(다른 엔티티의 캔버스에는 붙지 않는다).
    /// 캔버스째 숨김·위치는 HpBarView와 안개가 처리한다. 접사 이름표(HP바 아래)는 MonsterAffixLabelView가 그대로 맡는다.
    /// </summary>
    public class NamedThreatTitleView : MonoBehaviour, IEntityMessageHandler
    {
        // 캔버스 단위. HP바(140.6) 바로 위
        private const float LabelY = 154f;
        private const float FontSize = 11f;
        private static readonly Vector2 LabelSize = new Vector2(300f, 16f);
        private static readonly Color TitleColor = new Color(1f, 0.6f, 0.25f, 1f);

        private TextMeshProUGUI? _label;

        public void RegisterTo(EntityBase entity)
        {
            if (AR.s.Component.TryGetComponent<NamedThreatComponent>(entity.EntityId, out NamedThreatComponent named) == false ||
                AR.s.NamedThreat == null)
            {
                if (_label != null)
                    _label.gameObject.SetActive(false);
                return;
            }

            string title = AR.s.NamedThreat.GetTitle(named.ThreatId);
            TextMeshProUGUI label = EnsureLabel();
            label.text = title;
            label.gameObject.SetActive(string.IsNullOrEmpty(title) == false);
        }

        private TextMeshProUGUI EnsureLabel()
        {
            if (_label != null)
                return _label;

            GameObject labelObject = new GameObject("NamedThreatTitle");
            labelObject.transform.SetParent(transform, false);

            RectTransform rectTransform = labelObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0f, LabelY);
            rectTransform.sizeDelta = LabelSize;

            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = FontSize;
            label.fontStyle = FontStyles.Bold;
            label.color = TitleColor;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;

            _label = label;
            return label;
        }
    }
}
