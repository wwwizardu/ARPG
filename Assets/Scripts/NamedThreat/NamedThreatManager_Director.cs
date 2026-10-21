#nullable enable
using System.Collections.Generic;
using ARPG.Component;
using ARPG.Data;
using ARPG.History;
using ARPG.Tables;
using ARPG.Village;
using UnityEngine;

namespace ARPG.NamedThreat
{
    /// <summary>
    /// 감독 (NAMED_THREAT_DESIGN §4·§7·§8.1~§8.2). 게임은 System_NamedThreatDirector가 1초(실제)마다 플레이어 위치로 부르고
    /// (던전 안에서는 출현을 뺀 판정만, DUNGEON_WORLD_SIM_DESIGN §3.6),
    /// 진단은 같은 AdvanceDirector를 플레이어 위치를 흉내 내며 부른다 (live = false: 알림·저장 요청·엔티티 판단 없음).
    /// 출현 규칙은 마을과 시간으로만 판정한다 — 걸음 수 기준을 쓰지 않는다 (2026-10-18 사용자 결정).
    /// </summary>
    public partial class NamedThreatManager
    {
        /// <summary>게임 진입점. 마을 주민 배치가 끝나야 소문 주인을 뽑을 수 있으므로 그 전에는 아무것도 하지 않는다 (§4.1).</summary>
        public void TickDirector(float now)
        {
            if (_initialized == false || AR.s.Npc == null || AR.s.Npc.NarrativeOwnerSelectionReady == false)
                return;
            if (AR.s.Dungeon.IsInside == true)
            {
                // 던전 안에는 플레이어 월드 위치가 없다. 단계·사라짐·소문 주인·기록 정리는 게임 시간과 기록만 보므로 진행하고,
                // 출현 판정만 건너뛴다 — 퇴장 뒤 첫 판정에서 평소처럼 본다 (DUNGEON_WORLD_SIM_DESIGN §3.6)
                AdvanceRecords(now, true);
            }
            else
            {
                if (TryGetPlayerPosition(out Vector2 player) == false)
                    return;
                AdvanceDirector(now, player, true);
            }
            RetryPendingSaves();
        }

        /// <summary>
        /// 감독 판정 1회 (게임·진단 공용): 단계 전이 → 사라짐 → 소문 주인 재배정 → 끝난 기록 정리 → 출현.
        /// referencePosition = 플레이어 위치. 그와 가장 가까운 마을에 붙은 네임드가 없고 간격이 지났으면 그 마을에 새 네임드가 나온다.
        /// </summary>
        public void AdvanceDirector(float now, Vector2 referencePosition, bool live)
        {
            AdvanceRecords(now, live);
            TryAppear(now, referencePosition, live, false);
        }

        // 출현을 뺀 감독 판정: 단계 전이 → 사라짐 → 소문 주인 재배정 → 끝난 기록 정리. 플레이어 위치를 보지 않는다.
        private void AdvanceRecords(float now, bool live)
        {
            NamedThreatWorldState state = State;
            if (state.NextAppearGameTime < 0f)
            {
                // 새 게임·이 기능 이전 세이브: 지금 시각을 기준으로 둔다 → 바로 첫 네임드 (NT3)
                MarkDirty();
                state.NextAppearGameTime = now;
            }
            AdvanceActiveThreats(now, live);
            PruneEndedRecords(now);
        }

        private void AdvanceActiveThreats(float now, bool live)
        {
            List<NamedThreatRecord> active = State.Active;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                NamedThreatRecord record = active[i];
                // 단계 (§7.1): 시간 또는 부상 수. 내려가지 않는다. 엔티티가 있는 동안의 변화는 다음 생성 때 반영된다.
                int stage = Mathf.Max(record.Stage, NamedThreatRules.ComputeStage(record.AppearedGameTime, record.HarmCount, now));
                if (stage != record.Stage)
                {
                    MarkDirty();
                    record.Stage = Mathf.Min(stage, NamedThreatRules.MaxStage);
                    record.StageStartedGameTime = now;
                    if (live == true)
                        _worldSavePending = true;
                    if (live == true && record.PlayerHeard == true)
                        AR.s.UI.PushWorldFeed($"{Josa(record.Title, "이", "가")} 더 사나워졌다는 소문이 돕니다.", true);
#if UNITY_EDITOR
                    ValidateInvariants("stage");
#endif
                }

                // 사라짐 (§7.2): 싸우는 중이면(엔티티가 있으면) 플레이어가 떠나 엔티티가 지워진 뒤에 사라진다.
                if (NamedThreatRules.IsVanishDue(record.AppearedGameTime, now) == true)
                {
                    if (live == true && (_entityByThreat.ContainsKey(record.ThreatId) == true || _spawningThreats.Contains(record.ThreatId) == true))
                        continue;
                    Vanish(record, now, live);
                    continue;
                }

                EnsureRumorHolders(record, now);
            }
        }

