#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Component;
using ARPG.Data;
using ARPG.Factory;
using ARPG.Npc.Narrative;
using ARPG.Scene;
using ARPG.Tables;
using ARPG.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.NamedThreat
{
    /// <summary>
    /// 존재·흔적 (NAMED_THREAT_DESIGN §5.3·§5.4·§8.5). 위치와 체력은 저장하지 않는다 — 배회 지점은 게임 시각으로 계산하고,
    /// 그 청크가 활성일 때만 엔티티로 만든다. 플레이어가 떠나 엔티티가 서 있는 청크가 비활성이 되면 지우고 체력은 버린다.
    /// 필드 몬스터 슬롯에는 등록하지 않는다 — 슬롯 출신 기록이 없는 몬스터는 활성 게이트(IsMonsterSourceChunkActive)를 통과한다.
    /// </summary>
    public partial class NamedThreatManager
    {
        // 순찰 기준점이 이만큼 이상 바뀌었을 때만 AI 기준점을 옮긴다
        private const float AnchorMoveEpsilonSqr = 0.01f;

        /// <summary>실체화된 네임드 엔티티가 있는가 (사라짐을 미루는 조건, §7.2).</summary>
        public bool IsEntityPresent(int threatId) => _entityByThreat.ContainsKey(threatId) || _spawningThreats.Contains(threatId);

        /// <summary>System_NamedThreatPresence (0.5초). 청크 활성에 따라 엔티티를 만들거나 지우고, 순찰 기준점을 배회 지점으로 옮긴다.</summary>
        public void TickPresence(float now)
        {
            if (_initialized == false || AR.s.Dungeon.IsInside == true)
                return;
            ComponentManager cm = AR.s.Component;
            int worldSeed = AR.s.Data.WorldSeed;
            int step = NamedThreatRules.GetRoamStep(now);

            // 끝난 네임드의 엔티티가 남아 있으면 지운다 (치트·로드 보정)
            _threatIdScratch.Clear();
            foreach (KeyValuePair<int, int> pair in _entityByThreat)
                if (IsActive(pair.Key) == false)
                    _threatIdScratch.Add(pair.Key);
            for (int i = 0; i < _threatIdScratch.Count; i++)
                DespawnEntity(_threatIdScratch[i]);
            _threatIdScratch.Clear();

            List<NamedThreatRecord> active = State.Active;
            for (int i = 0; i < active.Count; i++)
            {
                NamedThreatRecord record = active[i];
                Vector2 roam = NamedThreatRules.GetRoamPoint(worldSeed, record.ThreatId, record.Territory, step);
                if (_entityByThreat.TryGetValue(record.ThreatId, out int entityId) == true)
                {
                    if (IsTrackedEntityAlive(cm, entityId, record.ThreatId) == false)
                    {
                        _entityByThreat.Remove(record.ThreatId);
                        continue;
                    }
                    cm.TryGetComponent<TransformComponent>(entityId, out TransformComponent transform);
                    if (AR.s.WorldMap.IsChunkActiveAtWorld(transform.Position) == false)
                    {
                        DespawnEntity(record.ThreatId);
                        continue;
                    }
                    // 배회 지점이 바뀌어도 순간이동하지 않는다. 순찰 기준점만 옮긴다 (§5.4).
                    if (cm.TryGetComponent<AIStateComponent>(entityId, out AIStateComponent aiState) == true &&
                        (aiState.SpawnPosition - roam).sqrMagnitude > AnchorMoveEpsilonSqr)
                    {
                        aiState.SpawnPosition = roam;
                        cm.SetComponent(entityId, aiState);
                    }
                    continue;
                }

                if (_spawningThreats.Contains(record.ThreatId) == true ||
                    NamedThreatRules.IsVanishDue(record.AppearedGameTime, now) == true ||
                    AR.s.WorldMap.IsChunkActiveAtWorld(roam) == false)
                    continue;
                if (_failedSpawnStep.TryGetValue(record.ThreatId, out int failedStep) == true && failedStep == step)
                    continue;
                if (TryFindSpawnPosition(roam, out Vector2 spawn) == false)
                {
                    // 배회 지점 8칸 안에 걸을 수 있는 칸이 없으면 이번 배회 지점에서는 만들지 않는다
                    _failedSpawnStep[record.ThreatId] = step;
                    continue;
                }
                SpawnAsync(record.ThreatId, spawn, roam).Forget();
            }
        }

        private static bool IsTrackedEntityAlive(ComponentManager cm, int entityId, int threatId)
            => AR.s.Entity.IsEntityRegistered(entityId) == true && cm.HasComponent<DestroyTag>(entityId) == false &&
               cm.TryGetComponent<NamedThreatComponent>(entityId, out NamedThreatComponent named) == true && named.ThreatId == threatId &&
               cm.HasComponent<TransformComponent>(entityId) == true;

        // 배회 지점에서 가장 가까운 걸을 수 있는 칸 (8칸 안, 활성 청크, 필드 몬스터 스폰과 같은 칸 규칙 — 마을 금지 여백·언덕·오브젝트 제외)
        private static bool TryFindSpawnPosition(Vector2 roam, out Vector2 position)
        {
            Map.MapManager map = AR.s.WorldMap;
            int originX = Mathf.FloorToInt(roam.x);
            int originY = Mathf.FloorToInt(roam.y);
            for (int radius = 0; radius <= NamedThreatRules.SpawnSearchRadius; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius)
                            continue;
                        int tx = originX + dx;
                        int ty = originY + dy;
                        Vector2Int chunk = new Vector2Int(Mathf.FloorToInt((float)tx / map.chunkSize), Mathf.FloorToInt((float)ty / map.chunkSize));
                        if (map.TryGetActiveChunkSpawnRuntime(chunk, out Map.ChunkSpawnRuntimeData runtime) == false)
                            continue;
                        Vector2 candidate = new Vector2(tx + 0.5f, ty + 0.5f);
                        if (map.IsMonsterSpawnWorldPositionValid(chunk, runtime.GenerationId, candidate) == false)
                            continue;
                        position = candidate;
                        return true;
                    }
                }
            }
            position = roam;
            return false;
        }

        private async UniTaskVoid SpawnAsync(int threatId, Vector2 position, Vector2 anchor)
        {
            int lifetime = _lifetime;
            _spawningThreats.Add(threatId);
            int entityId = -1;
            EntityBase? entity = null;
            try
            {
                NamedThreatRecord? record = GetActive(threatId);
                NamedThreatTable? template = record != null ? GetTemplate(record) : null;
                if (record == null || template == null || AR.s.CurrentScene is GameScene gameScene == false)
                    return;
                MonsterAffixComponent affixes = BuildFixedAffixes(template, record.Stage);
                int level = NamedThreatRules.GetLevel(record.BaseLevel, record.Stage);
                (entityId, entity) = await EntityFactory.CreateMonster(template.MonsterId, new Vector3(position.x, position.y, -0.05f),
                    gameScene.MonsterRoot, level, GlobalEnum.MonsterArchetype.Named, affixes, template.AiTableId);
                if (entityId < 0 || entity == null)
                    return;
                if (lifetime != _lifetime || IsActive(threatId) == false || AR.s.Dungeon.IsInside == true ||
                    AR.s.WorldMap.IsChunkActiveAtWorld(position) == false)
                {
                    AR.s.Component.AddComponent(entityId, new DestroyTag());
                    return;
                }

                ComponentManager cm = AR.s.Component;
                cm.AddComponent(entityId, new NamedThreatComponent { ThreatId = threatId });
                AR.s.Monster.AddMonster(entity);
                EntityFactory.AddActivationComponent(entityId, ActivationDistance, DeactivationDistance);
                if (cm.TryGetComponent<AIStateComponent>(entityId, out AIStateComponent aiState) == true)
                {
                    aiState.SpawnPosition = anchor;
                    cm.SetComponent(entityId, aiState);
                }
                AttachTitleView(entity);
                _entityByThreat[threatId] = entityId;
                Debug.Log($"[NamedThreat] Materialized id={threatId} entity={entityId} level={level} stage={record.Stage} " +
                          $"at ({position.x:F1},{position.y:F1})");
            }
            catch (Exception ex)
            {
                Debug.LogError("[NamedThreat] Spawn failed: " + ex);
                if (entityId >= 0 && AR.s.Entity.IsEntityRegistered(entityId) == true)
                    AR.s.Component.AddComponent(entityId, new DestroyTag());
            }
            finally
            {
                if (lifetime == _lifetime)
                    _spawningThreats.Remove(threatId);
            }
        }

        // 고정 접사 (§5.2): Affix1·Affix2 + 2단계 Stage2Affix + 3단계 Stage3Affix. 무작위로 굴리지 않는다.
        private static MonsterAffixComponent BuildFixedAffixes(NamedThreatTable template, int stage)
        {
            MonsterAffixComponent affixes = default;
            AddAffix(ref affixes, template.Affix1);
            AddAffix(ref affixes, template.Affix2);
            if (stage >= 2)
                AddAffix(ref affixes, template.Stage2Affix);
            if (stage >= 3)
                AddAffix(ref affixes, template.Stage3Affix);
            return affixes;
        }

        private static void AddAffix(ref MonsterAffixComponent affixes, int affixId)
        {
            if (affixId <= 0)
                return;
            for (int i = 0; i < affixes.Count; i++)
                if (affixes.GetByIndex(i) == affixId)
                    return;
            affixes.Add(affixId);
        }

        // 칭호 이름표를 HP바 캔버스(접사 이름표와 같은 UICanvas 루트)에 붙인다. 다른 엔티티의 캔버스에는 붙지 않는다.
        private static void AttachTitleView(EntityBase entity)
        {
            MonsterAffixLabelView? canvasRoot = entity.GetComponentInChildren<MonsterAffixLabelView>(true);
            if (canvasRoot == null)
                return;
            NamedThreatTitleView view = canvasRoot.GetComponent<NamedThreatTitleView>();
            if (view == null)
                view = canvasRoot.gameObject.AddComponent<NamedThreatTitleView>();
            view.RegisterTo(entity);
        }

        private void DespawnEntity(int threatId)
        {
            if (_entityByThreat.TryGetValue(threatId, out int entityId) == false)
                return;
            _entityByThreat.Remove(threatId);
            ComponentManager cm = AR.s.Component;
            if (AR.s.Entity.IsEntityRegistered(entityId) == true && cm.TryGetComponent<NamedThreatComponent>(entityId, out NamedThreatComponent named) == true &&
                named.ThreatId == threatId && cm.HasComponent<DestroyTag>(entityId) == false)
                cm.AddComponent(entityId, new DestroyTag());
        }

        // ================= 흔적 자막 (§8.5) =================

        /// <summary>
        /// System_NamedThreatTrail (0.5초). 플레이어가 영역(반경 24걸음) 안에 있고 네임드가 없거나 12걸음 밖이면
        /// 가운데 한 줄로 "{흔적 문구} 흔적은 {방향}으로 이어진다."를 보여 준다. 들어갈 때 1번, 안에 있는 동안 15초(실제)마다.
        /// 소문을 듣지 않았어도 뜬다. 조사 상호작용·흔적 오브젝트는 없다.
        /// </summary>
        public void TickTrail(float now)
        {
            if (_initialized == false || AR.s.Dungeon.IsInside == true || TryGetPlayerPosition(out Vector2 player) == false)
                return;
            NamedThreatRecord? inside = null;
            List<NamedThreatRecord> active = State.Active;
            for (int i = 0; i < active.Count && inside == null; i++)
                if (NamedThreatRules.IsInsideTerritory(active[i].Territory, player) == true)
                    inside = active[i];
            if (inside == null)
            {
                _trailThreatId = 0;
                return;
            }

            Vector2 target = NamedThreatRules.GetRoamPoint(AR.s.Data.WorldSeed, inside.ThreatId, inside.Territory, NamedThreatRules.GetRoamStep(now));
            if (_entityByThreat.TryGetValue(inside.ThreatId, out int entityId) == true &&
                AR.s.Component.TryGetComponent<TransformComponent>(entityId, out TransformComponent transform) == true)
            {
                if ((transform.Position - player).sqrMagnitude <= NamedThreatRules.TrailNearDistance * NamedThreatRules.TrailNearDistance)
                    return;   // 가까이 있으면 흔적 대신 그놈이 보인다
                target = transform.Position;
            }

            bool entered = _trailThreatId != inside.ThreatId;
            if (entered == false && Time.unscaledTime < _nextTrailTime)
                return;
            _trailThreatId = inside.ThreatId;
            _nextTrailTime = Time.unscaledTime + NamedThreatRules.TrailRefreshSeconds;
            int bearing = NamedThreatRules.GetBearing(player, target);
            string direction = bearing >= 0 ? NpcRumorPhrasing.BearingName(bearing) + "으로" : "이 근처에서";
            AR.s.UI.SetNotify($"{GetSignText(inside)} 흔적은 {direction} 이어진다.");
        }
    }
}
