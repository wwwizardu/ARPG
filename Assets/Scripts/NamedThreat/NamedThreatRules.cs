#nullable enable
using ARPG.History;
using ARPG.Npc;
using UnityEngine;

namespace ARPG.NamedThreat
{
    /// <summary>
    /// 네임드 위협의 순수 규칙 (NAMED_THREAT_DESIGN §4~§8, §12.1). 상태를 갖지 않는다 — 입력(기록·시각·좌표·시드)만 보고 계산한다.
    /// 게임(NamedThreatManager·System_NpcSim)과 진단(NpcAmbitionSimDiagnostics)이 같은 메서드를 불러 결과가 어긋나지 않게 한다.
    /// 수치는 모두 시작값이다(§3). 확정된 규칙: 마을당 1마리(NT2), 시작 즉시 첫 출현(NT3), 영향 마을 = 주 마을(NT5),
    /// 사망 없음(NT10), 이동 없음·180게임시간 뒤 사라짐(NT12), NPC 처치 불가(NT19).
    /// </summary>
    public static class NamedThreatRules
    {
        // ===== 출현·사라짐 (§4.1, §7.2) — 게임 1시간 = 실제 60초 =====
        // 마지막 출현·처치 중 늦은 쪽에서 다음 출현까지 (제안, 실제 약 48분). 사라짐은 간격을 다시 세지 않는다.
        public const float AppearIntervalHours = 48f;
        // 잡지 않으면 출현 뒤 사라지는 시각 (확정, 실제 약 3시간)
        public const float VanishAfterHours = 180f;
        // 영역 자리·템플릿을 못 찾았을 때 다시 시도하는 간격 (§4.3)
        public const float PlacementRetryHours = 6f;
        // 끝난 기록을 남겨 두는 기간 = 처치 반응 기한과 같은 14게임일 (§10.1)
        public const float EndedRetentionHours = 14f * 24f;

        // ===== 영역 (§4.3) — 출현 규칙이 아니라 모양을 정하는 구현값 =====
        public const float TerritoryDistanceMin = 48f;
        public const float TerritoryDistanceMax = 64f;
        public const float TerritoryRadius = 24f;
        // 마을 경계 + 스폰 금지 여백 (MapManager._villageSpawnExclusionMargin과 같은 5칸)
        public const float VillageExclusionMargin = 5f;
        // 원점 반대 방향 ±60° 안에서 후보 방향 8개
        public const float AwayFromOriginHalfAngle = 60f;
        public const int PlacementCandidateCount = 8;

        // ===== 개체 (§5.2~§5.4) =====
        public const int BaseLevelZoneBonus = 2;
        public const int MaxStage = 3;
        // 6게임시간(실제 6분)마다 영역 안 다른 배회 지점
        public const float RoamStepHours = 6f;
        // 배회 지점은 영역 반경 안쪽 70%에서 고른다 (영역 가장자리에 서서 흔적 자막이 끊기지 않게)
        public const float RoamPointRadiusFraction = 0.7f;
        // 배회 지점에서 이 칸 수 안의 가장 가까운 걸을 수 있는 칸에 만든다
        public const int SpawnSearchRadius = 8;

        // ===== 단계 (§7.1) =====
        public const float Stage2AfterHours = 3f * 24f;
        public const int Stage2AfterHarms = 4;
        public const float Stage3AfterHours = 6f * 24f;
        public const int Stage3AfterHarms = 8;

        // ===== NPC 마주침 (§6) — 착수 전 확인 뒤 시작값 유지 (2026-10-18 사용자 결정) =====
        private static readonly float[] EncounterChanceByStage = { 0.15f, 0.22f, 0.30f };
        public const float WoundChanceAtZeroCapability = 0.60f;
        public const float WoundChanceAtFullCapability = 0.35f;
        public const float SevereWoundChance = 0.30f;

        // ===== 소문·부탁·보상 (§8, §9) =====
        public const int FarRumorVillageCount = 4;
        // 교환 때 먼저 건네지도록 높게 둔다 (§8.1). 상한 8에서도 유망 소문과 같은 순위로 남긴다.
        public const int RumorValue = 100;
        // 의뢰 보답 = 기존 10~20골드의 3배 (Q5 제안)
        public const int RequestRewardMultiplier = 3;

        // ===== 흔적 자막 (§8.5) =====
        public const float TrailNearDistance = 12f;
        public const float TrailRefreshSeconds = 15f;

        // ===== RNG salt (§12.3, NPC 활동 채널 210~239 예약. 200~209는 단기 목표 예약) =====
        public const int SoloEncounterSalt = 210;
        public const int SoloResultSalt = 211;
        public const int PartyEncounterSalt = 212;
        public const int SoloSevereSalt = 213;
        public const int PartyResultSaltBase = 220;   // + 파티 내 인덱스
        public const int PartySevereSaltBase = 230;   // + 파티 내 인덱스