        private void Vanish(NamedThreatRecord record, float now, bool live)
        {
            EndRecord(record, NamedThreatState.Vanished, now, false);
            // 부탁은 미해결로 끝나고 부탁한 NPC가 다음 대화에서 끝 알림을 한다. 보답·행적 소문은 없다.
            AR.s.Npc.EndNamedThreatNarrative(record.ThreatId, false, now);
            if (live == true && record.PlayerHeard == true)
                AR.s.UI.PushWorldFeed($"{Josa(record.Title, "이", "가")} 자취를 감췄다는 소문이 돕니다.", true);
            // 기록 종료와 부탁 미해결 종료를 한 번에 저장한다 — 끝 알림 확정(narrative만)보다 먼저 기록이 끝나 있어야 한다
            if (live == true)
                _recordSavePending = true;
            if (live == true)
                Debug.Log($"[NamedThreat] Vanished id={record.ThreatId} title={record.Title} at {now:F1}h (appeared {record.AppearedGameTime:F1}h)");
        }

        // 소문 주인이 다른 이유(일반 사냥·던전 원정)로 죽었으면 그 마을의 새 주인을 뽑는다 (§8.1). 먼 소문 보유자가 죽으면 그 자리만 비운다.
        private void EnsureRumorHolders(NamedThreatRecord record, float now)
        {
            if (record.RumorOwnerNpcId < 0 || AR.s.Npc.IsNpcAlive(record.RumorOwnerNpcId) == false)
            {
                ulong hash = NamedThreatRules.Hash(AR.s.Data.WorldSeed, record.ThreatId, NamedThreatRules.PurposeRumorOwner,
                    Mathf.FloorToInt(now));
                int owner = AR.s.Npc.PickNamedThreatResident(record.PrimaryVillageId, hash);
                if (owner >= 0)
                {
                    MarkDirty();
                    record.RumorOwnerNpcId = owner;
                    AR.s.Npc.GiveNamedThreatRumor(owner, record, false, now);
                    AR.s.Npc.SetNamedThreatNarrativeOwner(record.ThreatId, owner);
                }
            }
            for (int f = record.FarRumorNpcIds.Count - 1; f >= 0; f--)
            {
                if (AR.s.Npc.IsNpcAlive(record.FarRumorNpcIds[f]) == true)
                    continue;
                MarkDirty();
                record.FarRumorNpcIds.RemoveAt(f);
            }
        }

        // ================= 출현 (§4) =================

