#nullable enable
using ARPG.Base;
using ARPG.Component;
using ARPG.Data;
using ARPG.Npc;
using ARPG.Tables;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Text = Unity.AppUI.UI.Text;
using GE = GlobalEnum;

namespace ARPG.UI
{
    /// <summary>
    /// NPC 검사 패널 — 장비/인벤토리/성향/스탯 풀 노출.
    /// 읽기 전용: 슬롯 호버 시 툴팁만, 클릭은 무시.
    /// 진입점: ServiceUIRouter (E키 근접 시), 또는 UIInn 방문자 카드(미리보기 진입).
    /// </summary>
    public class UINpcInspect : UIBaseForm
    {
        private UIDocument? _document;
        private IconButton? _closeBtn;
        private Text? _nameText;
        private Text? _jobText;
        private Text? _descText;
        private VisualElement? _equipGrid;
        private VisualElement? _invGrid;
        private VisualElement? _statList;
        private VisualElement? _personalityList;

        private int _entityId = -1;
        private NpcSaveData? _saveData;
        private VisualElement? _lastRoot;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[UINpcInspect] UIDocument 컴포넌트 없음 — prefab 확인 필요");
                return;
            }

            EnsureBound();
        }

        private void EnsureBound()
        {
            if (_document == null) return;
            VisualElement root = _document.rootVisualElement;
            if (root == null) return;
            if (root == _lastRoot) return;

            _lastRoot = root;

            _closeBtn = root.Q<IconButton>("close-btn");
            _nameText = root.Q<Text>("name-text");
            _jobText = root.Q<Text>("job-text");
            _descText = root.Q<Text>("desc-text");
            _equipGrid = root.Q<VisualElement>("equip-grid");
            _invGrid = root.Q<VisualElement>("inv-grid");
            _statList = root.Q<VisualElement>("stat-list");
            _personalityList = root.Q<VisualElement>("personality-list");

            if (_closeBtn != null) _closeBtn.clicked += () => Close();
        }

        public void Bind(int entityId)
        {
            EnsureBound();
            _entityId = entityId;
            _saveData = AR.s.Npc?.GetSaveData(entityId);

            if (_saveData == null)
            {
                Debug.LogWarning($"[UINpcInspect] Bind 실패 — entityId={entityId} saveData 없음");
                return;
            }

            RefreshAll();
        }

        /// <summary>
        /// Inn 방문자(IsActive=false) 미리보기용 — saveData를 직접 전달.
        /// UIInn에서 방문자 카드 클릭 시 호출.
        /// </summary>
        public void BindSaveData(NpcSaveData saveData)
        {
            EnsureBound();
            _entityId = saveData.EntityId;
            _saveData = saveData;
            RefreshAll();
        }

        public override void OnOpen()
        {
            base.OnOpen();
            EnsureBound();
            if (_saveData != null)
                RefreshAll();
        }

        private void RefreshAll()
        {
            if (_saveData == null) return;

            NpcTable? table = AR.s.Data?.GetNpc(_saveData.NpcTableId);
            string name = table?.Name ?? $"NPC #{_entityId}";
            string job = _saveData.JobType != GE.JobType.None
                ? _saveData.JobType.ToString()
                : (table?.JobType.ToString() ?? string.Empty);

            if (_nameText != null) _nameText.text = name;
            if (_jobText != null) _jobText.text = job;
            if (_descText != null) _descText.text = _saveData.Description ?? string.Empty;

            RebuildEquipGrid();
            RebuildInventoryGrid();
            RebuildPersonality();
            RebuildStats();
        }

        private void RebuildEquipGrid()
        {
            if (_equipGrid == null || _saveData == null) return;
            _equipGrid.Clear();

            int slotMax = (int)GE.EquipSlotType.Max;
            for (int i = 0; i < slotMax; i++)
            {
                GE.EquipSlotType slot = (GE.EquipSlotType)i;
                ItemData? item = _saveData.InventoryEquip[i];
                _equipGrid.Add(BuildSlotElement(item, slot.ToString()));
            }
        }

        private void RebuildInventoryGrid()
        {
            if (_invGrid == null || _saveData == null) return;
            _invGrid.Clear();

            for (int i = 0; i < _saveData.Inventory.Count; i++)
            {
                ItemData? item = _saveData.Inventory[i];
                _invGrid.Add(BuildSlotElement(item, $"Inv {i + 1}"));
            }
        }

        private VisualElement BuildSlotElement(ItemData? item, string emptyLabel)
        {
            VisualElement slot = new();
            slot.AddToClassList("npc-slot");

            VisualElement icon = new();
            icon.AddToClassList("npc-slot-icon");

            if (item != null && item.Table != null && string.IsNullOrEmpty(item.Table.SpriteName) == false)
            {
                Sprite? sprite = AR.s.Data?.GetSprite(item.Table.SpriteName);
                if (sprite != null)
                    icon.style.backgroundImage = new StyleBackground(sprite);
            }
            else
            {
                // 빈 슬롯 — 레이블만 표시
                Text emptyText = new() { text = emptyLabel };
                emptyText.AddToClassList("npc-slot-empty");
                icon.Add(emptyText);
            }

            slot.Add(icon);

            // 호버 툴팁 — 변경 불가, 호버만 (Q4=변경 불가)
            if (item != null)
            {
                VisualElement slotRef = slot;
                slot.userData = item;
                slot.RegisterCallback<MouseEnterEvent>(_ => AR.s.Tooltip.Show(slotRef.userData as ItemData, GetSlotScreenRect(slotRef)));
                slot.RegisterCallback<MouseLeaveEvent>(_ => AR.s.Tooltip.Hide());
            }

            return slot;
        }

        private void RebuildPersonality()
        {
            if (_personalityList == null) return;
            _personalityList.Clear();

            // NpcStatComponent (성향)은 ECS 활성 NPC만 갖고 있음. 비활성/방문자(saveData만 있음)는 표시 불가.
            if (_entityId < 0 || AR.s.Component == null)
                return;
            if (AR.s.Component.TryGetComponent<NpcStatComponent>(_entityId, out var nsc) == false)
                return;

            AddPersonalityRow("친화력", nsc.Friendliness);
            AddPersonalityRow("정직함", nsc.Honesty);
            AddPersonalityRow("탐욕", nsc.Greed);
            AddPersonalityRow("충성심", nsc.Loyalty);
            AddPersonalityRow("용기", nsc.Courage);
            AddPersonalityRow("호기심", nsc.Curiosity);
            AddPersonalityRow("자존심", nsc.Pride);
            AddPersonalityRow("인내심", nsc.Patience);
        }

        private void AddPersonalityRow(string label, int value)
        {
            if (_personalityList == null) return;
            VisualElement row = new();
            row.AddToClassList("npc-stat-row");
            Text lab = new() { text = label };
            lab.AddToClassList("npc-stat-label");
            Text val = new() { text = value.ToString() };
            val.AddToClassList("npc-stat-value");
            row.Add(lab);
            row.Add(val);
            _personalityList.Add(row);
        }

        private void RebuildStats()
        {
            if (_statList == null) return;
            _statList.Clear();

            if (_entityId < 0 || AR.s.Component == null)
                return;
            if (AR.s.Component.TryGetComponent<StatComponent>(_entityId, out var sc) == false)
                return;

            AddStatRow("힘", sc.FinalStr.ToString());
            AddStatRow("민첩", sc.FinalDex.ToString());
            AddStatRow("지능", sc.FinalInt.ToString());
            AddStatRow("체력", $"{sc.CurrentHp}/{sc.FinalMaxHp}");
            AddStatRow("마나", $"{sc.CurrentMp}/{sc.FinalMaxMp}");
            AddStatRow("체력 재생", sc.FinalHpGeneration.ToString());
            AddStatRow("마나 재생", sc.FinalMpGeneration.ToString());
            AddStatRow("물리 공격", $"{sc.FinalAttackMin} - {sc.FinalAttackMax}");
            AddStatRow("화염 공격", $"{sc.FinalFireAttackMin} - {sc.FinalFireAttackMax}");
            AddStatRow("냉기 공격", $"{sc.FinalIceAttackMin} - {sc.FinalIceAttackMax}");
            AddStatRow("번개 공격", $"{sc.FinalLightningAttackMin} - {sc.FinalLightningAttackMax}");
            AddStatRow("독 공격", $"{sc.FinalPoisonAttackMin} - {sc.FinalPoisonAttackMax}");
            AddStatRow("치명타 확률", $"{sc.FinalCriRate}%");
            AddStatRow("치명타 피해", $"{sc.FinalCriDamage}%");
            AddStatRow("공격 속도", $"{sc.FinalAttackSpeed}");
            AddStatRow("시전 속도", $"{sc.FinalCastSpeed}");
            AddStatRow("이동 속도", $"{sc.FinalMoveSpeed * sc.FinalMoveSpeedMul / 100f:F2}");
            AddStatRow("방어력", sc.FinalDefense.ToString());
            AddStatRow("화염 저항", $"{sc.FinalFireResist}% / {sc.FinalMaxFireResist}%");
            AddStatRow("냉기 저항", $"{sc.FinalIceResist}% / {sc.FinalMaxIceResist}%");
            AddStatRow("번개 저항", $"{sc.FinalLightningResist}% / {sc.FinalMaxLightningResist}%");
            AddStatRow("독 저항", $"{sc.FinalPoisonResist}% / {sc.FinalMaxPoisonResist}%");
            AddStatRow("회피", $"{sc.FinalEvasion}%");
            AddStatRow("막기", $"{sc.FinalBlockChance}%");
            AddStatRow("막기 감소", $"{sc.FinalBlockReduction}%");
            AddStatRow("스킬 데미지", $"{sc.FinalSkillDamage}%");
            AddStatRow("쿨타임 감소", $"{sc.FinalCooldownReduction}%");
            AddStatRow("생명력 흡수", $"{sc.FinalLifeSteal}%");
            AddStatRow("가시", sc.FinalThorns.ToString());
            AddStatRow("행운", sc.FinalLuck.ToString());
            AddStatRow("출혈률", sc.FinalBloodingRate.ToString());
            AddStatRow("점화율", sc.FinalIgniteRate.ToString());
        }

        private void AddStatRow(string label, string value)
        {
            if (_statList == null) return;
            VisualElement row = new();
            row.AddToClassList("npc-stat-row");
            Text lab = new() { text = label };
            lab.AddToClassList("npc-stat-label");
            Text val = new() { text = value };
            val.AddToClassList("npc-stat-value");
            row.Add(lab);
            row.Add(val);
            _statList.Add(row);
        }

        /// <summary>
        /// UI Toolkit VisualElement → Screen Space Rect. 툴팁 anchor용.
        /// UIShopMerchant/UISkillBook의 동명 헬퍼와 동일 정책 (TODO: 공통 유틸 추출).
        /// </summary>
        private static Rect GetSlotScreenRect(VisualElement slot)
        {
            if (slot == null) return Rect.zero;
            IPanel? panel = slot.panel;
            if (panel == null) return Rect.zero;

            VisualElement visualTree = panel.visualTree;
            float panelW = visualTree.resolvedStyle.width;
            float panelH = visualTree.resolvedStyle.height;
            if (panelW <= 0f || panelH <= 0f) return Rect.zero;

            float scaleX = Screen.width / panelW;
            float scaleY = Screen.height / panelH;

            Rect bound = slot.worldBound;
            float xMin = bound.xMin * scaleX;
            float xMax = bound.xMax * scaleX;
            float yMaxScreen = Screen.height - bound.yMin * scaleY;
            float yMinScreen = Screen.height - bound.yMax * scaleY;

            return new Rect(xMin, yMinScreen, xMax - xMin, yMaxScreen - yMinScreen);
        }
    }
}