        // ===== 감독 해시 채널 (DerivedSeed.ChannelNamedThreat + 네임드 일련번호 + 용도) =====
        public const int PurposeTemplate = 1;
        public const int PurposeTitle = 2;
        public const int PurposeTerritoryOrder = 3;
        public const int PurposeTerritoryDistance = 4;
        public const int PurposeRumorOwner = 5;
        public const int PurposeFarRumor = 6;
        public const int PurposeRoam = 7;

        // ---------------- 시간·단계 ----------------

        public static float GetVanishGameTime(float appearedGameTime) => appearedGameTime + VanishAfterHours;

        public static bool IsVanishDue(float appearedGameTime, float now) => now >= GetVanishGameTime(appearedGameTime);

        /// <summary>시간·부상 수로 정해지는 단계 (먼저 오는 쪽, §7.1). 단계는 내려가지 않으므로 호출자가 현재 값과 큰 쪽을 쓴다.</summary>
        public static int ComputeStage(float appearedGameTime, int harmCount, float now)
        {
            float age = now - appearedGameTime;
            if (age >= Stage3AfterHours || harmCount >= Stage3AfterHarms)
                return 3;
            if (age >= Stage2AfterHours || harmCount >= Stage2AfterHarms)
                return 2;
            return 1;
        }

        /// <summary>실제 몬스터 레벨 = 기준 레벨(출현 때 영역 구역 + 2) + (단계 − 1) (§5.2).</summary>
        public static int GetLevel(int baseLevel, int stage) => Mathf.Max(1, baseLevel + Mathf.Clamp(stage, 1, MaxStage) - 1);

        public static int GetBaseLevel(int territoryZone) => Mathf.Max(1, territoryZone) + BaseLevelZoneBonus;

        // ---------------- NPC 마주침 (§6.1·§6.2) ----------------

        public static float GetEncounterChance(int stage) => EncounterChanceByStage[Mathf.Clamp(stage, 1, MaxStage) - 1];

        /// <summary>마주쳤을 때 다칠 확률. 사냥 능력 0 → 60%, 1 → 35% 선형. 나머지는 도망(빈손)이다. 사망은 없다 (NT10).</summary>
        public static float GetWoundChance(float capability)
            => Mathf.Lerp(WoundChanceAtZeroCapability, WoundChanceAtFullCapability, Mathf.Clamp01(capability));

        /// <summary>마주침 판정 1회. 솔로 = NPC EntityId·salt 210, 파티 = PartyId·salt 212 (파티당 한 번).</summary>
        public static bool RollEncounter(int stage, int rngOwnerId, float seedGameTime, int worldSeed, int salt)
        {
            HistoryRng rng = NpcActivityResolver.CreateRng(rngOwnerId, seedGameTime, worldSeed, salt);
            return rng.NextDouble() < GetEncounterChance(stage);
        }

        /// <summary>마주친 사냥꾼 한 명의 결과: 부상 또는 실패(도망). 파티는 구성원마다 굴린다 (salt 211, 220+i).</summary>
        public static NpcHuntOutcome RollEncounterOutcome(float capability, int rngOwnerId, float seedGameTime, int worldSeed, int salt)
        {
            HistoryRng rng = NpcActivityResolver.CreateRng(rngOwnerId, seedGameTime, worldSeed, salt);
            return rng.NextDouble() < GetWoundChance(capability) ? NpcHuntOutcome.Wounded : NpcHuntOutcome.Failed;
        }

        // ---------------- 결정론 해시 ----------------

        public static ulong Seed(int worldSeed) => DerivedSeed.Derive(worldSeed, DerivedSeed.ChannelNamedThreat);

        /// <summary>(세계, 네임드, 용도, 추가 값)의 고정 무작위. 다시 불러와도 같다.</summary>
        public static ulong Hash(int worldSeed, int threatId, int purpose, int extra = 0)
        {
            ulong x = Seed(worldSeed);
            x ^= unchecked((ulong)(uint)threatId * 0x9E3779B97F4A7C15UL);
            x = Mix(x);
            x ^= unchecked((ulong)(uint)purpose * 0xC2B2AE3D27D4EB4FUL);
            x = Mix(x);
            x ^= unchecked((ulong)(uint)extra * 0x165667B19E3779F9UL);
            return Mix(x);
        }

        public static HistoryRng CreateRng(int worldSeed, int threatId, int purpose, int extra = 0)
            => new HistoryRng(Hash(worldSeed, threatId, purpose, extra));

