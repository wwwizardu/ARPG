#nullable enable
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Data;
using ARPG.Item;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Button = Unity.AppUI.UI.Button;
using Text = Unity.AppUI.UI.Text;

namespace ARPG.UI
{
    /// <summary>
    /// 미장착 장비와 마법부여 작업(품질 다시 굴리기·진화·랜덤 교체·추가)만 선택하는 Forge UI.
    /// 후보 구성·옵션 롤·비용·데이터 변경은 ItemEnchantmentService만 수행한다.
    /// </summary>
    public class UIForge : UIBaseForm
    {
        private UIDocument? _document;
        private Button? _disassembleBtn;
        private Button? _replaceBtn;
        private Button? _addBtn;
        private Button? _evolveBtn;
        private Button? _qualityBtn;
        private IconButton? _closeBtn;
        private Text? _tierText;
        private Text? _selectedText;
        private Text? _replaceCostText;
        private Text? _replaceStatusText;
        private Text? _addCostText;
        private Text? _addStatusText;
        private Text? _evolveCostText;
        private Text? _evolveStatusText;
        private Text? _qualityCostText;
        private Text? _qualityStatusText;
        private Text? _resultTitle;
        private VisualElement? _selectedOptions;
        private VisualElement? _resultSection;
        private VisualElement? _beforeOptions;
        private VisualElement? _afterOptions;

