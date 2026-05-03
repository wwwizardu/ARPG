#nullable enable
using ARPG.Data;
using ARPG.Tables;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UIElements;

namespace ARPG.Manager
{
    /// <summary>
    /// 글로벌 UI Toolkit 툴팁 매니저 (AR.s.Tooltip).
    /// ItemData를 받아 ItemType에 따라 자동 분기하여 컨텐츠 빌드.
    /// 호출자는 어떤 종류의 아이템이든 동일한 Show(item, screenPos) 한 줄로 사용.
    ///
    /// 데이터 타입 추가 시: Tooltip.uxml에 content-* 컨테이너 추가 → BuildXxxContent 메서드 + Show()의 switch 분기.
    /// </summary>
    public class TooltipManager : MonoBehaviour
    {
        // Addressable 키 (사용자 작업: TooltipUIT.prefab 등록)
        private const string TOOLTIP_PREFAB_KEY = "UI/TooltipUIT";

        private static readonly string[] TIER_CLASSES =
        {
            "tooltip-tier-1", "tooltip-tier-2", "tooltip-tier-3"
        };

        private GameObject? _tooltipObj;
        private UIDocument? _document;
        private VisualElement? _root;
        private VisualElement? _frame;

        // 데이터 타입별 컨테이너
        private VisualElement? _contentSkillBook;
        private VisualElement? _contentEquipment;
        private VisualElement? _contentItem;

        // SkillBook 컨텐츠 element 캐시
        private Label? _sbName;
        private Label? _sbTier;
        private Label? _sbSkillName;
        private Label? _sbDesc;
        private Label? _sbStatDamage;
        private Label? _sbStatCooltime;
        private Label? _sbStatMana;
        private Label? _sbStatRange;

        private bool _isReady = false;

        public void Initialize()
        {
            LoadAsync().Forget();
        }

        public void Reset()
        {
            Hide();
        }

        public void Show(ItemData? item, Vector2 screenPos)
        {
            if (_isReady == false || _frame == null) return;
            if (item == null || item.Table == null)
            {
                Hide();
                return;
            }

            VisualElement? activeContent = null;

            switch (item.Table.ItemType)
            {
                case GlobalEnum.ItemType.SkillBook:
                    if (item.SkillBook != null)
                    {
                        BuildSkillBookContent(item);
                        activeContent = _contentSkillBook;
                    }
                    break;
                // Phase 2: Equipment / 일반 아이템 컨텐츠 빌더 추가
                default:
                    break;
            }

            if (activeContent == null)
            {
                // 아직 지원하지 않는 ItemType (uGUI 툴팁이 담당 중) — 툴팁 표시 안 함
                Hide();
                return;
            }

            ShowContent(activeContent);
            _frame.style.display = DisplayStyle.Flex;
            UpdatePosition(screenPos);
        }

        public void Hide()
        {
            if (_frame == null) return;
            _frame.style.display = DisplayStyle.None;
        }

        public void UpdatePosition(Vector2 screenPos)
        {
            if (_isReady == false || _frame == null || _root == null) return;
            if (_frame.style.display == DisplayStyle.None) return;

            IPanel? panel = _root.panel;
            if (panel == null) return;

            // Input.mousePosition은 좌하단 원점(Y up). RuntimePanelUtils.ScreenToPanel은
            // 좌상단 원점(Y down)을 기대하므로 Y 반전 후 전달.
            Vector2 flipped = new Vector2(screenPos.x, Screen.height - screenPos.y);
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, flipped);

            const float offsetX = 18f;
            const float offsetY = 12f;

            float panelW = _root.resolvedStyle.width;
            float panelH = _root.resolvedStyle.height;
            float ttW = _frame.resolvedStyle.width;
            float ttH = _frame.resolvedStyle.height;

            float x = panelPos.x + offsetX;
            float y = panelPos.y + offsetY;

            // 화면 밖으로 나가면 마우스 반대편으로
            if (x + ttW > panelW) x = panelPos.x - ttW - offsetX;
            if (y + ttH > panelH) y = panelPos.y - ttH - offsetY;
            if (x < 0f) x = 0f;
            if (y < 0f) y = 0f;

            _frame.style.left = x;
            _frame.style.top = y;
        }

        // ========== 컨텐츠 빌더 ==========