        // ignoreInterval: 개발 치트 "네임드 근처 출현" — 간격·재시도 시각을 보지 않는다
        private NamedThreatRecord? TryAppear(float now, Vector2 referencePosition, bool live, bool ignoreInterval)
        {
            NamedThreatWorldState state = State;
            if (ignoreInterval == false && (now < state.NextAppearGameTime || now < state.NextPlacementRetryGameTime))
                return null;
            if (AR.s.Data.NamedThreatTemplates.Count == 0)
                return null;
            VillageData? village = FindNearestVillage(referencePosition);
            if (village == null || GetActiveForVillage(village.VillageId) != null)
                return null;
            // 주민이 없는 마을(사라진 터 포함)이면 나오지 않는다 — 소문 주인을 뽑을 수 없다 (§4.2)
            if (AR.s.Village.IsAbandonedSite(village) == true || AR.s.Npc.CountNamedThreatResidents(village.VillageId) == 0)
                return null;

            int worldSeed = AR.s.Data.WorldSeed;
            int threatId = state.NextSerial;
            if (TryPlaceTerritory(village, worldSeed, threatId, out Vector2 territory) == false)
            {
                MarkDirty();
                state.NextPlacementRetryGameTime = now + NamedThreatRules.PlacementRetryHours;
                return null;
            }
            int zone = AR.s.WorldMap.GetZoneAtWorld(territory);
            Vector2Int chunk = new Vector2Int(Mathf.FloorToInt(territory.x / AR.s.WorldMap.chunkSize),
                Mathf.FloorToInt(territory.y / AR.s.WorldMap.chunkSize));
            NamedThreatTable? template = PickTemplate(worldSeed, threatId, zone, AR.s.WorldMap.GetChunkBiome(chunk));
            if (template == null)
            {
                MarkDirty();
                state.NextPlacementRetryGameTime = now + NamedThreatRules.PlacementRetryHours;
                return null;
            }

            string title = PickTitle(template, worldSeed, threatId);
            MarkDirty();
            state.NextSerial++;
            var record = new NamedThreatRecord
            {
                ThreatId = threatId,
                TemplateId = template.Id,
                Title = title,
                State = NamedThreatState.Active,
                Stage = 1,
                BaseLevel = NamedThreatRules.GetBaseLevel(zone),
                AppearedGameTime = now,
                StageStartedGameTime = now,
                TerritoryX = territory.x,
                TerritoryY = territory.y,
                PrimaryVillageId = village.VillageId,
            };
            state.Active.Add(record);
            if (state.UsedTitles.Contains(title) == false)
                state.UsedTitles.Add(title);
            state.NextAppearGameTime = Mathf.Max(state.NextAppearGameTime, now + NamedThreatRules.AppearIntervalHours);
            state.NextPlacementRetryGameTime = 0f;

            AssignInitialRumorHolders(record, worldSeed, now);
            AR.s.Npc.CreateNamedThreatNarrative(record, template.NarrativeContentId);
            if (live == true)
            {
                // 출현·소문 주인을 World·NpcState(소문)·Narrative 한 번에 저장한다 (§10.4)
                _recordSavePending = true;
                Debug.Log($"[NamedThreat] Appeared id={threatId} template={template.Id} title={title} village={village.VillageId} " +
                          $"territory=({territory.x:F0},{territory.y:F0}) zone={zone} level={record.BaseLevel} owner={record.RumorOwnerNpcId} " +
                          $"far={record.FarRumorNpcIds.Count} at {now:F1}h");
            }
#if UNITY_EDITOR
            ValidateInvariants("appear");
#endif
            return record;
        }

        // 소문 주인 1명(주 마을) + 주 마을에서 가장 가까운 다른 마을 4곳에 1명씩 먼 소문 (§8.1·§8.2)
        private void AssignInitialRumorHolders(NamedThreatRecord record, int worldSeed, float now)
        {
            int owner = AR.s.Npc.PickNamedThreatResident(record.PrimaryVillageId,
                NamedThreatRules.Hash(worldSeed, record.ThreatId, NamedThreatRules.PurposeRumorOwner));
            record.RumorOwnerNpcId = owner;
            if (owner >= 0)
                AR.s.Npc.GiveNamedThreatRumor(owner, record, false, now);

            VillageData? primary = AR.s.Village.GetVillage(record.PrimaryVillageId);
            if (primary == null)
                return;
            List<VillageData> nearest = _villageScratch;
            nearest.Clear();
            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            for (int i = 0; i < villages.Count; i++)
            {
                VillageData village = villages[i];
                if (village.VillageId == record.PrimaryVillageId || AR.s.Village.IsAbandonedSite(village) == true ||
                    AR.s.Npc.CountNamedThreatResidents(village.VillageId) == 0)
                    continue;
                nearest.Add(village);
            }
            Vector2 center = primary.Position;
            nearest.Sort((a, b) =>
            {
                int byDistance = (a.Position - center).sqrMagnitude.CompareTo((b.Position - center).sqrMagnitude);
                return byDistance != 0 ? byDistance : a.VillageId.CompareTo(b.VillageId);
            });
            for (int i = 0; i < nearest.Count && record.FarRumorNpcIds.Count < NamedThreatRules.FarRumorVillageCount; i++)
            {
                int holder = AR.s.Npc.PickNamedThreatResident(nearest[i].VillageId,
                    NamedThreatRules.Hash(worldSeed, record.ThreatId, NamedThreatRules.PurposeFarRumor, nearest[i].VillageId));
                if (holder < 0)
                    continue;
                record.FarRumorNpcIds.Add(holder);
                AR.s.Npc.GiveNamedThreatRumor(holder, record, true, now);
            }
            nearest.Clear();
        }

