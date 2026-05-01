#nullable enable
using ARPG.Component;
using ARPG.Scene;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 마을 인구 관리 통합 시스템.
    ///
    /// 두 가지 책임:
    ///  1. **정원 재스폰** — NPC 전멸 후 쿨다운 만료 시 기본 NPC 재생성. 매 5s 체크.
    ///  2. **방문자 이민 (Inn 시스템, INN_HIRING_DESIGN.md)**
    ///     - 매 이민 틱: ① 만료 정리 → ② 빈자리 확인 → ③ 도착 확률 체크 → SpawnVisitorNpc
    ///     - 주기/확률은 마을 Stage에 따라 차등 적용 (§2.4)
    /// </summary>
    public class System_VillagePopulation : IFixedUpdateSystem
    {
        // 도메인 대역 (CLAUDE.md): 55-59 Population
        public int Priority => 56;
        public float UpdateInterval => 5.0f;

        // INN_HIRING_DESIGN.md §2.4 — Stage별 체크 주기 (게임시간 시)
        // 인덱스: 0=Settlement, 1=Hamlet, 2=Village, 3=Town, 4=City
        private static readonly float[] CHECK_HOURS_BY_STAGE = { 6f, 5f, 4f, 3f, 2f };
        // INN_HIRING_DESIGN.md §2.4 — Stage별 도착 확률
        private static readonly float[] ARRIVE_CHANCE_BY_STAGE = { 0.40f, 0.50f, 0.60f, 0.75f, 0.90f };

        // 마을별 마지막 이민 체크 시각 — Stage가 다른 마을이 섞여있어도 각자 주기로 동작
        private readonly System.Collections.Generic.Dictionary<int, float> _lastImmigrationCheckByVillage = new();

        public void OnCreate() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            if (AR.s.CurrentScene is GameScene == false)
                return;

            // (기존) 정원 재스폰 — 매 5s
            AR.s.Npc.EnsureAllVillagesPopulated();

            // (신규) Inn 기반 이민 — Stage별 주기로 마을마다 독립 체크
            TickImmigration();
        }

        public void OnReset()
        {
            _lastImmigrationCheckByVillage.Clear();
        }

        // ========== Inn 기반 이민 ==========

        private void TickImmigration()
        {
            float now = AR.s.Time.CurrentGameTime;

            foreach (VillageData v in AR.s.Village.GetAllVillages())
            {
                if (v.EntityId < 0) continue;

                int stageIdx = Mathf.Clamp((int)v.Stage, 0, CHECK_HOURS_BY_STAGE.Length - 1);
                float checkInterval = CHECK_HOURS_BY_STAGE[stageIdx];

                if (_lastImmigrationCheckByVillage.TryGetValue(v.VillageId, out float last) == false)
                {
                    // 첫 진입 — 이번 틱에 즉시 체크하지 않고 다음 주기부터 동작
                    _lastImmigrationCheckByVillage[v.VillageId] = now;
                    continue;
                }
                if (now - last < checkInterval) continue;
                _lastImmigrationCheckByVillage[v.VillageId] = now;

                // ① 만료 정리 (§2.10) — 24h 경과한 Visitor 디스폰
                AR.s.Npc.EvictExpiredVisitors(v.VillageId);

                // ② 전제조건: Inn 세트 완성 (§2.3)
                if (AR.s.Village.HasObjectSet(v.VillageId, ObjectSetType.Inn) == false)
                    continue;

                // ③ 빈자리 확인
                int capacity = AR.s.Npc.GetInnCapacity(v.VillageId);
                int visitorCount = AR.s.Npc.GetInnVisitorCount(v.VillageId);
                if (visitorCount >= capacity) continue;

                // ④ 식량 게이트 — Resident 인구 × 5 (§2.6, 방문자는 Inn 자체 보급 의제)
                if (AR.s.Component.TryGetComponent<VillageStorageComponent>(v.EntityId, out var s) == false) continue;
                const int FOOD_PER_NPC = 5;
                if (s.FoodAmount < v.Population * FOOD_PER_NPC) continue;

                // ⑤ 도착 확률
                float chance = ARRIVE_CHANCE_BY_STAGE[stageIdx];
                if (Random.value > chance) continue;

                SpawnVisitor(v);
            }
        }

        private static void SpawnVisitor(VillageData village)
        {
            Tables.VillageTable? table = AR.s.Data.GetVillageTable(village.TableId);
            if (table == null || table.DefaultNpcIds.Count == 0)
                return;

            // DefaultNpcIds 중 무작위 선택 (방문자 직업 다양성)
            int idx = Random.Range(0, table.DefaultNpcIds.Count);
            int npcTableId = table.DefaultNpcIds[idx];

            // 마을 중심 주변 랜덤 위치 (Inn 위치를 모르므로 마을 중심 기준)
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(0f, table.SpawnRadius);
            Vector2 spawnPos = village.Position + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);

            int entityId = AR.s.Npc.SpawnVisitorNpc(npcTableId, spawnPos, village.VillageId);

            Debug.Log($"[Immigration] v{village.VillageId} 방문자 도착 (entity={entityId}, NpcTableId={npcTableId}, Stage={village.Stage})");
        }
    }
}
