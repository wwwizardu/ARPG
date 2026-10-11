#nullable enable
using System.Text;
using ARPG.Base;
using ARPG.Component;
using ARPG.Message;
using ARPG.Tables;
using TMPro;
using UnityEngine;

namespace ARPG.UI
{
    /// <summary>
    /// 몬스터 접사 이름표 (MONSTER_AFFIX_DESIGN.md §2 A10). HP바 캔버스(UICanvas) 루트에 붙어 HP바 아래에 접사 이름을 보여 준다.
    /// 접사가 없는 엔티티는 글자 오브젝트를 만들지 않는다. 캔버스째 숨김·위치는 HpBarView와 안개가 처리한다.
    /// 이름이 MAX_VISIBLE_NAMES개를 넘으면 앞 4개 + "+N"으로 줄인다.
    /// </summary>
    public class MonsterAffixLabelView : MonoBehaviour, IEntityMessageHandler
    {
        private const int MAX_VISIBLE_NAMES = 4;
        private const string NAME_SEPARATOR = " · ";

        [SerializeField] private float _labelY = 128f;       // 캔버스 단위. HP바(140.6) 바로 아래
        [SerializeField] private float _fontSize = 9f;
        [SerializeField] private Vector2 _labelSize = new Vector2(300f, 14f);

        // 등급 색 (HP바 색과 같은 계열). 일반은 밝은 회색
        private static readonly Color NormalTextColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        private static readonly Color MagicTextColor = new Color(0.55f, 0.65f, 1f, 1f);
        private static readonly Color RareTextColor = new Color(1f, 0.85f, 0.25f, 1f);

        private TextMeshProUGUI? _label;
        private readonly StringBuilder _textBuilder = new StringBuilder(64);

        public void RegisterTo(EntityBase entity)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<MonsterAffixComponent>(entity.EntityId, out MonsterAffixComponent affixes) == false ||
                affixes.Count <= 0)
            {
                if (_label != null)
                    _label.gameObject.SetActive(false);
                return;
            }

            GlobalEnum.MonsterArchetype archetype = cm.TryGetComponent<MonsterArchetypeComponent>(entity.EntityId,
                out MonsterArchetypeComponent grade) == true
                ? grade.Archetype
                : GlobalEnum.MonsterArchetype.Normal;

            TextMeshProUGUI label = EnsureLabel();
            label.text = BuildText(affixes);
            label.color = GetTextColor(archetype);
            label.gameObject.SetActive(true);
        }

        private TextMeshProUGUI EnsureLabel()
        {
            if (_label != null)
                return _label;

            GameObject labelObject = new GameObject("AffixLabel");
            labelObject.transform.SetParent(transform, false);

            RectTransform rectTransform = labelObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0f, _labelY);
            rectTransform.sizeDelta = _labelSize;

            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = _fontSize;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;

            _label = label;
            return label;
        }

        private string BuildText(MonsterAffixComponent affixes)
        {
            _textBuilder.Clear();
            int shown = 0;
            for (int i = 0; i < affixes.Count; i++)
            {
                MonsterAffixTable? affix = AR.s.Data.GetMonsterAffix(affixes.GetByIndex(i));
                if (affix == null)
                    continue;

                if (shown >= MAX_VISIBLE_NAMES)
                {
                    _textBuilder.Append(" +").Append(affixes.Count - shown);
                    break;
                }

                if (shown > 0)
                    _textBuilder.Append(NAME_SEPARATOR);
                _textBuilder.Append(affix.Name);
                shown++;
            }

            return _textBuilder.ToString();
        }

        private static Color GetTextColor(GlobalEnum.MonsterArchetype archetype)
        {
            switch (archetype)
            {
                case GlobalEnum.MonsterArchetype.Magic: return MagicTextColor;
                case GlobalEnum.MonsterArchetype.Rare: return RareTextColor;
                default: return NormalTextColor;
            }
        }
    }
}
