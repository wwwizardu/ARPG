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

        private bool _isReady = false;

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

            ShowContent(activeContent);
            _frame.style.display = DisplayStyle.Flex;
            UpdatePosition(anchorScreenRect);
        }

        public void Hide()
        {
            if (_frame == null) return;
            _frame.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// 슬롯의 스크린 좌표 사각형을 anchor로 받아 툴팁 위치를 결정한다.
        /// 1차: 슬롯 우측에 툴팁 좌측 정렬. 우측 화면 넘치면 슬롯 좌측에 툴팁 우측 정렬.
        /// 하단 넘치면 위쪽으로 정렬, 상단/좌측 넘치면 0 클램프.
        /// </summary>
        public void UpdatePosition(Rect anchorScreenRect)
        {
            if (_isReady == false || _frame == null || _root == null) return;
            if (_frame.style.display == DisplayStyle.None) return;

            IPanel? panel = _root.panel;
            if (panel == null) return;

            // Screen Space(Y up, 좌하단 원점) → Panel Space(Y down, 좌상단 원점) 변환.
            // Rect의 좌하단/우상단 두 점을 변환한 뒤 panel-space Rect로 재구성한다.
            Vector2 slotBottomLeftScreen = new Vector2(anchorScreenRect.xMin, anchorScreenRect.yMin);
            Vector2 slotTopRightScreen = new Vector2(anchorScreenRect.xMax, anchorScreenRect.yMax);

            Vector2 flippedBL = new Vector2(slotBottomLeftScreen.x, Screen.height - slotBottomLeftScreen.y);
            Vector2 flippedTR = new Vector2(slotTopRightScreen.x, Screen.height - slotTopRightScreen.y);

            Vector2 panelBL = RuntimePanelUtils.ScreenToPanel(panel, flippedBL);
            Vector2 panelTR = RuntimePanelUtils.ScreenToPanel(panel, flippedTR);

            float anchorXMin = Mathf.Min(panelBL.x, panelTR.x);
            float anchorXMax = Mathf.Max(panelBL.x, panelTR.x);
            float anchorYMin = Mathf.Min(panelBL.y, panelTR.y);
            float anchorYMax = Mathf.Max(panelBL.y, panelTR.y);

            const float offsetX = 8f;
            const float offsetY = 0f;

            float panelW = _root.resolvedStyle.width;
            float panelH = _root.resolvedStyle.height;
            float ttW = _frame.resolvedStyle.width;
            float ttH = _frame.resolvedStyle.height;

            // 1차: 슬롯 우측에 툴팁 좌측 정렬, 슬롯 상단(panel Y down 기준 yMin)에 툴팁 상단 정렬
            float x = anchorXMax + offsetX;
            float y = anchorYMin + offsetY;

            // 우측 넘침 → 슬롯 좌측에 툴팁 우측 붙이기
            if (x + ttW > panelW)
            {
                x = anchorXMin - ttW - offsetX;
            }
            // 하단 넘침 → 툴팁 하단을 슬롯 하단(yMax)에 정렬
            if (y + ttH > panelH)
            {
                y = anchorYMax - ttH;
            }
            // 좌측/상단 클램프
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

            ApplyTierName(_eqName, itemName, tier);
            ApplyTierLabel(_eqTier, tier);

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
                SetStatLine(_eqDmgPhysics, FormatDamageLine("물리 피해", stats.Physics));
                SetStatLine(_eqDmgFire, FormatDamageLine("화염 피해", stats.Fire));
                SetStatLine(_eqDmgIce, FormatDamageLine("냉기 피해", stats.Ice));
                SetStatLine(_eqDmgLightning, FormatDamageLine("번개 피해", stats.Lightning));
                SetStatLine(_eqDmgPoison, FormatDamageLine("독 피해", stats.Poison));
                SetStatLine(_eqCrit, stats.CriRate > 0 ? $"치명타 확률: {stats.CriRate}%" : null);
                SetStatLine(_eqAs, stats.AttackSpeed > 0f ? $"초당 공격 횟수: {stats.AttackSpeed:F2}" : null);
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
                    AppendModLine(_eqBaseMods, mod, isImplicit: false);
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

        private static string? FormatDamageLine(string label, DamageRange range)
        {
            return range.Max > 0 ? $"{label}: {range.Min}~{range.Max}" : null;
        }

        private static void AppendModLine(VisualElement? container, ModInstance mod, bool isImplicit)
        {
            if (container == null || mod.Table == null) return;

            string body = FormatModLine(mod);

            Label line = new Label(body);
            line.AddToClassList(isImplicit ? "tooltip-statline-implicit" : "tooltip-modline");
            container.Add(line);
        }

        private static string FormatModLine(ModInstance mod)
        {
            if (mod.Table == null)
                return string.Empty;

            if (mod.Table.EffectType == GlobalEnum.ModEffectType.FlatStat
                && mod.Table.TargetStat == GlobalEnum.Stat.CriRate)
            {
                return $"치명타 확률 +{mod.Value1}%";
            }

            if (mod.Table.EffectType == GlobalEnum.ModEffectType.FlatStat
                && mod.Table.TargetStat == GlobalEnum.Stat.AttackSpeed
                && mod.Slot != GlobalEnum.ModSlot.Base)
            {
                return $"공격속도 {mod.Value1}% 증가";
            }

            if (mod.Table.EffectType == GlobalEnum.ModEffectType.IncreasedStat)
            {
                if (mod.Table.TargetStat == GlobalEnum.Stat.CriRate
                    || mod.Table.TargetStat == GlobalEnum.Stat.CriRateMul)
                    return $"치명타 확률 {mod.Value1}% 증가";

                if (mod.Table.TargetStat == GlobalEnum.Stat.AttackSpeed
                    || mod.Table.TargetStat == GlobalEnum.Stat.AttackSpeedMul)
                    return $"공격속도 {mod.Value1}% 증가";

                return $"{mod.Table.Name}: {mod.Value1}% 증가";
            }

            return mod.Value2 > 0
                ? $"{mod.Table.Name}: +{mod.Value1}~{mod.Value2}"
                : $"{mod.Table.Name}: +{mod.Value1}";
        }

        private static void AppendEnhancementLine(
            VisualElement? container,
            WeaponLocalEnhancementInstance enhancement)
        {
            if (container == null) return;

            string label = enhancement.StatType switch
            {
                GlobalEnum.WeaponLocalStatType.PhysicsDamage => "물리 피해 강화",
                GlobalEnum.WeaponLocalStatType.FireDamage => "화염 피해 강화",
                GlobalEnum.WeaponLocalStatType.IceDamage => "냉기 피해 강화",
                GlobalEnum.WeaponLocalStatType.LightningDamage => "번개 피해 강화",
                GlobalEnum.WeaponLocalStatType.PoisonDamage => "독 피해 강화",
                GlobalEnum.WeaponLocalStatType.CriticalRate => "치명타 확률 강화",
                GlobalEnum.WeaponLocalStatType.AttackSpeed => "공격속도 강화",
                _ => enhancement.StatType.ToString(),
            };

            Label line = new Label($"{label}: +{enhancement.Value}%");
            line.AddToClassList("tooltip-enhancement-line");
            container.Add(line);
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
            ShowContent(_contentSkillPage);
            _frame.style.display = DisplayStyle.Flex;
            UpdatePosition(anchorScreenRect);
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

            Hide();
        }
    }
}