        private static ulong Mix(ulong x)
        {
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        // ---------------- 영역과 배회 지점 (§4.3·§5.3) ----------------

        /// <summary>영역 후보 방향 8개의 시도 순서 (고정 무작위 순열). order 길이 = PlacementCandidateCount. 호출자의 scratch 배열을 채운다.</summary>
        public static void FillCandidateOrder(int worldSeed, int threatId, int[] order)
        {
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            HistoryRng rng = CreateRng(worldSeed, threatId, PurposeTerritoryOrder);
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
        }

        /// <summary>
        /// 영역 후보 중심 (방향 칸 slot = 0~7). 주 마을 중심에서 원점 반대 방향 ±60° 부채꼴을 8칸으로 나눈 방향으로 48~64걸음.
        /// 원점에 있는 마을(시작 마을)은 아무 방향이나 쓴다 (§4.3).
        /// </summary>
        public static Vector2 GetTerritoryCandidate(Vector2 villageCenter, int worldSeed, int threatId, int slot)
        {
            float baseAngle;
            float halfSpread;
            if (villageCenter.sqrMagnitude < 1f)
            {
                baseAngle = 0f;
                halfSpread = 180f;
            }
            else
            {
                baseAngle = Mathf.Atan2(villageCenter.y, villageCenter.x) * Mathf.Rad2Deg;
                halfSpread = AwayFromOriginHalfAngle;
            }
            float t = (Mathf.Clamp(slot, 0, PlacementCandidateCount - 1) + 0.5f) / PlacementCandidateCount;
            float angle = (baseAngle - halfSpread + t * halfSpread * 2f) * Mathf.Deg2Rad;
            HistoryRng distanceRng = CreateRng(worldSeed, threatId, PurposeTerritoryDistance, slot);
            float distance = Mathf.Lerp(TerritoryDistanceMin, TerritoryDistanceMax, (float)distanceRng.NextDouble());
            return villageCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
        }

        /// <summary>영역 원이 마을 사각형(경계 + 여백)과 겹치는가 (§4.3).</summary>
        public static bool OverlapsVillage(Vector2 center, float radius, int boundsX, int boundsY, int boundsW, int boundsH,
            Vector2 villageCenter)
        {
            if (boundsW <= 0 || boundsH <= 0)
            {
                // 경계가 없는 마을은 중심 주변 여백만 본다
                float minimum = radius + VillageExclusionMargin;
                return (center - villageCenter).sqrMagnitude < minimum * minimum;
            }
            float minX = boundsX - VillageExclusionMargin;
            float minY = boundsY - VillageExclusionMargin;
            float maxX = boundsX + boundsW + VillageExclusionMargin;
            float maxY = boundsY + boundsH + VillageExclusionMargin;
            float closestX = Mathf.Clamp(center.x, minX, maxX);
            float closestY = Mathf.Clamp(center.y, minY, maxY);
            float dx = center.x - closestX;
            float dy = center.y - closestY;
            return dx * dx + dy * dy < radius * radius;
        }

        public static bool OverlapsTerritory(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < (TerritoryRadius * 2f) * (TerritoryRadius * 2f);

        /// <summary>배회 지점 번호 k = floor(게임시각 ÷ 6) (§5.3).</summary>
        public static int GetRoamStep(float now) => Mathf.FloorToInt(Mathf.Max(0f, now) / RoamStepHours);

        /// <summary>배회 지점 = 영역 안의 고정 무작위 점(네임드 ID, k). 게임과 진단이 같은 순수 함수로 계산한다 (§5.3).</summary>
        public static Vector2 GetRoamPoint(int worldSeed, int threatId, Vector2 territory, int step)
        {
            HistoryRng rng = CreateRng(worldSeed, threatId, PurposeRoam, step);
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt((float)rng.NextDouble()) * TerritoryRadius * RoamPointRadiusFraction;
            return territory + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        public static bool IsInsideTerritory(Vector2 territory, Vector2 position)
            => (position - territory).sqrMagnitude <= TerritoryRadius * TerritoryRadius;

        /// <summary>from에서 to를 본 8방위 (0 북 … 7 북서, 화면 위 = 북). 같은 점이면 -1.</summary>
        public static int GetBearing(Vector2 from, Vector2 to)
        {
            Vector2 offset = to - from;
            if (offset.sqrMagnitude < 0.0001f)
                return -1;
            float angleFromNorth = Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg;
            return (Mathf.RoundToInt(angleFromNorth / 45f) % 8 + 8) % 8;
        }

        // ---------------- 보상 ----------------

        /// <summary>의뢰 보답 = 기존 10~20골드 × 3 (§8.4, Q5). 인스턴스 ID로 정하므로 재시도해도 같다.</summary>
        public static int GetRequestReward(string instanceId)
            => ARPG.Npc.Narrative.NarrativeReactionRules.RewardAmount(instanceId) * RequestRewardMultiplier;
    }
}
