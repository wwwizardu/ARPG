#nullable enable
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Component;
using ARPG.Npc;
using ARPG.Tables;
using ARPG.Village;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using IconButton = Unity.AppUI.UI.IconButton;
using Text = Unity.AppUI.UI.Text;

namespace ARPG.UI
{
    /// <summary>
    /// 마을 정보 UI — 화면 중앙 모달.
    /// 플레이어가 마을 안에 있을 때 V 키로 열림.
    /// 좌측: 상태/리소스/다음 건설/진행 중 빌드
    /// 우측: NPC 목록
    /// </summary>
    public class UIVillage : UIBaseForm
    {
        private const int DEFAULT_RESOURCE_CAP = 50;

        private static readonly GlobalEnum.ItemType[] DISPLAYED_RESOURCES =
        {
            GlobalEnum.ItemType.Food,
            GlobalEnum.ItemType.Wood,
            GlobalEnum.ItemType.Stone,
            GlobalEnum.ItemType.Gold,
            GlobalEnum.ItemType.Copper,
            GlobalEnum.ItemType.Iron,
            GlobalEnum.ItemType.Herb,
        };

        private UIDocument? _document;
        private VisualElement? _lastRoot;

        private Heading? _titleText;
        private Text? _subtitleText;
        private IconButton? _closeBtn;

        private Text? _stageText;
        private Text? _populationText;
        private Text? _threatText;
        private Text? _placedText;

        private VisualElement? _resourceList;
        private VisualElement? _nextBuildCard;
        private Text? _nextBuildName;
        private Text? _nextBuildDetail;
        private VisualElement? _activeBuildList;
        private VisualElement? _npcList;

