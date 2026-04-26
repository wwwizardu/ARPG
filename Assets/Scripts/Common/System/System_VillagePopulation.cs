#nullable enable
using ARPG.Component;
using ARPG.Scene;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// Phase A→C: 마을 인구 관리 통합 시스템 (구 System_VillageRespawn).
    ///
    /// 두 가지 책임:
    ///  1. **정원 재스폰** (Phase A) — NPC 전멸 후 쿨다운 만료 시 기본 NPC 재생성. 매 5s 체크.
    ///  2. **자연 이민** (Phase C) — Hamlet+ 마을에 게임시간 24h마다 1명 확률 스폰.
    /// </summary>
    public class System_VillagePopulation : IFixedUpdateSystem
    {
        // 도메인 대역 (CLAUDE.md): 55-59 Population
        public int Priority => 56;
        public float UpdateInterval => 5.0f;

        private const float IMMIGRATION_CHECK_HOURS = 8f;
        private float _lastImmigrationCheckGameTime = -1f;

        public void OnCreate()
        {
            _lastImmigrationCheckGameTime = AR.s.Time.CurrentGameTime;
        }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (AR.s.CurrentScene is GameScene == false)
                return;

            // (기존) 정원 재스폰 — 매 5s
            AR.s.Npc.EnsureAllVillagesPopulated();

            // (신규) 자연 이민 — 게임시간 24h마다
            float now = AR.s.Time.CurrentGameTime;
            if (now - _lastImmigrationCheckGameTime >= IMMIGRATION_CHECK_HOURS)
            {
                _lastImmigrationCheckGameTime = now;
                TickImmigration();
            }
        }

        public void OnReset()
        {
            _lastImmigrationCheckGameTime = -1f;
        }

        // ========== 자연 이민 (Phase C) ==========

        private const int BEDROLL_TABLE_ID = 101;
        private const int BED_TABLE_ID = 102;
        private const int FOOD_PER_NPC = 5;
        private const float IMMIGRATION_BASE_CHANCE = 0.10f;        // Settlement 기준
        private const float IMMIGRATION_CHANCE_PER_STAGE = 0.05f;   // Stage당 +5%

        private static void TickImmigration()
        {
            foreach (VillageData v in AR.s.Village.GetAllVillages())
            {
                if (v.EntityId < 0) continue;

                // 빈 잠자리 수 (Bedroll + Bed 모두 카운트 — Settlement에서도 이민 가능하도록)
                int bedCount = CountSleepSpots(v);
                if (bedCount <= v.Population) continue;

                // 식량 여유 (인구 × 5 이상)
                if (AR.s.Component.TryGetComponent<VillageStorageComponent>(v.EntityId, out var s) == false) continue;
                if (s.FoodAmount < v.Population * FOOD_PER_NPC) continue;

                // 확률: Settlement 10%, Hamlet 15%, Village 20%, Town 25%, City 30%
                float chance = IMMIGRATION_BASE_CHANCE + (int)v.Stage * IMMIGRATION_CHANCE_PER_STAGE;
                if (Random.value > chance) continue;

                SpawnImmigrantNpc(v);
            }
        }

        private static void SpawnImmigrantNpc(VillageData village)
        {
            Tables.VillageTable? table = AR.s.Data.GetVillageTable(village.TableId);
            if (table == null || table.DefaultNpcIds.Count == 0)
                return;

            // 마을의 DefaultNpcIds 중 무작위 선택 (이민자 직업 다양성 확보)
            int idx = Random.Range(0, table.DefaultNpcIds.Count);
            int npcTableId = table.DefaultNpcIds[idx];

            // 마을 중심 주변 랜덤 위치
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(0f, table.SpawnRadius);
            Vector2 spawnPos = village.Position + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);

            int oldPop = village.Population;
            AR.s.Npc.SpawnNewNpc(npcTableId, spawnPos, village.VillageId);

            Debug.Log($"[Immigration] v{village.VillageId} 이민자 도착 (Pop {oldPop}→{oldPop + 1}, NpcTableId={npcTableId})");
        }

        /// <summary>
        /// 이민 가능 여부용 잠자리 카운트 — Bedroll(임시)도 포함.
        /// Tier 승격 조건의 정식 Bed 카운트와는 별도 (그건 Bed만).
        /// </summary>
        private static int CountSleepSpots(VillageData v)
        {
            if (v.PlacedObjectTypeIds == null) return 0;
            int count = 0;
            for (int i = 0; i < v.PlacedObjectTypeIds.Count; i++)
            {
                int id = v.PlacedObjectTypeIds[i];
                if (id == BED_TABLE_ID || id == BEDROLL_TABLE_ID) count++;
            }
            return count;
        }
    }
}