        private int _forgeEntityId = -1;
        private GlobalEnum.ForgeTier _tier = GlobalEnum.ForgeTier.None;
        private int _selectedSlotIndex = -1;
        private int _selectedItemInstanceId = -1;
        private bool _testBinding;
        private ItemEnchantmentResult? _lastResult;
        private VisualElement? _lastRoot;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);
            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[UIForge] UIDocument 컴포넌트 없음");
                return;
            }
            EnsureBound();
        }

        public void Bind(int forgeEntityId)
        {
            EnsureBound();
            _forgeEntityId = forgeEntityId;
            _tier = GlobalEnum.ForgeTier.None;
            _testBinding = false;
            ResetSelections();

            if (AR.s.Item.Enchantment.TryResolveForgeTier(forgeEntityId, out _tier) == false)
            {
                Debug.LogWarning($"[UIForge] Bind 실패 — entityId={forgeEntityId}는 유효한 Forge가 아님");
            }
            RefreshAll();
        }

        public void BindForTest(GlobalEnum.ForgeTier tier = GlobalEnum.ForgeTier.Premium)
        {
            EnsureBound();
#if UNITY_EDITOR
            _forgeEntityId = -1;
            _tier = tier;
            _testBinding = true;
            ResetSelections();
            RefreshAll();
            Debug.Log($"[UIForge] BindForTest — Editor 테스트 모드 ({tier})");
#else
            _forgeEntityId = -1;
            _tier = GlobalEnum.ForgeTier.None;
            _testBinding = false;
            ResetSelections();
            RefreshAll();
            Debug.LogWarning("[UIForge] BindForTest는 Unity Editor에서만 사용할 수 있습니다.");
#endif
        }

        public override void OnOpen()
        {
            base.OnOpen();
            EnsureBound();
            ResetSelections();
            if (_testBinding == false && _forgeEntityId >= 0)
            {
                if (AR.s.Item.Enchantment.TryResolveForgeTier(_forgeEntityId,
                        out GlobalEnum.ForgeTier resolved) == true)
                    _tier = resolved;
                else
                    _tier = GlobalEnum.ForgeTier.None;
            }
            RefreshAll();
        }

        private void EnsureBound()
        {
            if (_document == null)
                return;
            VisualElement root = _document.rootVisualElement;
            if (root == null || root == _lastRoot)
                return;

            _lastRoot = root;
            _disassembleBtn = root.Q<Button>("disassemble-btn");
            _replaceBtn = root.Q<Button>("replace-btn");
            _addBtn = root.Q<Button>("add-btn");
            _evolveBtn = root.Q<Button>("evolve-btn");
            _qualityBtn = root.Q<Button>("quality-btn");
            _closeBtn = root.Q<IconButton>("close-btn");
            _tierText = root.Q<Text>("tier-text");
            _selectedText = root.Q<Text>("selected-text");
            _replaceCostText = root.Q<Text>("replace-cost-text");
            _replaceStatusText = root.Q<Text>("replace-status-text");
            _addCostText = root.Q<Text>("add-cost-text");
            _addStatusText = root.Q<Text>("add-status-text");
            _evolveCostText = root.Q<Text>("evolve-cost-text");
            _evolveStatusText = root.Q<Text>("evolve-status-text");
            _qualityCostText = root.Q<Text>("quality-cost-text");
            _qualityStatusText = root.Q<Text>("quality-status-text");
            _resultTitle = root.Q<Text>("result-title");
            _selectedOptions = root.Q<VisualElement>("selected-options");
            _resultSection = root.Q<VisualElement>("result-section");
            _beforeOptions = root.Q<VisualElement>("before-options");
            _afterOptions = root.Q<VisualElement>("after-options");

            if (_disassembleBtn != null)
                _disassembleBtn.clicked += OnClickDisassemble;
            if (_replaceBtn != null)
                _replaceBtn.clicked += OnClickReplace;
            if (_addBtn != null)
                _addBtn.clicked += OnClickAdd;
            if (_evolveBtn != null)
                _evolveBtn.clicked += OnClickEvolve;
            if (_qualityBtn != null)
                _qualityBtn.clicked += OnClickQuality;
            if (_closeBtn != null)
                _closeBtn.clicked += OnClickClose;
        }

        private void OnClickClose()
        {
            Close();
        }

        private void OnClickDisassemble()
        {
            Debug.Log("[UIForge] 분해는 이번 MVP 범위가 아닙니다.");
        }

        private void OnClickReplace()
        {
            ExecuteOperation(GlobalEnum.ItemEnchantmentOperation.ReplaceAffix);
        }

        private void OnClickAdd()
        {
            ExecuteOperation(GlobalEnum.ItemEnchantmentOperation.AddAffix);
        }

        private void OnClickEvolve()
        {
            ExecuteOperation(GlobalEnum.ItemEnchantmentOperation.EvolveToMagic);
        }

        private void OnClickQuality()
        {
            ExecuteOperation(GlobalEnum.ItemEnchantmentOperation.RerollQuality);
        }

        private void ExecuteOperation(GlobalEnum.ItemEnchantmentOperation operation)
        {
            if (GetSelectedItem() == null)
                return;

            ItemEnchantmentRequest request = CreateRequest(operation);
            ItemEnchantmentResult result;
#if UNITY_EDITOR
            result = _testBinding == true
                ? AR.s.Item.Enchantment.ExecuteForTest(request, _tier)
                : AR.s.Item.Enchantment.Execute(request);
#else
            result = AR.s.Item.Enchantment.Execute(request);
#endif
            if (result.Success == false)
            {
                _lastResult = null;
                RefreshAll();
                Text? status = GetStatusText(operation);
                if (status != null)
                    status.text = GetFailureMessage(result.FailureReason);
                return;
            }

            _lastResult = result;
            RefreshAll();
        }

        private void OnSelectSlot(int slotIndex)
        {
            ItemData? item = AR.s.Player?.Inventory?.GetItemBySlotIndex(slotIndex);
            if (IsEnchantableInventoryEquipment(item) == false)
                return;

            _selectedSlotIndex = slotIndex;
            _selectedItemInstanceId = item!.ItemInstanceId;
            _lastResult = null;
            RefreshAll();
        }

        private void RefreshAll()
        {
            RefreshTier();
            RebuildInventoryGrid();
            RebuildSelectedOptions();
            RefreshOperations();
            RebuildResult();
            _disassembleBtn?.SetEnabled(false);
        }

        private void RefreshTier()
        {
            if (_tierText == null)
                return;
            _tierText.text = _tier switch
            {
                GlobalEnum.ForgeTier.Premium => "제련소 Tier 3 — MVP 비용·후보는 Tier 1과 동일",
                GlobalEnum.ForgeTier.Standard => "제련소 Tier 2 — MVP 비용·후보는 Tier 1과 동일",
                GlobalEnum.ForgeTier.Basic => "제련소 Tier 1 — 품질·진화·옵션 변경·추가 사용 가능",
                _ => "마법부여 불가 — 유효한 제련소가 없습니다",
            };
        }

        private void RebuildInventoryGrid()
        {
            if (_document == null)
                return;
            VisualElement? grid = _document.rootVisualElement.Q<VisualElement>("inventory-grid");
            if (grid == null)
                return;
            grid.Clear();

            Inventory? inventory = AR.s.Player?.Inventory;
            if (inventory == null || inventory.Items == null)
            {
                AddMessage(grid, "플레이어 인벤토리가 없습니다", "forge-inv-empty-text");
                return;
            }

            int equipmentCount = 0;
            List<ItemData?> items = inventory.Items;
            for (int i = 0; i < items.Count; i++)
            {
                int slotIndex = i;
                ItemData? item = items[i];
                VisualElement slot = new VisualElement();
                slot.AddToClassList("forge-inv-slot");

                if (IsEnchantableInventoryEquipment(item) == true)
                {
                    slot.AddToClassList("forge-inv-slot-equipment");
                    if (slotIndex == _selectedSlotIndex)
                        slot.AddToClassList("forge-inv-slot-selected");

                    VisualElement icon = new VisualElement();
                    icon.AddToClassList("forge-inv-icon");
                    Sprite? sprite = string.IsNullOrEmpty(item!.Table!.SpriteName) == false
                        ? AR.s.Data.GetSprite(item.Table.SpriteName)
                        : null;
                    if (sprite != null)
                        icon.style.backgroundImage = new StyleBackground(sprite);
                    slot.Add(icon);
                    slot.RegisterCallback<ClickEvent>(_ => OnSelectSlot(slotIndex));
                    slot.tooltip = item.Table.Name;
                    equipmentCount++;
                }
                else if (item != null)
                {
                    slot.AddToClassList("forge-inv-slot-disabled");
                    slot.tooltip = item.Table?.ItemType == GlobalEnum.ItemType.Equipment
                        ? "인스턴스 1개로 분리된 장비만 마법부여할 수 있습니다"
                        : string.Empty;
                }
                grid.Add(slot);
            }
            if (equipmentCount == 0)
                AddMessage(grid, "마법부여할 미장착 장비가 없습니다", "forge-inv-empty-text");
        }

        private void RebuildSelectedOptions()
        {
            if (_selectedOptions == null)
                return;
            _selectedOptions.Clear();
            ItemData? selected = GetSelectedItem();
            if (_selectedText != null)
                _selectedText.text = selected != null
                    ? FormatSelectedText(selected)
                    : "장비를 선택하세요";
            if (selected == null)
            {
                AddMessage(_selectedOptions, "선택된 장비 없음", "forge-list-empty");
                return;
            }
            AddOptionLines(_selectedOptions,
                AR.s.Item.Enchantment.GetOptionSnapshots(selected));
        }

        private void RefreshOperations()
        {
            RefreshOperation(GlobalEnum.ItemEnchantmentOperation.ReplaceAffix,
                _replaceBtn, _replaceCostText, _replaceStatusText);
            RefreshOperation(GlobalEnum.ItemEnchantmentOperation.AddAffix,
                _addBtn, _addCostText, _addStatusText);
            RefreshOperation(GlobalEnum.ItemEnchantmentOperation.EvolveToMagic,
                _evolveBtn, _evolveCostText, _evolveStatusText);
            RefreshOperation(GlobalEnum.ItemEnchantmentOperation.RerollQuality,
                _qualityBtn, _qualityCostText, _qualityStatusText);
        }

        private void RefreshOperation(GlobalEnum.ItemEnchantmentOperation operation,
            Button? button, Text? costText, Text? statusText)
        {
            button?.SetEnabled(false);
            if (costText != null)
                costText.text = "비용: -";
            if (statusText != null)
                statusText.text = GetSelectedItem() == null ? "먼저 장비를 선택하세요." : string.Empty;
            if (GetSelectedItem() == null)
                return;

            ItemEnchantmentRequest request = CreateRequest(operation);
            ItemEnchantmentPreview preview;
#if UNITY_EDITOR
            preview = _testBinding == true
                ? AR.s.Item.Enchantment.InspectForTest(request, _tier)
                : AR.s.Item.Enchantment.Inspect(request);
#else
            preview = AR.s.Item.Enchantment.Inspect(request);
#endif
            if (costText != null && preview.MaterialItem != null)
            {
                costText.text = $"비용: {preview.MaterialItem.Name} " +
                    $"{preview.RequiredMaterialCount}개 (보유 {preview.OwnedMaterialCount}) + " +
                    $"{preview.FinalGoldCost} Gold (보유 {preview.OwnedGold})";
            }
            else if (costText != null && operation == GlobalEnum.ItemEnchantmentOperation.RerollQuality
                && preview.FailureReason != ItemEnchantmentFailureReason.CostDataInvalid)
            {
                costText.text = $"비용: {preview.FinalGoldCost} Gold (보유 {preview.OwnedGold})";
            }
            if (statusText != null)
            {
                string magicNote = preview.UsesMagicRule == true ? " · 마법 규칙(높은 티어 2배)" : string.Empty;
                if (preview.CanApply == false)
                    statusText.text = GetFailureMessage(preview.FailureReason);
                else if (operation == GlobalEnum.ItemEnchantmentOperation.ReplaceAffix)
                    statusText.text = $"교체 가능 옵션 {preview.ReplaceableAffixCount}개 · 랜덤 대체 후보 {preview.RandomCandidateCount}개{magicNote}";
                else if (operation == GlobalEnum.ItemEnchantmentOperation.RerollQuality)
                    statusText.text = $"현재 품질 {preview.CurrentQuality} → 0~{EquipmentQualityRules.MAX_QUALITY} 중 새 값";
                else
                    statusText.text = $"랜덤 후보 {preview.RandomCandidateCount}개 · Prefix {preview.PrefixCount}/{preview.SlotLimit} · Postfix {preview.PostfixCount}/{preview.SlotLimit}{magicNote}";
            }
            button?.SetEnabled(preview.CanApply);
        }

        private void RebuildResult()
        {
            if (_resultSection == null || _beforeOptions == null || _afterOptions == null)
                return;
            if (_lastResult == null || _lastResult.Success == false)
            {
                _resultSection.AddToClassList("forge-hidden");
                _beforeOptions.Clear();
                _afterOptions.Clear();
                return;
            }

            _resultSection.RemoveFromClassList("forge-hidden");
            if (_resultTitle != null)
            {
                if (_lastResult.ResolvedOperation
                    == GlobalEnum.ItemEnchantmentOperation.ReplaceAffix)
                {
                    _resultTitle.text = $"랜덤 옵션 변경 성공 — " +
                        $"{_lastResult.ReplacedMod?.DisplayText} → {_lastResult.AppliedMod?.DisplayText} / " +
                        $"재료 {_lastResult.ConsumedMaterialCount}개 + {_lastResult.ConsumedGold} Gold";
                }
                else if (_lastResult.ResolvedOperation
                    == GlobalEnum.ItemEnchantmentOperation.RerollQuality)
                {
                    _resultTitle.text = $"품질 다시 굴리기 — {_lastResult.PreviousQuality} → {_lastResult.NewQuality} / " +
                        $"{_lastResult.ConsumedGold} Gold";
                }
                else if (_lastResult.ResolvedOperation
                    == GlobalEnum.ItemEnchantmentOperation.EvolveToMagic)
                {
                    _resultTitle.text = $"마법 장비로 진화 — {_lastResult.AppliedMod?.DisplayText} / 재료 " +
                        $"{_lastResult.ConsumedMaterialCount}개 + {_lastResult.ConsumedGold} Gold";
                }
                else
                {
                    _resultTitle.text = $"랜덤 옵션 추가 성공 — " +
                        $"{_lastResult.AppliedMod?.DisplayText} / 재료 " +
                        $"{_lastResult.ConsumedMaterialCount}개 + {_lastResult.ConsumedGold} Gold";
                }
            }
            _beforeOptions.Clear();
            _afterOptions.Clear();
            AddOptionLines(_beforeOptions, _lastResult.BeforeOptions);
            AddOptionLines(_afterOptions, _lastResult.AfterOptions);
        }

        private Text? GetStatusText(GlobalEnum.ItemEnchantmentOperation operation)
        {
            return operation switch
            {
                GlobalEnum.ItemEnchantmentOperation.ReplaceAffix => _replaceStatusText,
                GlobalEnum.ItemEnchantmentOperation.EvolveToMagic => _evolveStatusText,
                GlobalEnum.ItemEnchantmentOperation.RerollQuality => _qualityStatusText,
                _ => _addStatusText,
            };
        }

        /// <summary>"선택: 가죽 장화 · 일반 · 품질 12 · 아이템 레벨 40 (Item #123)"</summary>
        private static string FormatSelectedText(ItemData selected)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("선택: ").Append(selected.Table?.Name);
            EquipmentData? equipment = selected.Equipment;
            if (equipment != null)
            {
                sb.Append(" · ").Append(EquipmentOptionText.GetGradeName(equipment.GetGrade()));
                if (equipment.Quality > 0)
                    sb.Append(" · 품질 ").Append(equipment.Quality);
                if (equipment.ItemLevel > 0)
                    sb.Append(" · 아이템 레벨 ").Append(equipment.ItemLevel);
            }
            sb.Append(" (Item #").Append(selected.ItemInstanceId).Append(')');
            return sb.ToString();
        }

        private ItemData? GetSelectedItem()
        {
            if (_selectedSlotIndex < 0 || _selectedItemInstanceId < 0)
                return null;
            ItemData? item = AR.s.Player?.Inventory?.GetItemBySlotIndex(_selectedSlotIndex);
            return IsEnchantableInventoryEquipment(item) == true
                && item!.ItemInstanceId == _selectedItemInstanceId
                ? item
                : null;
        }

        private ItemEnchantmentRequest CreateRequest(
            GlobalEnum.ItemEnchantmentOperation operation)
        {
            return new ItemEnchantmentRequest
            {
                ForgeEntityId = _forgeEntityId,
                TargetInventorySlot = _selectedSlotIndex,
                TargetItemInstanceId = _selectedItemInstanceId,
                Operation = operation,
            };
        }

        private void ResetSelections()
        {
            _selectedSlotIndex = -1;
            _selectedItemInstanceId = -1;
            _lastResult = null;
        }

        private static bool IsEnchantableInventoryEquipment(ItemData? item)
        {
            return item != null
                && item.Table != null
                && item.Table.ItemType == GlobalEnum.ItemType.Equipment
                && item.Equipment != null
                && item.Quantity == 1;
        }

        private static void AddOptionLines(VisualElement container,
            IReadOnlyList<EquipmentOptionSnapshot> options)
        {
            if (options.Count == 0)
            {
                AddMessage(container, "옵션 없음", "forge-list-empty");
                return;
            }
            for (int i = 0; i < options.Count; i++)
            {
                Text line = new Text { text = options[i].DisplayText };
                line.AddToClassList("forge-option-line");
                container.Add(line);
            }
        }

        private static void AddMessage(VisualElement container, string message,
            string className)
        {
            Text text = new Text { text = message };
            text.AddToClassList(className);
            container.Add(text);
        }

        private static string GetFailureMessage(ItemEnchantmentFailureReason reason)
        {
            return reason switch
            {
                ItemEnchantmentFailureReason.None => string.Empty,
                ItemEnchantmentFailureReason.ForgeNotFound => "유효한 제련소를 확인할 수 없습니다.",
                ItemEnchantmentFailureReason.ForgeTierInsufficient => "Tier 1 이상 제련소가 필요합니다.",
                ItemEnchantmentFailureReason.TargetNotFound => "선택한 장비를 인벤토리에서 찾을 수 없습니다.",
                ItemEnchantmentFailureReason.TargetChanged => "장비 슬롯이 변경되었습니다. 다시 선택하세요.",
                ItemEnchantmentFailureReason.TargetQuantityInvalid => "인스턴스 1개로 분리된 장비만 마법부여할 수 있습니다.",
                ItemEnchantmentFailureReason.NotEquipment => "미장착 장비만 마법부여할 수 있습니다.",
                ItemEnchantmentFailureReason.ImmutableSlot => "Base·Implicit·무기 고유 강화는 변경할 수 없습니다.",
                ItemEnchantmentFailureReason.AmbiguousExistingGroup => "같은 Slot·Group 옵션이 여러 개인 구 세이브 데이터 오류입니다.",
                ItemEnchantmentFailureReason.AffixLimitReached => "Prefix와 Postfix가 모두 가득 찼습니다.",
                ItemEnchantmentFailureReason.NoAffixAvailable => "변경할 Prefix/Postfix 옵션이 없습니다.",
                ItemEnchantmentFailureReason.NoRandomCandidate => "조건에 맞는 다른 랜덤 옵션 후보가 없습니다.",
                ItemEnchantmentFailureReason.MaterialMissing => "필요한 마법부여 재료가 부족합니다.",
                ItemEnchantmentFailureReason.GoldInsufficient => "Gold가 부족합니다.",
                ItemEnchantmentFailureReason.IncompatibleMod => "장비 옵션 데이터가 올바르지 않습니다.",
                ItemEnchantmentFailureReason.EffectNotImplemented => "실제 전투 소비 경로가 확인되지 않은 효과입니다.",
                ItemEnchantmentFailureReason.DuplicateGroup => "Group이 없거나 중복 규칙이 잘못된 Mod 데이터입니다.",
                ItemEnchantmentFailureReason.TierUnavailable => "아이템 레벨에 맞는 결과 Tier가 없습니다.",
                ItemEnchantmentFailureReason.RequiresNormalGrade => "옵션이 없는 일반 장비에만 쓸 수 있습니다.",
                ItemEnchantmentFailureReason.RequiresAffixedGrade => "일반 장비는 진화의 오브로 먼저 마법 장비로 만드세요.",
                ItemEnchantmentFailureReason.UnsupportedOperation => "아직 지원하지 않는 마법부여 작업입니다.",
                ItemEnchantmentFailureReason.CostDataInvalid => "마법부여 비용 테이블을 사용할 수 없습니다.",
                _ => "마법부여 처리에 실패했습니다. 데이터는 변경되지 않았습니다.",
            };
        }
    }
}