        // 영역 (§4.3): 후보 방향 8개를 고정 무작위 순서로 시도. 맵 안이고 어떤 마을 경계 + 여백과도, 다른 네임드 영역과도 겹치지 않아야 한다.
        private bool TryPlaceTerritory(VillageData village, int worldSeed, int threatId, out Vector2 territory)
        {
            NamedThreatRules.FillCandidateOrder(worldSeed, threatId, _candidateOrderScratch);
            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            for (int k = 0; k < _candidateOrderScratch.Length; k++)
            {
                Vector2 candidate = NamedThreatRules.GetTerritoryCandidate(village.Position, worldSeed, threatId, _candidateOrderScratch[k]);
                if (IsTerritoryInsideMap(candidate) == false)
                    continue;
                bool blocked = false;
                for (int v = 0; v < villages.Count && blocked == false; v++)
                {
                    VillageData other = villages[v];
                    blocked = NamedThreatRules.OverlapsVillage(candidate, NamedThreatRules.TerritoryRadius, other.BoundsX, other.BoundsY,
                        other.BoundsW, other.BoundsH, other.Position);
                }
                List<NamedThreatRecord> active = State.Active;
                for (int a = 0; a < active.Count && blocked == false; a++)
                    blocked = NamedThreatRules.OverlapsTerritory(candidate, active[a].Territory);
                if (blocked == true)
                    continue;
                territory = candidate;
                return true;
            }
            territory = Vector2.zero;
            return false;
        }

        private static bool IsTerritoryInsideMap(Vector2 center)
        {
            Map.MapManager map = AR.s.WorldMap;
            float radius = NamedThreatRules.TerritoryRadius;
            float minX = map.minChunkX * map.chunkSize;
            float maxX = (map.maxChunkX + 1) * map.chunkSize;
            float minY = map.minChunkY * map.chunkSize;
            float maxY = (map.maxChunkY + 1) * map.chunkSize;
            return center.x - radius >= minX && center.x + radius <= maxX && center.y - radius >= minY && center.y + radius <= maxY;
        }

        // 조건(구역 범위·바이옴)에 맞는 행을 가중 무작위로 고른다 (§4.4)
        private NamedThreatTable? PickTemplate(int worldSeed, int threatId, int zone, GlobalEnum.BiomeType biome)
        {
            List<NamedThreatTable> candidates = _templateScratch;
            candidates.Clear();
            int totalWeight = 0;
            IReadOnlyList<NamedThreatTable> templates = AR.s.Data.NamedThreatTemplates;
            for (int i = 0; i < templates.Count; i++)
            {
                NamedThreatTable template = templates[i];
                if (template.Weight <= 0 || zone < template.MinZone || zone > template.MaxZone ||
                    (template.Biome != GlobalEnum.BiomeType.Any && template.Biome != biome) ||
                    AR.s.Data.GetMonster(template.MonsterId) == null)
                    continue;
                candidates.Add(template);
                totalWeight += template.Weight;
            }
            NamedThreatTable? picked = null;
            if (totalWeight > 0)
            {
                HistoryRng rng = NamedThreatRules.CreateRng(worldSeed, threatId, NamedThreatRules.PurposeTemplate);
                int roll = rng.Range(0, totalWeight);
                for (int i = 0; i < candidates.Count && picked == null; i++)
                {
                    roll -= candidates[i].Weight;
                    if (roll < 0)
                        picked = candidates[i];
                }
            }
            candidates.Clear();
            return picked;
        }

        // 칭호 후보 3개 중 이 세계에서 아직 쓰지 않은 것. 모두 썼으면 다시 쓴다 (§4.4)
        private string PickTitle(NamedThreatTable template, int worldSeed, int threatId)
        {
            int unused = 0;
            int available = 0;
            for (int i = 0; i < 3; i++)
            {
                string title = template.GetTitle(i);
                if (string.IsNullOrEmpty(title) == true)
                    continue;
                available++;
                if (State.UsedTitles.Contains(title) == false)
                    unused++;
            }
            if (available == 0)
                return string.IsNullOrEmpty(template.Name) == false ? template.Name : "이름 모를 짐승";
            HistoryRng rng = NamedThreatRules.CreateRng(worldSeed, threatId, NamedThreatRules.PurposeTitle);
            bool onlyUnused = unused > 0;
            int pick = rng.Range(0, onlyUnused == true ? unused : available);
            for (int i = 0; i < 3; i++)
            {
                string title = template.GetTitle(i);
                if (string.IsNullOrEmpty(title) == true || (onlyUnused == true && State.UsedTitles.Contains(title) == true))
                    continue;
                if (pick == 0)
                    return title;
                pick--;
            }
            return template.GetTitle(0);
        }