        private void BuildSkillBookContent(ItemData book)
        {
            string itemName = book.Table?.Name ?? "스킬북";
            int tier = book.Table?.Tier ?? 0;
            SkillTable? skill = book.SkillBook?.Table;

            if (_sbName != null)
            {
                _sbName.text = itemName;
                for (int i = 0; i < TIER_CLASSES.Length; i++)
                {
                    _sbName.RemoveFromClassList(TIER_CLASSES[i]);
                }
                if (1 <= tier && tier <= TIER_CLASSES.Length)
                {
                    _sbName.AddToClassList(TIER_CLASSES[tier - 1]);
                }
            }

            if (_sbTier != null)
            {
                if (tier > 0)
                {
                    _sbTier.text = $"Tier {tier}";
                    _sbTier.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _sbTier.style.display = DisplayStyle.None;
                }
            }

            if (_sbSkillName != null)
            {
                _sbSkillName.text = skill?.Name ?? "?";
            }

            if (_sbDesc != null)
            {
                string desc = skill?.Desctiption ?? string.Empty;
                if (string.IsNullOrEmpty(desc))
                {
                    _sbDesc.style.display = DisplayStyle.None;
                }
                else
                {
                    _sbDesc.text = desc;
                    _sbDesc.style.display = DisplayStyle.Flex;
                }
            }

            string? damageText = (skill != null && (skill.DamageMin > 0 || skill.DamageMax > 0))
                ? $"데미지: {skill.DamageMin}~{skill.DamageMax}"
                : null;
            string? cooltimeText = (skill != null && skill.Cooltime > 0f)
                ? $"쿨타임: {skill.Cooltime:F1}s"
                : null;
            string? manaText = (skill != null && skill.Mana > 0)
                ? $"마나: {skill.Mana}"
                : null;
            string? rangeText = (skill != null && skill.SkillRangeMax > 0f)
                ? $"사정거리: {skill.SkillRangeMax:F1}"
                : null;

            SetStatLine(_sbStatDamage, damageText);
            SetStatLine(_sbStatCooltime, cooltimeText);
            SetStatLine(_sbStatMana, manaText);
            SetStatLine(_sbStatRange, rangeText);
        }

        // ========== 헬퍼 ==========

        private void ShowContent(VisualElement? which)
        {
            if (_contentSkillBook != null)
            {
                _contentSkillBook.style.display = (_contentSkillBook == which) ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_contentEquipment != null)
            {
                _contentEquipment.style.display = (_contentEquipment == which) ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_contentItem != null)
            {
                _contentItem.style.display = (_contentItem == which) ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static void SetStatLine(Label? label, string? text)
        {
            if (label == null) return;
            if (string.IsNullOrEmpty(text))
            {
                label.style.display = DisplayStyle.None;
            }
            else
            {
                label.text = text;
                label.style.display = DisplayStyle.Flex;
            }
        }

        private async UniTaskVoid LoadAsync()
        {
            GameObject? go = await Addressables.InstantiateAsync(TOOLTIP_PREFAB_KEY, transform, false).ToUniTask();
            if (go == null)
            {
                Debug.LogError($"[TooltipManager] Addressable 로드 실패 — key({TOOLTIP_PREFAB_KEY})");
                return;
            }

            _tooltipObj = go;
            _document = go.GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[TooltipManager] UIDocument 컴포넌트 없음 — TooltipUIT prefab 확인 필요");
                return;
            }

            BindElements();
            _isReady = true;
        }

        private void BindElements()
        {
            if (_document == null) return;
            VisualElement root = _document.rootVisualElement;
            if (root == null) return;

            _root = root;
            _frame = root.Q<VisualElement>("tooltip");

            _contentSkillBook = root.Q<VisualElement>("content-skillbook");
            _contentEquipment = root.Q<VisualElement>("content-equipment");
            _contentItem = root.Q<VisualElement>("content-item");

            _sbName = root.Q<Label>("sb-name");
            _sbTier = root.Q<Label>("sb-tier");
            _sbSkillName = root.Q<Label>("sb-skillname");
            _sbDesc = root.Q<Label>("sb-desc");
            _sbStatDamage = root.Q<Label>("sb-stat-damage");
            _sbStatCooltime = root.Q<Label>("sb-stat-cooltime");
            _sbStatMana = root.Q<Label>("sb-stat-mana");
            _sbStatRange = root.Q<Label>("sb-stat-range");

            Hide();
        }
    }
}
