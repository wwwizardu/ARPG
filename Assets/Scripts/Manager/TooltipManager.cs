#nullable enable
using System.Collections.Generic;
using ARPG.Data;
using ARPG.Item;
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

        // 장비 등급별 이름 색 (ITEM_DROP_BALANCE_DESIGN §4.1): 일반 흰색, 마법 파랑, 희귀 노랑. GlobalEnum.EquipmentGrade 순서
        private static readonly string[] GRADE_CLASSES =
        {
            "tooltip-grade-normal", "tooltip-grade-magic", "tooltip-grade-rare"
        };

        private const float ANCHOR_OFFSET_X = 8f;

        // 인벤토리 장비 비교 (INVENTORY_EQUIPMENT_COMPARISON_DESIGN.md §화면 구성)
        private const float COMPARE_CARD_WIDTH = 250f;
        private const float COMPARE_CARD_GAP = 8f;
        private const float COMPARE_FRAME_HORIZONTAL_EXTRA = 26f;   // .compare-frame 좌우 padding 12×2 + border 1×2
        private const float COMPARE_MAX_HEIGHT_RATIO = 0.75f;
        private const float COMPARE_SCROLL_STEP = 40f;
        private const int COMPARE_MAX_COLUMNS = 3;
        private const string DIFF_COLOR_BETTER = "#78DC78";
        private const string DIFF_COLOR_WORSE = "#EB6464";
        private const string DIFF_COLOR_NEUTRAL = "#C8C8C8";

        /// <summary>비교 카드 한 장. 선택 카드는 Rows가 null이고, 장착 카드는 선택 아이템과의 비교 결과를 가진다.</summary>
        private struct CompareColumn
        {
            public ItemData? Item;
            public string Title;
            public List<EquipmentComparisonRow>? Rows;
        }

        /// <summary>모든 카드가 맞춰 쓰는 행 키 (장착 카드별 비교 결과의 합집합).</summary>
        private struct CompareKey
        {
            public long Key;
            public EquipmentComparisonSection Section;
            public bool IsImplicit;
        }

        private GameObject? _tooltipObj;
        private UIDocument? _document;
        private VisualElement? _root;
        private VisualElement? _frame;

        // 데이터 타입별 컨테이너
        private VisualElement? _contentSkillBook;
        private VisualElement? _contentSkillPage;
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
        private VisualElement? _sbPageSection;
        private Label? _sbPageCapacity;
        private Label? _sbPageList;

        // SkillPage 컨텐츠 element 캐시
        private Label? _spName;
        private Label? _spTier;
        private Label? _spEffectName;
        private Label? _spDesc;
        private Label? _spTrigger;
        private Label? _spCost;
        private Label? _spEfficiency;
        private Label? _spCondition;

        // Equipment 컨텐츠 element 캐시
        private Label? _eqName;
        private Label? _eqTier;
        private Label? _eqItemLevel;
        private Label? _eqQuality;
        private Label? _eqDmgPhysics;
        private Label? _eqDmgFire;
        private Label? _eqDmgIce;
        private Label? _eqDmgLightning;
        private Label? _eqDmgPoison;
        private Label? _eqCrit;
        private Label? _eqAs;
        private Label? _eqBaseTitle;
        private VisualElement? _eqBaseSection;
        private VisualElement? _eqBaseMods;
        private VisualElement? _eqEnhancementSection;
        private VisualElement? _eqLocalEnhancements;
        private VisualElement? _eqOptionsSection;
        private VisualElement? _eqImplicitMods;
        private VisualElement? _eqAffixMods;

        // 일반 아이템 컨텐츠 element 캐시
        private Label? _itName;
        private Label? _itTier;
        private Label? _itDesc;

        // 인벤토리 장비 비교 element 캐시
        private VisualElement? _compareHint;
        private VisualElement? _compareFrame;
        private VisualElement? _compareHeader;
        private ScrollView? _compareBody;
        private Label? _compareGuide;
        private Label? _compareScrollHint;

        // 비교 콘텐츠를 만들 때마다 비우고 다시 채우는 버퍼
        private readonly CompareColumn[] _compareColumns = new CompareColumn[COMPARE_MAX_COLUMNS];
        private int _compareColumnCount = 0;
        private readonly List<EquipmentComparisonRow> _compareRowsLeftClick = new();
        private readonly List<EquipmentComparisonRow> _compareRowsRightClick = new();
        private readonly List<CompareKey> _compareKeys = new();
        private readonly List<VisualElement> _compareTableRows = new();

        private bool _isReady = false;
        private VisualElement? _positionTarget;     // 위치를 맞출 패널 (단일 툴팁 또는 비교 묶음)
        private Rect _pendingAnchorScreenRect;
        private bool _hasPendingPositionUpdate = false;

        public bool IsEquipmentComparisonVisible =>
            _compareFrame != null && _compareFrame.style.display == DisplayStyle.Flex;

        public void Initialize()
        {
            LoadAsync().Forget();
        }

        public void Reset()
        {
            Hide();
        }

        public void Show(ItemData? item, Rect anchorScreenRect)
        {
            ShowSingle(item, anchorScreenRect, false);
        }

        /// <summary>
        /// 인벤토리 장비 전용 단일 툴팁. 맨 아래에 구분선과 `[Shift] 비교` 안내를 붙인다.
        /// 장비가 아니면 Show와 같다. 비교 자체는 ShowEquipmentComparison이 표시한다.
        /// </summary>
        public void ShowWithCompareHint(ItemData? item, Rect anchorScreenRect)
        {
            ShowSingle(item, anchorScreenRect, true);
        }

        /// <summary>
        /// 인벤토리 장비와 장착 아이템 비교 묶음을 표시한다.
        /// 칸이 하나인 장비는 두 슬롯 인자가 같고 [선택한 아이템][현재 장착] 두 장,
        /// 칸이 두 개인 장비는 [선택한 아이템][좌클릭 대상][우클릭 대상] 세 장이다. 빈 칸은 장착 아이템이 null이다.
        /// 표시만 하며 아이템·장비·저장 상태를 바꾸지 않는다.
        /// </summary>
        public void ShowEquipmentComparison(ItemData selected,
            GlobalEnum.EquipSlotType leftClickSlot, ItemData? leftClickItem,
            GlobalEnum.EquipSlotType rightClickSlot, ItemData? rightClickItem,
            Rect anchorScreenRect)
        {
            if (_isReady == false || _frame == null || _compareFrame == null
                || _compareHeader == null || _compareBody == null) return;
            if (selected == null || selected.Table == null || selected.Equipment == null)
            {
                Hide();
                return;
            }

            bool isDualSlot = leftClickSlot != rightClickSlot;
            EquipmentComparisonCalculator.Compare(selected, leftClickItem, _compareRowsLeftClick);
            if (isDualSlot == true)
                EquipmentComparisonCalculator.Compare(selected, rightClickItem, _compareRowsRightClick);
            else
                _compareRowsRightClick.Clear();

            _compareColumnCount = 0;
            AddCompareColumn(selected, "선택한 아이템", null);
            if (isDualSlot == true)
            {
                AddCompareColumn(leftClickItem, $"좌클릭 — {GetSlotDisplayName(leftClickSlot)}", _compareRowsLeftClick);
                AddCompareColumn(rightClickItem, $"우클릭 — {GetSlotDisplayName(rightClickSlot)}", _compareRowsRightClick);
            }
            else
            {
                AddCompareColumn(leftClickItem, $"현재 장착 — {GetSlotDisplayName(leftClickSlot)}", _compareRowsLeftClick);
            }
            CollectCompareKeys();

            _compareHeader.Clear();
            _compareBody.Clear();
            VisualElement bodyRoot = _compareBody.contentContainer;
            float frameWidth;
            if (ShouldStackCompareCards(anchorScreenRect) == false)
            {
                _compareHeader.style.display = DisplayStyle.Flex;
                BuildCompareTable(0, _compareColumnCount, _compareHeader, bodyRoot);
                frameWidth = GetCompareTableWidth(_compareColumnCount);
            }
            else
            {
                // 가용 폭에 들어가지 않으면 선택한 아이템을 위에 두고 장착 카드를 아래에 좌클릭·우클릭 순서로 쌓는다.
                // 두 표를 함께 스크롤하므로 고정 헤더는 쓰지 않는다.
                _compareHeader.style.display = DisplayStyle.None;
                BuildCompareTable(0, 1, bodyRoot, bodyRoot);
                VisualElement gap = new VisualElement();
                gap.pickingMode = PickingMode.Ignore;
                gap.AddToClassList("compare-table-gap");
                bodyRoot.Add(gap);
                BuildCompareTable(1, _compareColumnCount, bodyRoot, bodyRoot);
                frameWidth = GetCompareTableWidth(Mathf.Max(1, _compareColumnCount - 1));
            }

            if (_compareGuide != null)
                _compareGuide.text = BuildCompareGuide(isDualSlot, leftClickSlot, leftClickItem, rightClickSlot, rightClickItem);

            _frame.style.display = DisplayStyle.None;
            _compareFrame.style.width = frameWidth + COMPARE_FRAME_HORIZONTAL_EXTRA;
            float panelHeight = _root != null ? _root.resolvedStyle.height : float.NaN;
            if (float.IsNaN(panelHeight) == false && panelHeight > 0f)
                _compareFrame.style.maxHeight = panelHeight * COMPARE_MAX_HEIGHT_RATIO;
            _compareBody.scrollOffset = Vector2.zero;
            _compareFrame.style.display = DisplayStyle.Flex;
            UpdateCompareScrollHint();
            RequestPositionUpdate(_compareFrame, anchorScreenRect);
        }

        /// <summary>비교 본문을 휠 방향으로 한 단계 스크롤한다. 양수 = 휠을 위로(앞쪽 내용).</summary>
        public void ScrollEquipmentComparison(float wheelDelta)
        {
            if (_compareBody == null || IsEquipmentComparisonVisible == false || wheelDelta == 0f) return;

            float maxOffset = GetCompareScrollableHeight();
            if (maxOffset <= 0f) return;

            Vector2 offset = _compareBody.scrollOffset;
            offset.y = Mathf.Clamp(offset.y - Mathf.Sign(wheelDelta) * COMPARE_SCROLL_STEP, 0f, maxOffset);
            _compareBody.scrollOffset = offset;
        }

        public void Hide()
        {
            if (_frame == null) return;
            _hasPendingPositionUpdate = false;
            _frame.style.display = DisplayStyle.None;
            HideCompareFrame();
        }

        /// <summary>
        /// 슬롯의 스크린 좌표 사각형을 anchor로 받아 표시 중인 툴팁(단일 또는 비교 묶음)의 위치를 결정한다.
        /// 1차: 슬롯 우측에 툴팁 좌측 정렬. 우측 화면 넘치면 슬롯 좌측에 툴팁 우측 정렬.
        /// 하단 넘치면 위쪽으로 정렬, 상단/좌측 넘치면 0 클램프.
        /// </summary>
        public void UpdatePosition(Rect anchorScreenRect)
        {
            if (_isReady == false || _positionTarget == null) return;
            PlaceElement(_positionTarget, anchorScreenRect);
        }

        private void ShowSingle(ItemData? item, Rect anchorScreenRect, bool withCompareHint)
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
                case GlobalEnum.ItemType.SkillPage:
                    if (item.SkillPage != null)
                    {
                        BuildSkillPageContentFromItem(item);
                        activeContent = _contentSkillPage;
                    }
                    break;
                case GlobalEnum.ItemType.Equipment:
                    if (item.Equipment != null)
                    {
                        BuildEquipmentContent(item);
                        activeContent = _contentEquipment;
                    }
                    break;
                default:
                    BuildItemContent(item);
                    activeContent = _contentItem;
                    break;
            }

            if (activeContent == null)
            {
                Hide();
                return;
            }

            SetSectionVisible(_compareHint, withCompareHint == true && activeContent == _contentEquipment);
            HideCompareFrame();
            ShowContent(activeContent);
            _frame.style.display = DisplayStyle.Flex;
            RequestPositionUpdate(_frame, anchorScreenRect);
        }

        private void PlaceElement(VisualElement target, Rect anchorScreenRect)
        {
            if (_root == null) return;
            if (target.style.display == DisplayStyle.None) return;
            if (TryGetPanelAnchor(anchorScreenRect, out Rect anchor) == false) return;

            const float offsetY = 0f;

            float panelW = _root.resolvedStyle.width;
            float panelH = _root.resolvedStyle.height;
            float ttW = target.resolvedStyle.width;
            float ttH = target.resolvedStyle.height;

            // 1차: 슬롯 우측에 툴팁 좌측 정렬, 슬롯 상단(panel Y down 기준 yMin)에 툴팁 상단 정렬
            float x = anchor.xMax + ANCHOR_OFFSET_X;
            float y = anchor.yMin + offsetY;

            // 우측 넘침 → 슬롯 좌측에 툴팁 우측 붙이기 (비교 묶음은 카드 순서를 유지한 채 통째로 옮긴다)
            if (x + ttW > panelW)
            {
                x = anchor.xMin - ttW - ANCHOR_OFFSET_X;
            }
            // 하단 넘침 → 툴팁 하단을 슬롯 하단(yMax)에 정렬
            if (y + ttH > panelH)
            {
                y = anchor.yMax - ttH;
            }
            // 좌측/상단 클램프
            if (x < 0f) x = 0f;
            if (y < 0f) y = 0f;

            target.style.left = x;
            target.style.top = y;
        }

        /// <summary>
        /// Screen Space(Y up, 좌하단 원점) 사각형 → Panel Space(Y down, 좌상단 원점) 사각형.
        /// Rect의 좌하단/우상단 두 점을 변환한 뒤 panel-space Rect로 재구성한다.
        /// </summary>
        private bool TryGetPanelAnchor(Rect anchorScreenRect, out Rect panelAnchor)
        {
            panelAnchor = Rect.zero;
            IPanel? panel = _root?.panel;
            if (panel == null) return false;

            Vector2 flippedBL = new Vector2(anchorScreenRect.xMin, Screen.height - anchorScreenRect.yMin);
            Vector2 flippedTR = new Vector2(anchorScreenRect.xMax, Screen.height - anchorScreenRect.yMax);

            Vector2 panelBL = RuntimePanelUtils.ScreenToPanel(panel, flippedBL);
            Vector2 panelTR = RuntimePanelUtils.ScreenToPanel(panel, flippedTR);

            float xMin = Mathf.Min(panelBL.x, panelTR.x);
            float xMax = Mathf.Max(panelBL.x, panelTR.x);
            float yMin = Mathf.Min(panelBL.y, panelTR.y);
            float yMax = Mathf.Max(panelBL.y, panelTR.y);
            panelAnchor = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return true;
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

            BuildSkillBookPageSection(book);
        }

        private void BuildSkillBookPageSection(ItemData book)
        {
            int slots = AR.s.PlayerSkill?.GetPageSlots(book) ?? 0;
            if (slots <= 0)
            {
                if (_sbPageSection != null) _sbPageSection.style.display = DisplayStyle.None;
                return;
            }

            int used = AR.s.PlayerSkill?.GetUsedPageCost(book) ?? 0;
            int capacity = AR.s.PlayerSkill?.GetPageCapacity(book) ?? 0;
            int filled = book.SkillBook?.SocketedPages?.Count ?? 0;

            if (_sbPageSection != null) _sbPageSection.style.display = DisplayStyle.Flex;

            SetStatLine(_sbPageCapacity, $"페이지: {used} / {capacity} (슬롯 {filled}/{slots})");

            string listText = string.Empty;
            if (filled > 0 && book.SkillBook?.SocketedPages != null)
            {
                System.Text.StringBuilder sb = new();
                var pages = book.SkillBook.SocketedPages;
                for (int i = 0; i < pages.Count; i++)
                {
                    Tables.SkillEffectTable? effect = pages[i].SkillPage?.Table;
                    if (effect == null) continue;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append($"  · {effect.Name} ({effect.PageCost})");
                }
                listText = sb.ToString();
            }
            SetStatLine(_sbPageList, listText);
        }

        private void BuildSkillPageContentFromItem(ItemData page)
        {
            string itemName = page.Table?.Name ?? "스킬 페이지";
            int tier = page.Table?.Tier ?? 0;

            if (_spName != null)
            {
                _spName.text = itemName;
                for (int i = 0; i < TIER_CLASSES.Length; i++)
                {
                    _spName.RemoveFromClassList(TIER_CLASSES[i]);
                }
                if (1 <= tier && tier <= TIER_CLASSES.Length)
                {
                    _spName.AddToClassList(TIER_CLASSES[tier - 1]);
                }
            }

            if (_spTier != null)
            {
                if (tier > 0)
                {
                    _spTier.text = $"Tier {tier}";
                    _spTier.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _spTier.style.display = DisplayStyle.None;
                }
            }

            Tables.SkillEffectTable? effect = page.SkillPage?.Table;
            string effectName = effect?.Name ?? "?";
            if (_spEffectName != null) _spEffectName.text = effectName;

            string desc = effect != null ? DescribeEffect(effect) : string.Empty;
            if (_spDesc != null)
            {
                if (string.IsNullOrEmpty(desc))
                {
                    _spDesc.style.display = DisplayStyle.None;
                }
                else
                {
                    _spDesc.text = desc;
                    _spDesc.style.display = DisplayStyle.Flex;
                }
            }

            string? triggerText = effect != null ? $"트리거: {effect.Trigger}" : null;
            string? costText = effect != null ? $"페이지 비용: {effect.PageCost}" : null;
            string? efficiencyText = (effect != null && effect.PageCost > 0)
                ? $"효율: 효과 강도 / {effect.PageCost} 비용"
                : null;
            string? conditionText = (effect != null && effect.Condition != GlobalEnum.PageCondition.None)
                ? $"조건: {effect.Condition} ({effect.ConditionParam})"
                : null;

            SetStatLine(_spTrigger, triggerText);
            SetStatLine(_spCost, costText);
            SetStatLine(_spEfficiency, efficiencyText);
            SetStatLine(_spCondition, conditionText);
        }

        /// <summary>
        /// Equipment 컨텐츠를 기본 능력 / 무기 고유 강화 / 글로벌 장비 옵션으로 분리해 구성한다.
        /// </summary>
        private void BuildEquipmentContent(ItemData equipItem)
        {
            string itemName = equipItem.Table?.Name ?? "장비";
            int tier = equipItem.Table?.Tier ?? 0;
            EquipmentData? equip = equipItem.Equipment;

            ApplyEquipmentName(_eqName, itemName, equip);
            SetStatLine(_eqTier, FormatTierGradeText(tier, equip));
            // 아이템 레벨이 없는 장비(기본 아이템·시작 장비·구 장비)는 줄을 숨긴다
            SetStatLine(_eqItemLevel, equip != null && equip.ItemLevel > 0 ? $"아이템 레벨 {equip.ItemLevel}" : null);
            // 품질은 0이면 숨긴다 (ITEM_DROP_BALANCE_DESIGN §5)
            SetStatLine(_eqQuality, EquipmentOptionText.FormatQualityLine(equip));

            bool isWeapon = equip != null && equip.EquipType == GlobalEnum.EquipmentTypeMask.Weapon;
            if (_eqBaseTitle != null)
                _eqBaseTitle.text = isWeapon == true ? "무기 기본 능력" : "기본 능력";

            ClearChildren(_eqBaseMods);
            ClearChildren(_eqLocalEnhancements);
            ClearChildren(_eqImplicitMods);
            ClearChildren(_eqAffixMods);

            bool hasBaseContent = false;
            bool hasEnhancementContent = false;
            bool hasOptionContent = false;

            if (isWeapon == true && equip != null)
            {
                WeaponStatCache stats = equip.WeaponStats;
                SetStatLine(_eqDmgPhysics, EquipmentOptionText.FormatWeaponDamageLine("물리 피해", stats.Physics));
                SetStatLine(_eqDmgFire, EquipmentOptionText.FormatWeaponDamageLine("화염 피해", stats.Fire));
                SetStatLine(_eqDmgIce, EquipmentOptionText.FormatWeaponDamageLine("냉기 피해", stats.Ice));
                SetStatLine(_eqDmgLightning, EquipmentOptionText.FormatWeaponDamageLine("번개 피해", stats.Lightning));
                SetStatLine(_eqDmgPoison, EquipmentOptionText.FormatWeaponDamageLine("독 피해", stats.Poison));
                SetStatLine(_eqCrit, EquipmentOptionText.FormatWeaponCritLine(stats.CriRate));
                SetStatLine(_eqAs, EquipmentOptionText.FormatWeaponAttackSpeedLine(stats.AttackSpeed));
                hasBaseContent = stats.Physics.Max > 0
                    || stats.Fire.Max > 0
                    || stats.Ice.Max > 0
                    || stats.Lightning.Max > 0
                    || stats.Poison.Max > 0
                    || stats.CriRate > 0
                    || stats.AttackSpeed > 0f;
            }
            else
            {
                SetStatLine(_eqDmgPhysics, null);
                SetStatLine(_eqDmgFire, null);
                SetStatLine(_eqDmgIce, null);
                SetStatLine(_eqDmgLightning, null);
                SetStatLine(_eqDmgPoison, null);
                SetStatLine(_eqCrit, null);
                SetStatLine(_eqAs, null);
            }

            if (equip == null)
            {
                SetSectionVisible(_eqBaseSection, false);
                SetSectionVisible(_eqEnhancementSection, false);
                SetSectionVisible(_eqOptionsSection, false);
                return;
            }

            // 방어구는 Base Mod를 표시한다. 무기는 WeaponStats가 Base/강화 결과를 이미 표시하므로 Base 라인을 반복하지 않는다.
            if (isWeapon == false)
            {
                for (int i = 0; i < equip.Mods.Count; i++)
                {
                    ModInstance mod = equip.Mods[i];
                    if (mod.Table == null) continue;
                    if (mod.Slot != GlobalEnum.ModSlot.Base) continue;
                    // Base 수치는 품질을 적용해 보여 준다 (ITEM_DROP_BALANCE_DESIGN §5)
                    AppendModLine(_eqBaseMods, EquipmentQualityRules.ApplyToBaseMod(mod, equip.Quality), isImplicit: false);
                    hasBaseContent = true;
                }
            }

            if (isWeapon == true)
            {
                for (int i = 0; i < equip.WeaponLocalEnhancements.Count; i++)
                {
                    WeaponLocalEnhancementInstance enhancement = equip.WeaponLocalEnhancements[i];
                    if (enhancement == null || enhancement.Value == 0) continue;
                    AppendEnhancementLine(_eqLocalEnhancements, enhancement);
                    hasEnhancementContent = true;
                }
            }

            for (int i = 0; i < equip.Mods.Count; i++)
            {
                ModInstance mod = equip.Mods[i];
                if (mod.Table == null) continue;
                if (mod.Slot != GlobalEnum.ModSlot.Implicit) continue;
                AppendModLine(_eqImplicitMods, mod, isImplicit: true);
                hasOptionContent = true;
            }

            for (int i = 0; i < equip.Mods.Count; i++)
            {
                ModInstance mod = equip.Mods[i];
                if (mod.Table == null) continue;
                if (mod.Slot != GlobalEnum.ModSlot.Prefix && mod.Slot != GlobalEnum.ModSlot.Postfix) continue;
                AppendModLine(_eqAffixMods, mod, isImplicit: false);
                hasOptionContent = true;
            }

            SetSectionVisible(_eqBaseSection, hasBaseContent);
            SetSectionVisible(_eqEnhancementSection, hasEnhancementContent);
            SetSectionVisible(_eqOptionsSection, hasOptionContent);
        }

        /// <summary>
        /// 일반 아이템(자원/소비 등) 컨텐츠 빌더. 이름 + Tier + Description.
        /// </summary>
        private void BuildItemContent(ItemData item)
        {
            string itemName = item.Table?.Name ?? "아이템";
            int tier = item.Table?.Tier ?? 0;
            string desc = item.Table?.Description ?? string.Empty;

            ApplyTierName(_itName, itemName, tier);
            ApplyTierLabel(_itTier, tier);

            if (_itDesc != null)
            {
                if (string.IsNullOrEmpty(desc))
                {
                    _itDesc.style.display = DisplayStyle.None;
                }
                else
                {
                    _itDesc.text = desc;
                    _itDesc.style.display = DisplayStyle.Flex;
                }
            }
        }

        private static void AppendModLine(VisualElement? container, ModInstance mod, bool isImplicit)
        {
            if (container == null || mod.Table == null) return;

            string body = EquipmentOptionText.FormatModLine(mod);

            Label line = CreateIgnoredLabel(body, isImplicit ? "tooltip-statline-implicit" : "tooltip-modline");
            container.Add(line);
        }

        private static void AppendEnhancementLine(
            VisualElement? container,
            WeaponLocalEnhancementInstance enhancement)
        {
            if (container == null) return;

            Label line = CreateIgnoredLabel(EquipmentOptionText.FormatWeaponEnhancementLine(enhancement), "tooltip-enhancement-line");
            container.Add(line);
        }

        // ========== 인벤토리 장비 비교 빌더 ==========

        private void AddCompareColumn(ItemData? item, string title, List<EquipmentComparisonRow>? rows)
        {
            if (_compareColumnCount >= _compareColumns.Length) return;
            _compareColumns[_compareColumnCount] = new CompareColumn
            {
                Item = item,
                Title = title,
                Rows = rows,
            };
            _compareColumnCount++;
        }

        /// <summary>장착 카드별 비교 결과의 행 키를 합쳐 모든 카드가 같은 순서·위치로 쓰게 한다.</summary>
        private void CollectCompareKeys()
        {
            _compareKeys.Clear();
            for (int c = 0; c < _compareColumnCount; c++)
            {
                List<EquipmentComparisonRow>? rows = _compareColumns[c].Rows;
                if (rows == null) continue;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (ContainsCompareKey(rows[i].Key) == true) continue;
                    _compareKeys.Add(new CompareKey
                    {
                        Key = rows[i].Key,
                        Section = rows[i].Section,
                        IsImplicit = rows[i].IsImplicit,
                    });
                }
            }

            // 키 수가 적어 삽입 정렬로 충분하다 (비교 델리게이트를 만들지 않는다)
            for (int i = 1; i < _compareKeys.Count; i++)
            {
                CompareKey current = _compareKeys[i];
                int j = i - 1;
                while (j >= 0 && _compareKeys[j].Key > current.Key)
                {
                    _compareKeys[j + 1] = _compareKeys[j];
                    j--;
                }
                _compareKeys[j + 1] = current;
            }
        }

        private bool ContainsCompareKey(long key)
        {
            for (int i = 0; i < _compareKeys.Count; i++)
            {
                if (_compareKeys[i].Key == key) return true;
            }
            return false;
        }

        /// <summary>
        /// 카드 [firstColumn, endColumn)를 행 단위 표로 만든다. 같은 행의 칸은 높이가 같아 모든 카드의 항목 위치가 맞는다.
        /// 이름·등급·아이템 레벨은 headerParent(고정), 능력·옵션은 bodyParent(스크롤)에 붙인다.
        /// </summary>
        private void BuildCompareTable(int firstColumn, int endColumn, VisualElement headerParent, VisualElement bodyParent)
        {
            _compareTableRows.Clear();

            VisualElement titleRow = CreateCompareRow(headerParent);
            for (int c = firstColumn; c < endColumn; c++)
                CreateCompareCell(titleRow, c - firstColumn).Add(CreateIgnoredLabel(_compareColumns[c].Title, "compare-card-title"));

            VisualElement nameRow = CreateCompareRow(headerParent);
            bool hasTier = false;
            bool hasItemLevel = false;
            bool hasQuality = false;
            for (int c = firstColumn; c < endColumn; c++)
            {
                VisualElement cell = CreateCompareCell(nameRow, c - firstColumn);
                ItemData? item = _compareColumns[c].Item;
                if (item == null)
                {
                    cell.Add(CreateIgnoredLabel("장착된 아이템 없음", "compare-empty"));
                    continue;
                }

                Label name = CreateIgnoredLabel(string.Empty, "tooltip-name");
                ApplyEquipmentName(name, item.Table?.Name ?? "장비", item.Equipment);
                cell.Add(name);
                hasTier |= FormatTierGradeText(item.Table?.Tier ?? 0, item.Equipment) != null;
                hasItemLevel |= (item.Equipment?.ItemLevel ?? 0) > 0;
                hasQuality |= EquipmentOptionText.FormatQualityLine(item.Equipment) != null;
            }

            // 이름·등급·아이템 레벨에는 우열 색을 붙이지 않는다
            if (hasTier == true)
            {
                VisualElement tierRow = CreateCompareRow(headerParent);
                for (int c = firstColumn; c < endColumn; c++)
                {
                    VisualElement cell = CreateCompareCell(tierRow, c - firstColumn);
                    ItemData? tierItem = _compareColumns[c].Item;
                    string? tierText = FormatTierGradeText(tierItem?.Table?.Tier ?? 0, tierItem?.Equipment);
                    if (tierText != null) cell.Add(CreateIgnoredLabel(tierText, "tooltip-tier"));
                }
            }

            if (hasItemLevel == true)
            {
                VisualElement levelRow = CreateCompareRow(headerParent);
                for (int c = firstColumn; c < endColumn; c++)
                {
                    VisualElement cell = CreateCompareCell(levelRow, c - firstColumn);
                    int itemLevel = _compareColumns[c].Item?.Equipment?.ItemLevel ?? 0;
                    if (itemLevel > 0) cell.Add(CreateIgnoredLabel($"아이템 레벨 {itemLevel}", "tooltip-tier"));
                }
            }

            if (hasQuality == true)
            {
                VisualElement qualityRow = CreateCompareRow(headerParent);
                for (int c = firstColumn; c < endColumn; c++)
                {
                    VisualElement cell = CreateCompareCell(qualityRow, c - firstColumn);
                    string? qualityText = EquipmentOptionText.FormatQualityLine(_compareColumns[c].Item?.Equipment);
                    if (qualityText != null) cell.Add(CreateIgnoredLabel(qualityText, "tooltip-tier"));
                }
            }

            VisualElement dividerRow = CreateCompareRow(headerParent);
            for (int c = firstColumn; c < endColumn; c++)
            {
                VisualElement divider = new VisualElement();
                divider.pickingMode = PickingMode.Ignore;
                divider.AddToClassList("tooltip-divider");
                CreateCompareCell(dividerRow, c - firstColumn).Add(divider);
            }

            AppendCompareSection(EquipmentComparisonSection.Base, firstColumn, endColumn, bodyParent);
            AppendCompareSection(EquipmentComparisonSection.Enhancement, firstColumn, endColumn, bodyParent);
            AppendCompareSection(EquipmentComparisonSection.Options, firstColumn, endColumn, bodyParent);

            // 같은 열의 첫 칸·마지막 칸에 카드 모서리를 붙인다
            if (_compareTableRows.Count > 0)
            {
                AddClassToRowCells(_compareTableRows[0], "compare-cell-top");
                AddClassToRowCells(_compareTableRows[_compareTableRows.Count - 1], "compare-cell-bottom");
            }
            _compareTableRows.Clear();
        }

        private void AppendCompareSection(EquipmentComparisonSection section, int firstColumn, int endColumn, VisualElement parent)
        {
            bool hasAnyRow = false;
            for (int k = 0; k < _compareKeys.Count; k++)
            {
                if (_compareKeys[k].Section != section) continue;
                if (HasCompareRowContent(_compareKeys[k].Key, firstColumn, endColumn) == false) continue;
                hasAnyRow = true;
                break;
            }
            if (hasAnyRow == false) return;

            VisualElement titleRow = CreateCompareRow(parent);
            for (int c = firstColumn; c < endColumn; c++)
            {
                VisualElement cell = CreateCompareCell(titleRow, c - firstColumn);
                if (ColumnHasSectionContent(c, section) == false) continue;
                Label title = CreateIgnoredLabel(GetCompareSectionTitle(section, _compareColumns[c].Item), "tooltip-section-title");
                title.AddToClassList("compare-section-title");
                if (section == EquipmentComparisonSection.Enhancement)
                    title.AddToClassList("tooltip-section-title-enhancement");
                cell.Add(title);
            }

            for (int k = 0; k < _compareKeys.Count; k++)
            {
                CompareKey key = _compareKeys[k];
                if (key.Section != section) continue;
                if (HasCompareRowContent(key.Key, firstColumn, endColumn) == false) continue;

                VisualElement row = CreateCompareRow(parent);
                for (int c = firstColumn; c < endColumn; c++)
                {
                    VisualElement cell = CreateCompareCell(row, c - firstColumn);
                    if (TryGetCompareCellText(c, key.Key, out string? text, out string? diff, out EquipmentComparisonTone tone) == false)
                        continue;
                    cell.Add(CreateCompareLine(key, text, diff, tone));
                }
            }
        }

        /// <summary>
        /// 카드 c의 행 문구. 장착된 아이템이 없는 카드는 비워 둔다(false).
        /// 그 카드의 아이템에 없는 항목은 text가 null(—)이며, 장착 카드는 값 뒤에 `교체 시` 차이를 붙인다.
        /// </summary>
        private bool TryGetCompareCellText(int column, long key, out string? text, out string? diff, out EquipmentComparisonTone tone)
        {
            text = null;
            diff = null;
            tone = EquipmentComparisonTone.None;

            CompareColumn info = _compareColumns[column];
            if (info.Item == null) return false;

            if (info.Rows == null)
            {
                text = FindSelectedCompareText(key);
                return true;
            }

            for (int i = 0; i < info.Rows.Count; i++)
            {
                EquipmentComparisonRow row = info.Rows[i];
                if (row.Key != key) continue;
                text = row.EquippedText;
                diff = row.DiffText;
                tone = row.Tone;
                return true;
            }
            return true;
        }

        private string? FindSelectedCompareText(long key)
        {
            for (int c = 0; c < _compareColumnCount; c++)
            {
                List<EquipmentComparisonRow>? rows = _compareColumns[c].Rows;
                if (rows == null) continue;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Key == key) return rows[i].SelectedText;
                }
            }
            return null;
        }

        private bool HasCompareRowContent(long key, int firstColumn, int endColumn)
        {
            for (int c = firstColumn; c < endColumn; c++)
            {
                if (TryGetCompareCellText(c, key, out string? text, out string? diff, out _) == false) continue;
                if (text != null || diff != null) return true;
            }
            return false;
        }

        private bool ColumnHasSectionContent(int column, EquipmentComparisonSection section)
        {
            for (int k = 0; k < _compareKeys.Count; k++)
            {
                if (_compareKeys[k].Section != section) continue;
                if (TryGetCompareCellText(column, _compareKeys[k].Key, out string? text, out string? diff, out _) == false) return false;
                if (text != null || diff != null) return true;
            }
            return false;
        }

        private static Label CreateCompareLine(CompareKey key, string? text, string? diff, EquipmentComparisonTone tone)
        {
            string body = text ?? "—";
            // `교체 시 …`는 한 덩어리로 줄바꿈한다 (한글은 글자 단위로 끊겨 `교체 시 제/거`처럼 갈라진다)
            if (diff != null)
                body = $"{body} · <nobr><color={GetDiffColor(tone)}>{diff}</color></nobr>";

            Label line = CreateIgnoredLabel(body, "compare-line");
            if (text == null)
                line.AddToClassList("compare-line-missing");
            else if (key.Section == EquipmentComparisonSection.Enhancement)
                line.AddToClassList("compare-line-enhancement");
            else if (key.IsImplicit == true)
                line.AddToClassList("compare-line-implicit");
            return line;
        }

        private VisualElement CreateCompareRow(VisualElement parent)
        {
            VisualElement row = new VisualElement();
            row.pickingMode = PickingMode.Ignore;
            row.AddToClassList("compare-row");
            parent.Add(row);
            _compareTableRows.Add(row);
            return row;
        }

        private static VisualElement CreateCompareCell(VisualElement row, int positionInRow)
        {
            VisualElement cell = new VisualElement();
            cell.pickingMode = PickingMode.Ignore;
            cell.AddToClassList("compare-cell");
            cell.style.width = COMPARE_CARD_WIDTH;
            if (positionInRow > 0)
                cell.style.marginLeft = COMPARE_CARD_GAP;
            row.Add(cell);
            return cell;
        }

        private static void AddClassToRowCells(VisualElement row, string className)
        {
            for (int i = 0; i < row.childCount; i++)
                row[i].AddToClassList(className);
        }

        private static float GetCompareTableWidth(int columnCount)
        {
            return columnCount * COMPARE_CARD_WIDTH + Mathf.Max(0, columnCount - 1) * COMPARE_CARD_GAP;
        }

        /// <summary>카드를 한 줄로 놓을 폭이 슬롯 오른쪽에도 왼쪽에도 없으면 쌓는다.</summary>
        private bool ShouldStackCompareCards(Rect anchorScreenRect)
        {
            if (_root == null || _compareColumnCount <= 1) return false;
            if (TryGetPanelAnchor(anchorScreenRect, out Rect anchor) == false) return false;

            float panelW = _root.resolvedStyle.width;
            if (float.IsNaN(panelW) == true || panelW <= 0f) return false;

            float bundleWidth = GetCompareTableWidth(_compareColumnCount) + COMPARE_FRAME_HORIZONTAL_EXTRA;
            float rightSpace = panelW - (anchor.xMax + ANCHOR_OFFSET_X);
            float leftSpace = anchor.xMin - ANCHOR_OFFSET_X;
            return bundleWidth > rightSpace && bundleWidth > leftSpace;
        }

        private float GetCompareScrollableHeight()
        {
            if (_compareBody == null) return 0f;
            float contentHeight = _compareBody.contentContainer.layout.height;
            float viewportHeight = _compareBody.contentViewport.layout.height;
            if (float.IsNaN(contentHeight) == true || float.IsNaN(viewportHeight) == true) return 0f;
            return Mathf.Max(0f, contentHeight - viewportHeight);
        }

        private void UpdateCompareScrollHint()
        {
            if (_compareScrollHint == null || IsEquipmentComparisonVisible == false) return;

            DisplayStyle display = GetCompareScrollableHeight() > 0.5f ? DisplayStyle.Flex : DisplayStyle.None;
            if (_compareScrollHint.style.display == display) return;

            // 안내 줄이 생기거나 사라지면 묶음 높이가 바뀌므로 위치를 다시 맞춘다
            _compareScrollHint.style.display = display;
            _hasPendingPositionUpdate = true;
        }

        private void HideCompareFrame()
        {
            if (_compareFrame == null) return;
            _compareFrame.style.display = DisplayStyle.None;
        }

        private static string GetCompareSectionTitle(EquipmentComparisonSection section, ItemData? item)
        {
            switch (section)
            {
                case EquipmentComparisonSection.Base:
                    return item?.Equipment?.EquipType == GlobalEnum.EquipmentTypeMask.Weapon ? "무기 기본 능력" : "기본 능력";
                case EquipmentComparisonSection.Enhancement:
                    return "무기 고유 강화";
                default:
                    return "장비 옵션";
            }
        }

        private static string GetDiffColor(EquipmentComparisonTone tone)
        {
            switch (tone)
            {
                case EquipmentComparisonTone.Better: return DIFF_COLOR_BETTER;
                case EquipmentComparisonTone.Worse: return DIFF_COLOR_WORSE;
                default: return DIFF_COLOR_NEUTRAL;
            }
        }

        /// <summary>
        /// 하단 조작 안내. 칸이 두 개면 `좌클릭: 오른쪽 반지와 교체 · 우클릭: 왼쪽 반지에 장착`,
        /// 칸이 하나면 `클릭: 갑옷과 교체`. 부위만 적고, 왼손 방패는 교체 대상을 `왼손 방패`로 밝힌다.
        /// </summary>
        private static string BuildCompareGuide(bool isDualSlot,
            GlobalEnum.EquipSlotType leftClickSlot, ItemData? leftClickItem,
            GlobalEnum.EquipSlotType rightClickSlot, ItemData? rightClickItem)
        {
            if (isDualSlot == false)
                return $"클릭: {DescribeEquipAction(leftClickSlot, leftClickItem)}";

            return $"좌클릭: {DescribeEquipAction(leftClickSlot, leftClickItem)} · 우클릭: {DescribeEquipAction(rightClickSlot, rightClickItem)}";
        }

        private static string DescribeEquipAction(GlobalEnum.EquipSlotType slot, ItemData? equipped)
        {
            string slotName = GetSlotDisplayName(slot);
            if (equipped == null)
                return $"{slotName}에 장착";

            // 왼손에는 무기와 방패가 모두 들어가므로 방패는 교체 대상을 함께 적는다
            string target = equipped.Equipment?.EquipType == GlobalEnum.EquipmentTypeMask.Shield
                ? $"{slotName} 방패"
                : slotName;
            return $"{target}{GetWithParticle(target)} 교체";
        }

        /// <summary>마지막 글자의 받침 유무로 `과`/`와`를 고른다. 한글이 아니면 `와`.</summary>
        private static string GetWithParticle(string word)
        {
            if (string.IsNullOrEmpty(word) == true) return "와";
            char last = word[word.Length - 1];
            if (last < '가' || last > '힣') return "와";
            return (last - '가') % 28 != 0 ? "과" : "와";
        }

        private static string GetSlotDisplayName(GlobalEnum.EquipSlotType slot)
        {
            switch (slot)
            {
                case GlobalEnum.EquipSlotType.WeaponRight: return "오른손";
                case GlobalEnum.EquipSlotType.WeaponLeft: return "왼손";
                case GlobalEnum.EquipSlotType.Helmet: return "투구";
                case GlobalEnum.EquipSlotType.Armor: return "갑옷";
                case GlobalEnum.EquipSlotType.Gloves: return "장갑";
                case GlobalEnum.EquipSlotType.Boots: return "장화";
                case GlobalEnum.EquipSlotType.Necklace: return "목걸이";
                case GlobalEnum.EquipSlotType.RingRight: return "오른쪽 반지";
                case GlobalEnum.EquipSlotType.RingLeft: return "왼쪽 반지";
                case GlobalEnum.EquipSlotType.Belt: return "허리띠";
                case GlobalEnum.EquipSlotType.EarringRight: return "오른쪽 귀걸이";
                case GlobalEnum.EquipSlotType.EarringLeft: return "왼쪽 귀걸이";
                default: return slot.ToString();
            }
        }

        /// <summary>툴팁은 포인터를 가로채지 않는다 (슬롯 호버·클릭 유지).</summary>
        private static Label CreateIgnoredLabel(string text, string className)
        {
            Label label = new Label(text);
            label.pickingMode = PickingMode.Ignore;
            label.AddToClassList(className);
            return label;
        }

        private static void IgnorePickingRecursive(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < element.hierarchy.childCount; i++)
                IgnorePickingRecursive(element.hierarchy[i]);
        }

        private static void SetSectionVisible(VisualElement? section, bool visible)
        {
            if (section == null) return;
            section.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void ApplyTierName(Label? label, string text, int tier)
        {
            if (label == null) return;
            label.text = text;
            for (int i = 0; i < TIER_CLASSES.Length; i++)
                label.RemoveFromClassList(TIER_CLASSES[i]);
            if (1 <= tier && tier <= TIER_CLASSES.Length)
                label.AddToClassList(TIER_CLASSES[tier - 1]);
        }

        /// <summary>장비 이름: 등급 색 (일반 흰색·마법 파랑·희귀 노랑). 장비 데이터가 없으면 색을 붙이지 않는다.</summary>
        private static void ApplyEquipmentName(Label? label, string text, EquipmentData? equip)
        {
            if (label == null) return;
            label.text = text;
            for (int i = 0; i < TIER_CLASSES.Length; i++)
                label.RemoveFromClassList(TIER_CLASSES[i]);
            for (int i = 0; i < GRADE_CLASSES.Length; i++)
                label.RemoveFromClassList(GRADE_CLASSES[i]);
            if (equip != null)
                label.AddToClassList(GRADE_CLASSES[(int)equip.GetGrade()]);
        }

        /// <summary>"Tier 2 · 마법"처럼 아이템 Tier와 장비 등급을 한 줄로. 둘 다 없으면 null.</summary>
        private static string? FormatTierGradeText(int tier, EquipmentData? equip)
        {
            string? gradeName = equip != null ? EquipmentOptionText.GetGradeName(equip.GetGrade()) : null;
            if (tier > 0 && gradeName != null) return $"Tier {tier} · {gradeName}";
            if (tier > 0) return $"Tier {tier}";
            return gradeName;
        }

        private static void ApplyTierLabel(Label? label, int tier)
        {
            if (label == null) return;
            if (tier > 0)
            {
                label.text = $"Tier {tier}";
                label.style.display = DisplayStyle.Flex;
            }
            else
            {
                label.style.display = DisplayStyle.None;
            }
        }

        private static void ClearChildren(VisualElement? element)
        {
            if (element == null) return;
            element.Clear();
        }

        /// <summary>
        /// 책에 장착된 페이지(SkillEffectId만 들고 있는 케이스) 전용 빌더.
        /// ItemTable 의존이 없으며, 인벤토리 페이지 빌더와 분리해 표시 정책을 독립적으로 가져간다.
        /// </summary>
        private void BuildSkillPageContentFromEffect(Tables.SkillEffectTable effect)
        {
            if (_spName != null)
            {
                _spName.text = effect.Name;
                for (int i = 0; i < TIER_CLASSES.Length; i++)
                {
                    _spName.RemoveFromClassList(TIER_CLASSES[i]);
                }
            }

            if (_spTier != null)
            {
                _spTier.style.display = DisplayStyle.None;
            }

            if (_spEffectName != null)
            {
                _spEffectName.style.display = DisplayStyle.None;
            }

            string desc = DescribeEffect(effect);
            if (_spDesc != null)
            {
                if (string.IsNullOrEmpty(desc))
                {
                    _spDesc.style.display = DisplayStyle.None;
                }
                else
                {
                    _spDesc.text = desc;
                    _spDesc.style.display = DisplayStyle.Flex;
                }
            }

            string triggerText = $"트리거: {effect.Trigger}";
            string costText = $"페이지 비용: {effect.PageCost}";
            string? efficiencyText = effect.PageCost > 0
                ? $"효율: 효과 강도 / {effect.PageCost} 비용"
                : null;
            string? conditionText = effect.Condition != GlobalEnum.PageCondition.None
                ? $"조건: {effect.Condition} ({effect.ConditionParam})"
                : null;

            SetStatLine(_spTrigger, triggerText);
            SetStatLine(_spCost, costText);
            SetStatLine(_spEfficiency, efficiencyText);
            SetStatLine(_spCondition, conditionText);
        }

        /// <summary>
        /// 책에 장착된 페이지처럼 ItemData 인스턴스가 없는 케이스를 위한 오버로드.
        /// 미래에 SkillEffectId의 형식이 바뀌어도 호출자(UISkillBook 등)가 같은 패턴으로 호출 가능하도록 별도 시그니처로 분리.
        /// </summary>
        public void Show(int skillEffectId, Rect anchorScreenRect)
        {
            if (_isReady == false || _frame == null) return;
            if (skillEffectId <= 0)
            {
                Hide();
                return;
            }

            Tables.SkillEffectTable? effect = AR.s.Data?.GetSkillEffect(skillEffectId);
            if (effect == null)
            {
                Hide();
                return;
            }

            BuildSkillPageContentFromEffect(effect);
            SetSectionVisible(_compareHint, false);
            HideCompareFrame();
            ShowContent(_contentSkillPage);
            _frame.style.display = DisplayStyle.Flex;
            RequestPositionUpdate(_frame, anchorScreenRect);
        }

        private static string DescribeEffect(Tables.SkillEffectTable effect)
        {
            return effect.EffectType switch
            {
                GlobalEnum.Stat.LifeSteal => $"적중 시 입힌 피해의 {effect.Param1}%만큼 생명력 회복",
                GlobalEnum.Stat.ApplyBuff => DescribeApplyBuffOnHit(effect),
                GlobalEnum.Stat.DelegateToTotem => $"토템 소환({effect.Param1}초)",
                _ => string.Empty,
            };
        }

        private static string DescribeApplyBuffOnHit(SkillEffectTable effect)
        {
            int buffId = (int)effect.Param1;
            BuffTable? buffTable = AR.s.Data.GetBuff(buffId);
            string buffLabel = buffTable != null ? buffTable.Name : $"#{buffId}";

            float duration = effect.Param3;
            if (duration <= 0f && buffTable != null)
                duration = buffTable.Duration;

            return $"적중 시 [{buffLabel}] {duration}초 부여";
        }

        // ========== 헬퍼 ==========

        private void ShowContent(VisualElement? which)
        {
            if (_contentSkillBook != null)
            {
                _contentSkillBook.style.display = (_contentSkillBook == which) ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_contentSkillPage != null)
            {
                _contentSkillPage.style.display = (_contentSkillPage == which) ? DisplayStyle.Flex : DisplayStyle.None;
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

        private void RequestPositionUpdate(VisualElement target, Rect anchorScreenRect)
        {
            _positionTarget = target;
            _pendingAnchorScreenRect = anchorScreenRect;
            _hasPendingPositionUpdate = true;

            // 이미 레이아웃이 계산된 툴팁은 즉시 이동하고,
            // display 복원이나 콘텐츠 변경으로 크기가 바뀌면 GeometryChangedEvent에서 보정한다.
            PlaceElement(target, anchorScreenRect);
        }

        private void OnTooltipGeometryChanged(GeometryChangedEvent evt)
        {
            if (_hasPendingPositionUpdate == false || _positionTarget == null) return;
            if (evt.target != _positionTarget) return;
            if (_positionTarget.style.display == DisplayStyle.None)
            {
                _hasPendingPositionUpdate = false;
                return;
            }

            _hasPendingPositionUpdate = false;
            PlaceElement(_positionTarget, _pendingAnchorScreenRect);
        }

        private void OnCompareGeometryChanged(GeometryChangedEvent evt)
        {
            OnTooltipGeometryChanged(evt);
            // 안내 줄을 토글하면 다음 GeometryChangedEvent에서 바뀐 높이로 다시 배치한다
            UpdateCompareScrollHint();
        }

        private void OnCompareContentGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateCompareScrollHint();
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
            _frame?.RegisterCallback<GeometryChangedEvent>(OnTooltipGeometryChanged);

            _contentSkillBook = root.Q<VisualElement>("content-skillbook");
            _contentSkillPage = root.Q<VisualElement>("content-skillpage");
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
            _sbPageSection = root.Q<VisualElement>("sb-page-section");
            _sbPageCapacity = root.Q<Label>("sb-page-capacity");
            _sbPageList = root.Q<Label>("sb-page-list");

            _spName = root.Q<Label>("sp-name");
            _spTier = root.Q<Label>("sp-tier");
            _spEffectName = root.Q<Label>("sp-effect-name");
            _spDesc = root.Q<Label>("sp-desc");
            _spTrigger = root.Q<Label>("sp-trigger");
            _spCost = root.Q<Label>("sp-cost");
            _spEfficiency = root.Q<Label>("sp-efficiency");
            _spCondition = root.Q<Label>("sp-condition");

            _eqName = root.Q<Label>("eq-name");
            _eqTier = root.Q<Label>("eq-tier");
            _eqItemLevel = root.Q<Label>("eq-item-level");
            _eqQuality = root.Q<Label>("eq-quality");
            _eqDmgPhysics = root.Q<Label>("eq-dmg-physics");
            _eqDmgFire = root.Q<Label>("eq-dmg-fire");
            _eqDmgIce = root.Q<Label>("eq-dmg-ice");
            _eqDmgLightning = root.Q<Label>("eq-dmg-lightning");
            _eqDmgPoison = root.Q<Label>("eq-dmg-poison");
            _eqCrit = root.Q<Label>("eq-crit");
            _eqAs = root.Q<Label>("eq-as");
            _eqBaseTitle = root.Q<Label>("eq-base-title");
            _eqBaseSection = root.Q<VisualElement>("eq-base-section");
            _eqBaseMods = root.Q<VisualElement>("eq-base-mods");
            _eqEnhancementSection = root.Q<VisualElement>("eq-enhancement-section");
            _eqLocalEnhancements = root.Q<VisualElement>("eq-local-enhancements");
            _eqOptionsSection = root.Q<VisualElement>("eq-options-section");
            _eqImplicitMods = root.Q<VisualElement>("eq-implicit-mods");
            _eqAffixMods = root.Q<VisualElement>("eq-affix-mods");

            _itName = root.Q<Label>("it-name");
            _itTier = root.Q<Label>("it-tier");
            _itDesc = root.Q<Label>("it-desc");

            _compareHint = root.Q<VisualElement>("compare-hint");
            _compareFrame = root.Q<VisualElement>("compare");
            _compareHeader = root.Q<VisualElement>("compare-header");
            _compareBody = root.Q<ScrollView>("compare-body");
            _compareGuide = root.Q<Label>("compare-guide");
            _compareScrollHint = root.Q<Label>("compare-scroll-hint");
            _compareFrame?.RegisterCallback<GeometryChangedEvent>(OnCompareGeometryChanged);
            if (_compareBody != null)
            {
                // 본문은 인벤토리 슬롯 위 휠 입력으로만 스크롤한다 (ScrollEquipmentComparison)
                _compareBody.mode = ScrollViewMode.Vertical;
                _compareBody.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                _compareBody.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                _compareBody.contentContainer.RegisterCallback<GeometryChangedEvent>(OnCompareContentGeometryChanged);
                _compareBody.contentViewport.RegisterCallback<GeometryChangedEvent>(OnCompareContentGeometryChanged);
            }

            // UXML 기본 Label·ScrollView 내부 요소도 포인터를 가로채지 않게 한다 (슬롯 호버·클릭 유지)
            IgnorePickingRecursive(root);

            Hide();
        }
    }
}
