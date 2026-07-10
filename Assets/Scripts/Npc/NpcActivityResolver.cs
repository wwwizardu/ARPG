#nullable enable
using ARPG.History;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 활동 선택과 Hunt 결과를 결정론적으로 계산한다.
    /// 상태 적용과 저장은 호출자인 System_NpcSim이 담당한다.
    /// </summary>
    public static class NpcActivityResolver
    {
        // 모두 게임시간(hour) 단위. 튜닝값을 한곳에 두어 상태 전이 코드와 분리한다.
        public const float RestDurationHours = 4f;
        public const float HuntDurationHours = 8f;
        public const float WoundedRecoveryHours = 24f;

        public static NpcActivityKind SelectNextActivity(NpcSaveData saveData, int entityId, float startGameTime, int worldSeed)
        {
            HistoryRng rng = CreateRng(entityId, startGameTime, worldSeed, 0);
            // 인내심은 휴식 성향을, 용기·호기심은 사냥 성향을 높인다.
            // 최고 점수 선택이 아니라 가중 랜덤으로 선택해 같은 성향의 NPC도 행동 다양성을 갖는다.
            float restWeight = 75f + saveData.NpcStat.Patience * 0.35f;
            float huntWeight = 25f + saveData.NpcStat.Courage * 0.75f + saveData.NpcStat.Curiosity * 0.15f;
            float totalWeight = restWeight + huntWeight;
            float pick = (float)(rng.NextDouble() * totalWeight);
            return pick < restWeight ? NpcActivityKind.Rest : NpcActivityKind.Hunt;
        }

        public static NpcHuntOutcome ResolveHunt(NpcSaveData saveData, int entityId,
            float startGameTime, int worldSeed)
        {
            HistoryRng rng = CreateRng(entityId, startGameTime, worldSeed, 1);
            // v1 전투력은 영속 성향과 SkillLevel만 사용한다. 장비·Zone 위험도는 후속 단계에서 합산한다.
            float courage = Mathf.Clamp01(saveData.NpcStat.Courage / 100f);
            float skill = Mathf.Clamp01(saveData.SkillLevel / 10f);
            float capability = Mathf.Clamp01(0.20f + courage * 0.55f + skill * 0.25f);

            float deathChance = Mathf.Lerp(0.08f, 0.01f, capability);
            float woundedChance = Mathf.Lerp(0.32f, 0.08f, capability);
            float successChance = Mathf.Lerp(0.40f, 0.86f, capability);
            float roll = (float)rng.NextDouble();

            // 심각한 결과부터 먼저 소비한다. 남은 확률은 평범한 실패로 귀결된다.
            if (roll < deathChance)
                return NpcHuntOutcome.Dead;
            if (roll < deathChance + woundedChance)
                return NpcHuntOutcome.Wounded;
            if (roll < deathChance + woundedChance + successChance)
                return NpcHuntOutcome.Success;
            return NpcHuntOutcome.Failed;
        }

        /// <summary>성공한 Hunt가 마을에 가져오는 최소 식량 보상. v1은 Food만 다룬다.</summary>
        public static int GetHuntFoodYield(NpcSaveData saveData, int entityId,
            float startGameTime, int worldSeed)
        {
            HistoryRng rng = CreateRng(entityId, startGameTime, worldSeed, 2);
            float capability = Mathf.Clamp01(saveData.NpcStat.Courage / 100f + saveData.SkillLevel / 20f);
            int baseYield = capability >= 0.70f ? 3 : capability >= 0.35f ? 2 : 1;
            return baseYield + (rng.Chance(0.20d) ? 1 : 0);
        }

        /// <summary>
        /// Hunt 대상 좌표를 결정론적으로 고른다. 실제 경로 탐색은 하지 않고 오프스크린 보간용 종점만 만든다.
        /// </summary>
        public static Vector2 GetHuntTargetPosition(Vector2 origin, int entityId, float startGameTime,
            int worldSeed, float minX, float maxX, float minY, float maxY)
        {
            HistoryRng rng = CreateRng(entityId, startGameTime, worldSeed, 3);
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float distance = Mathf.Lerp(8f, 16f, (float)rng.NextDouble());
            Vector2 target = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            target.x = Mathf.Clamp(target.x, minX, maxX);
            target.y = Mathf.Clamp(target.y, minY, maxY);
            return target;
        }

        /// <summary>
        /// Hunt 단일 구간의 analytic 좌표. 전반은 사냥 지점으로, 후반은 출발지로 돌아온다.
        /// </summary>
        public static Vector2 EvaluateHuntPosition(NpcActivityState activity, float gameTime)
        {
            if (activity.HasMovementPath == false || activity.PhaseEndGameTime <= activity.PhaseStartedGameTime)
                return activity.OriginPosition;

            float progress = Mathf.Clamp01((gameTime - activity.PhaseStartedGameTime) /
                (activity.PhaseEndGameTime - activity.PhaseStartedGameTime));
            float outboundFraction = Mathf.Clamp01(activity.OutboundFraction);
            if (outboundFraction <= 0f)
                return Vector2.Lerp(activity.TargetPosition, activity.ReturnPosition, progress);
            if (outboundFraction >= 1f || progress <= outboundFraction)
                return Vector2.Lerp(activity.OriginPosition, activity.TargetPosition, progress / outboundFraction);
            return Vector2.Lerp(activity.TargetPosition, activity.ReturnPosition,
                (progress - outboundFraction) / (1f - outboundFraction));
        }

        private static HistoryRng CreateRng(int entityId, float startGameTime, int worldSeed, int salt)
        {
            // 활동 시작시각을 1/100 게임시간 단위로 양자화한다.
            // 같은 월드·NPC·활동 시작시각·판정 종류는 세이브/로드 뒤에도 같은 난수열을 얻는다.
            int startTick = Mathf.FloorToInt(startGameTime * 100f);
            // salt=0은 활동 선택, salt=1은 Hunt 결과, salt=2는 보상, salt=3은 목표 좌표에 사용한다.
            int key = unchecked(worldSeed ^ (entityId * 486187739) ^ (startTick * 16777619) ^ salt);
            return new HistoryRng(DerivedSeed.Derive(key, DerivedSeed.ChannelNpcActivity));
        }
    }
}