        private int _villageId = -1;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);

            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                Debug.LogError("[UIVillage] UIDocument 컴포넌트 없음 — prefab 확인 필요");
                return;
            }

            EnsureBound();
        }

        public override void OnOpen()
        {
            base.OnOpen();
            EnsureBound();
            ResolveCurrentVillage();
            RefreshAll();
        }

        /// <summary>
        /// UIDocument는 SetActive 토글 시마다 rootVisualElement를 재구성하므로
        /// root 변경을 감지해 매번 재바인딩.
        /// </summary>
        private void EnsureBound()
        {
            if (_document == null) return;
            VisualElement root = _document.rootVisualElement;
            if (root == null) return;
            if (root == _lastRoot) return;

            _lastRoot = root;

            _titleText      = root.Q<Heading>("title-text");
            _subtitleText   = root.Q<Text>("subtitle-text");
            _closeBtn       = root.Q<IconButton>("close-btn");

            _stageText      = root.Q<Text>("stage-text");
            _populationText = root.Q<Text>("population-text");
            _threatText     = root.Q<Text>("threat-text");
            _placedText     = root.Q<Text>("placed-text");

            _resourceList    = root.Q<VisualElement>("resource-list");
            _nextBuildCard   = root.Q<VisualElement>("next-build-card");
            _nextBuildName   = root.Q<Text>("next-build-name");
            _nextBuildDetail = root.Q<Text>("next-build-detail");
            _activeBuildList = root.Q<VisualElement>("active-build-list");
            _npcList         = root.Q<VisualElement>("npc-list");

            if (_closeBtn != null) _closeBtn.clicked += () => Close();
        }

        // ========== 마을 결정 ==========

        /// <summary>
        /// 플레이어가 현재 위치한 마을을 찾아 _villageId 세팅.
        /// 마을 밖이면 -1.
        /// </summary>
        private void ResolveCurrentVillage()
        {
            _villageId = -1;

            if (AR.s.Data == null) return;
            int playerEntityId = AR.s.Data.CurrentPlayerEntityId;
            if (playerEntityId < 0) return;

            if (AR.s.Component.TryGetComponent<TransformComponent>(playerEntityId, out var tr) == false)
                return;

            int tileX = Mathf.FloorToInt(tr.Position.x);
            int tileY = Mathf.FloorToInt(tr.Position.y);
            _villageId = AR.s.Village.FindVillageContaining(tileX, tileY);
        }

        // ========== 갱신 ==========

        private void RefreshAll()
        {
            VillageData? v = _villageId >= 0 ? AR.s.Village.GetVillage(_villageId) : null;

            if (v == null)
            {
                ShowOutsideVillageState();
                return;
            }

            // 헤더
            VillageTable? table = AR.s.Data.GetVillageTable(v.TableId);
            if (_titleText != null)
                _titleText.text = !string.IsNullOrEmpty(table?.Name) ? table!.Name : $"마을 {v.VillageId}";
            if (_subtitleText != null)
                _subtitleText.text = $"좌표 ({Mathf.FloorToInt(v.PositionX)}, {Mathf.FloorToInt(v.PositionY)})";

            // 상태
            if (_stageText != null)      _stageText.text = AR.s.Data.GetVillageStage(v.Stage)?.Name ?? v.Stage.ToString();
            if (_populationText != null) _populationText.text = $"{v.Population}명";
            if (_threatText != null)     _threatText.text = v.ThreatLevel.ToString("F1");
            if (_placedText != null)     _placedText.text = $"{v.PlacedObjectTypeIds.Count}개";

            // 리소스
            RebuildResourceList(v);

            // 다음 건설
            RebuildNextBuild(v);

            // 진행 중 빌드
            RebuildActiveBuildList(v);

            // NPC
            RebuildNpcList(v);
        }

        private void ShowOutsideVillageState()
        {
            if (_titleText != null)      _titleText.text = "마을 정보";
            if (_subtitleText != null)   _subtitleText.text = "현재 마을 안에 있지 않습니다";
            if (_stageText != null)      _stageText.text = "-";
            if (_populationText != null) _populationText.text = "-";
            if (_threatText != null)     _threatText.text = "-";
            if (_placedText != null)     _placedText.text = "-";

            if (_resourceList != null)
            {
                _resourceList.Clear();
                AddEmptyText(_resourceList, "마을 안에서 V를 눌러주세요");
            }
            if (_nextBuildName != null)   _nextBuildName.text = "-";
            if (_nextBuildDetail != null) _nextBuildDetail.text = "";
            if (_activeBuildList != null)
            {
                _activeBuildList.Clear();
                AddEmptyText(_activeBuildList, "");
            }
            if (_npcList != null)
            {
                _npcList.Clear();
                AddEmptyText(_npcList, "");
            }
        }

        private void RebuildResourceList(VillageData v)
        {
            if (_resourceList == null) return;
            _resourceList.Clear();

            for (int i = 0; i < DISPLAYED_RESOURCES.Length; i++)
            {
                GlobalEnum.ItemType type = DISPLAYED_RESOURCES[i];
                int amount = v.Resources.TryGetValue(type, out int a) ? a : 0;
                int cap = v.ResourceCaps.TryGetValue(type, out int c) ? c : DEFAULT_RESOURCE_CAP;

                // Gold/Copper/Iron/Herb는 0이고 cap도 기본이라면 표시 생략 (정보 노이즈 줄이기)
                bool isCore = type == GlobalEnum.ItemType.Food
                    || type == GlobalEnum.ItemType.Wood
                    || type == GlobalEnum.ItemType.Stone;
                if (isCore == false && amount == 0)
                    continue;

                VisualElement row = new();
                row.AddToClassList("village-resource-row");

                Text name = new() { text = ResourceToKorean(type) };
                name.AddToClassList("village-resource-name");
                row.Add(name);

                Text value = new() { text = $"{amount} / {cap}" };
                value.AddToClassList("village-resource-amount");
                row.Add(value);

                _resourceList.Add(row);
            }
        }

        private void RebuildNextBuild(VillageData v)
        {
            if (_nextBuildName == null || _nextBuildDetail == null) return;

            List<int> ranked = VillageNeedsEvaluator.GetRankedCandidates(v);
            if (ranked.Count == 0)
            {
                _nextBuildName.text = "(없음)";
                _nextBuildDetail.text = "현재 우선 건설할 후보가 없습니다";
                return;
            }

            int nextId = ranked[0];
            BuildableItemTable? t = AR.s.Data.GetBuildableItem(nextId);
            if (t == null)
            {
                _nextBuildName.text = $"TableId {nextId}";
                _nextBuildDetail.text = "";
                return;
            }

            _nextBuildName.text = t.Name;

            string cost = FormatBuildCost(t);
            string hours = t.BuildHours > 0f ? $"{t.BuildHours:F1}h" : "-";
            _nextBuildDetail.text = $"비용: {cost}  ·  소요 {hours}";
        }

        private void RebuildActiveBuildList(VillageData v)
        {
            if (_activeBuildList == null) return;
            _activeBuildList.Clear();

            if (v.ActiveBuildTasks.Count == 0)
            {
                AddEmptyText(_activeBuildList, "(진행 중인 건설 없음)");
                return;
            }

            for (int i = 0; i < v.ActiveBuildTasks.Count; i++)
            {
                BuildTaskSnapshot snap = v.ActiveBuildTasks[i];
                BuildableItemTable? t = AR.s.Data.GetBuildableItem(snap.TableId);
                string name = t != null ? t.Name : $"TableId {snap.TableId}";

                float total = t != null && t.BuildHours > 0f ? t.BuildHours : 1f;
                float pct = Mathf.Clamp01(snap.AccumulatedHours / total) * 100f;

                VisualElement row = new();
                row.AddToClassList("village-active-build-row");

                Text n = new() { text = $"{name} ({snap.TileX}, {snap.TileY})" };
                n.AddToClassList("village-active-build-name");
                row.Add(n);

                Text p = new() { text = $"진행 {pct:F0}% ({snap.AccumulatedHours:F1} / {total:F1}h)" };
                p.AddToClassList("village-active-build-progress");
                row.Add(p);

                _activeBuildList.Add(row);
            }
        }

        private void RebuildNpcList(VillageData v)
        {
            if (_npcList == null) return;
            _npcList.Clear();

            if (v.NpcEntityIds.Count == 0)
            {
                AddEmptyText(_npcList, "(NPC 없음)");
                return;
            }

            for (int i = 0; i < v.NpcEntityIds.Count; i++)
            {
                int entityId = v.NpcEntityIds[i];
                NpcSaveData? save = AR.s.Npc.GetSaveData(entityId);
                if (save == null) continue;

                NpcTable? table = AR.s.Data.GetNpc(save.NpcTableId);
                string displayName = table != null ? table.Name : $"NPC {save.NpcTableId}";

                VisualElement card = new();
                card.AddToClassList("village-npc-card");

                VisualElement portrait = new();
                portrait.AddToClassList("village-npc-portrait");
                card.Add(portrait);

                VisualElement info = new();
                info.AddToClassList("village-npc-info");

                Text n = new() { text = displayName };
                n.AddToClassList("village-npc-name");
                info.Add(n);

                string job = JobTypeToKorean(save.JobType);
                Text meta = new() { text = $"{job}  ·  숙련 {save.SkillLevel}" };
                meta.AddToClassList("village-npc-meta");
                info.Add(meta);

                if (save.Status == NpcStatus.InnVisitor)
                {
                    Text status = new() { text = "여관 방문자" };
                    status.AddToClassList("village-npc-status-visitor");
                    info.Add(status);
                }

                card.Add(info);
                _npcList.Add(card);
            }
        }

        private static void AddEmptyText(VisualElement parent, string message)
        {
            Text t = new() { text = message };
            t.AddToClassList("village-empty-text");
            parent.Add(t);
        }

        // ========== 헬퍼 ==========

        private static string FormatBuildCost(BuildableItemTable t)
        {
            List<string> parts = new();
            if (t.Cost_Wood > 0)  parts.Add($"목재 {t.Cost_Wood}");
            if (t.Cost_Stone > 0) parts.Add($"석재 {t.Cost_Stone}");
            if (t.Cost_Metal > 0) parts.Add($"금속 {t.Cost_Metal}");
            if (parts.Count == 0) return "무료";
            return string.Join(", ", parts);
        }

        private static string ResourceToKorean(GlobalEnum.ItemType type)
        {
            return type switch
            {
                GlobalEnum.ItemType.Food   => "식량",
                GlobalEnum.ItemType.Wood   => "목재",
                GlobalEnum.ItemType.Stone  => "석재",
                GlobalEnum.ItemType.Gold   => "골드",
                GlobalEnum.ItemType.Copper => "구리",
                GlobalEnum.ItemType.Iron   => "철",
                GlobalEnum.ItemType.Herb   => "약초",
                _ => type.ToString(),
            };
        }

        private static string JobTypeToKorean(GlobalEnum.JobType type)
        {
            return type switch
            {
                GlobalEnum.JobType.None       => "무직",
                GlobalEnum.JobType.Farmer     => "농부",
                GlobalEnum.JobType.Blacksmith => "대장장이",
                GlobalEnum.JobType.Merchant   => "상인",
                GlobalEnum.JobType.Hunter     => "사냥꾼",
                GlobalEnum.JobType.Builder    => "건축가",
                GlobalEnum.JobType.Scholar    => "학자",
                GlobalEnum.JobType.Guard      => "경비병",
                GlobalEnum.JobType.Chief      => "촌장",
                GlobalEnum.JobType.Woodcutter => "벌목꾼",
                GlobalEnum.JobType.Miner      => "광부",
                GlobalEnum.JobType.Gatherer   => "채집꾼",
                _ => type.ToString(),
            };
        }
    }
}
