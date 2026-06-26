#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Npc;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// NPC 관계의 런타임 변화 시스템. 구 System_Relationship 대체 (설계 NPC_RELATION_HISTORY_DESIGN §5.2).
    /// 데이터 정본은 NpcManager(_npcAffinity / _npcRelations) — 이 시스템은 변화 로직만 담당.
    ///
    /// 현재 구현: 호감도 자연 변동(drift) — 같은 마을 + 둘 다 활성(스폰된) NPC 페어가
    ///   근접 또는 같은 직업이면 +drift, Rival/Grudge 관계면 −drift, NpcStat(Friendliness/Patience)이 가중.
    ///   비활성(청크 unload) NPC는 컴포넌트 풀에 없으므로 자동 제외 = 관계 동결.
    /// 추후: 사건 기반 관계 변화(배신/살해 → 원한 형성, 친구↔라이벌 전환 등)도 이 시스템에 추가
    ///   (OnCreate에서 broadcast 구독). 그래서 이름을 drift로 한정하지 않고 Relationship으로 둔다.
    /// </summary>
    public class System_NpcRelationship : IFixedUpdateSystem
    {
        public int Priority => 58;            // 구 System_Relationship 자리 (Population 밴드 55-59)
        public float UpdateInterval => 3.0f;

        private const float PROXIMITY_SQR = 6f * 6f;   // 근접 본딩 반경(타일)²
        private const int SOCIAL_BOND_MIN = 30;        // 친화/인내 평균 이 미만이면 본딩 안 함

        private struct Entry
        {
            public int EntityId;
            public Vector2 Pos;
            public GlobalEnum.JobType Job;
            public int Friendliness;
            public int Patience;
        }

        private readonly Dictionary<int, List<Entry>> _byVillage = new();

        public void OnCreate() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<NpcStatComponent> stats = cm.GetComponentPool<NpcStatComponent>();
            if (stats.Count < 2)
                return;

            // 1) 활성 NPC를 마을별로 묶는다 (마을 없는 NPC 제외)
            _byVillage.Clear();
            for (int i = 0; i < stats.Count; i++)
            {
                int eid = stats.GetEntityId(i);
                if (cm.TryGetComponent<NpcVillageComponent>(eid, out var vc) == false || vc.VillageId < 0)
                    continue;
                if (cm.TryGetComponent<TransformComponent>(eid, out var tr) == false)
                    continue;

                NpcStatComponent st = stats.GetByIndex(i);
                GlobalEnum.JobType job = cm.TryGetComponent<NpcJobComponent>(eid, out var jc)
                    ? jc.JobType : GlobalEnum.JobType.None;

                if (_byVillage.TryGetValue(vc.VillageId, out List<Entry> list) == false)
                    _byVillage[vc.VillageId] = list = new List<Entry>();
                list.Add(new Entry
                {
                    EntityId = eid,
                    Pos = tr.Position,
                    Job = job,
                    Friendliness = st.Friendliness,
                    Patience = st.Patience,
                });
            }

            // 2) 마을 내 페어 drift (활성 마을 1~2개 × 수십 명 = 무해)
            foreach (var kvp in _byVillage)
            {
                List<Entry> list = kvp.Value;
                for (int a = 0; a < list.Count; a++)
                {
                    for (int b = a + 1; b < list.Count; b++)
                    {
                        Entry ea = list[a];
                        Entry eb = list[b];

                        bool sameJob = ea.Job != GlobalEnum.JobType.None && ea.Job == eb.Job;
                        bool close = (ea.Pos - eb.Pos).sqrMagnitude <= PROXIMITY_SQR;
                        if (sameJob == false && close == false)
                            continue;

                        int delta = DriftDelta(ea, eb);
                        if (delta != 0)
                            AR.s.Npc.ModifyAffinity(ea.EntityId, eb.EntityId, delta);
                    }
                }
            }
        }

        private int DriftDelta(in Entry a, in Entry b)
        {
            // Rival/Grudge 관계면 음수 drift
            if (AR.s.Npc.GetRelation(a.EntityId, b.EntityId, NpcRelationType.Rival) != null
                || AR.s.Npc.GetRelation(a.EntityId, b.EntityId, NpcRelationType.GrudgeAgainst) != null)
                return -1;

            // 친화/인내 평균이 낮으면 본딩 안 함
            int social = (a.Friendliness + b.Friendliness + a.Patience + b.Patience) / 4;
            return social >= SOCIAL_BOND_MIN ? 1 : 0;
        }

        public void OnReset()
        {
            _byVillage.Clear();
        }
    }
}