        /// <summary>플레이어와 가장 가까운 마을(마을 중심 기준, §4.2). 같은 거리면 VillageId가 작은 쪽.</summary>
        public static VillageData? FindNearestVillage(Vector2 position)
        {
            IReadOnlyList<VillageData> villages = AR.s.Village.GetAllVillages();
            VillageData? best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < villages.Count; i++)
            {
                float sqr = (villages[i].Position - position).sqrMagnitude;
                if (sqr < bestSqr || (sqr == bestSqr && best != null && villages[i].VillageId < best.VillageId))
                {
                    best = villages[i];
                    bestSqr = sqr;
                }
            }
            return best;
        }

        private static bool TryGetPlayerPosition(out Vector2 position)
        {
            if (AR.s.Component.TryGetComponent<TransformComponent>(AR.s.Data.CurrentPlayerEntityId, out TransformComponent transform) == true)
            {
                position = transform.Position;
                return true;
            }
            position = Vector2.zero;
            return false;
        }

        // ================= 개발 치트 (§13, UICheat) =================

        /// <summary>플레이어와 가장 가까운 마을에 간격을 무시하고 네임드를 낸다. 결과 문구를 돌려준다.</summary>
        public string CheatAppearNearPlayer()
        {
            if (_initialized == false || TryGetPlayerPosition(out Vector2 player) == false)
                return "플레이어 위치를 알 수 없습니다.";
            VillageData? village = FindNearestVillage(player);
            if (village == null)
                return "가까운 마을이 없습니다.";
            NamedThreatRecord? existing = GetActiveForVillage(village.VillageId);
            if (existing != null)
                return $"마을 {village.VillageId}에는 이미 {existing.Title}(단계 {existing.Stage})가 있습니다.";
            NamedThreatRecord? record = TryAppear(AR.s.Time.CurrentGameTime, player, true, true);
            return record != null
                ? $"{record.Title} 출현 — 마을 {record.PrimaryVillageId}, 영역 ({record.TerritoryX:F0},{record.TerritoryY:F0}), 레벨 {record.BaseLevel}"
                : "출현 실패 — 주민·영역 자리·템플릿을 확인하세요 (콘솔 로그).";
        }

        /// <summary>가까운 마을의 네임드 단계를 1 올린다.</summary>
        public string CheatAdvanceStage()
        {
            NamedThreatRecord? record = FindNearestActive();
            if (record == null)
                return "활성 네임드가 없습니다.";
            if (record.Stage >= NamedThreatRules.MaxStage)
                return $"{record.Title}는 이미 {record.Stage}단계입니다.";
            MarkDirty();
            record.Stage++;
            record.StageStartedGameTime = AR.s.Time.CurrentGameTime;
            _worldSavePending = true;
            return $"{record.Title} → {record.Stage}단계 (다음에 새로 만들 때 반영)";
        }

        /// <summary>가까운 마을의 네임드를 바로 사라지게 한다 (엔티티가 있으면 먼저 지운다).</summary>
        public string CheatVanishNow()
        {
            NamedThreatRecord? record = FindNearestActive();
            if (record == null)
                return "활성 네임드가 없습니다.";
            DespawnEntity(record.ThreatId);
            Vanish(record, AR.s.Time.CurrentGameTime, true);
            return $"{record.Title} 사라짐 처리";
        }

        private NamedThreatRecord? FindNearestActive()
        {
            if (TryGetPlayerPosition(out Vector2 player) == false)
                return null;
            NamedThreatRecord? best = null;
            float bestSqr = float.MaxValue;
            List<NamedThreatRecord> active = State.Active;
            for (int i = 0; i < active.Count; i++)
            {
                float sqr = (active[i].Territory - player).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    best = active[i];
                    bestSqr = sqr;
                }
            }
            return best;
        }
    }
}
