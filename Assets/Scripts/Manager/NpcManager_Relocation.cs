#nullable enable
using System.Globalization;
using ARPG.Component;
using ARPG.History;
using ARPG.Tables;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Npc
{
    /// <summary>
    /// 사라진 터(폐허·합병)에 소속된 NPC의 강제 이주 (NPC_AMBITION_DESIGN D138~D141, NS4의 첫 부분).
    /// 터에 잠깐 머무는 것은 괜찮지만 그곳에서 살지는 않는다 — 체류 기한이 지나면 다음 활동을 고를 때
    /// 가장 가까운 상점 마을로 편도 이동하고, 도착하면 소속(VillageId)과 마을 명단을 옮긴다.
    /// 게임(System_NpcSim)과 진단(NpcAmbitionSimDiagnostics)이 같은 두 메서드를 부른다.
    /// 자발적 이주(살아 있는 마을 → 다른 마을·터)의 계기는 NS4 후속이다.
    /// </summary>
    public partial class NpcManager
    {
        // D139: 터 체류 기한. 터 소속으로 처음 확인된 때부터 센다.
        public const float AbandonedSiteStayHours = 72f;
        // D140: 마을 간 편도 이동 속도(칸/게임시간)와 최소 이동 시간. 터 → 가장 가까운 상점 마을 36~101칸 → 약 3~8.5시간.
        public const float RelocationTilesPerHour = 12f;
        public const float RelocationMinHours = 1f;

        /// <summary>
        /// 활동 선택 직전에 부른다. 사라진 터 소속이고 체류 기한이 지났으면 이주를 시작하고 true.
        /// 처음 확인한 터 소속 NPC는 체류 시작 시각만 기록하고 false (기존 세이브 포함 — 로드 뒤 72h 안에 떠난다).
        /// 화면 안 NPC는 시작하지 않는다 — 이동 중에는 그 자리에 멈춰 서므로 화면 밖으로 나간 뒤 떠난다.
        /// </summary>
        public bool TryStartRelocation(int entityId, NpcSaveData saveData, float startGameTime, int worldSeed)
        {
            VillageData? home = saveData.VillageId >= 0 ? AR.s.Village.GetVillage(saveData.VillageId) : null;
            if (home == null || AR.s.Village.IsAbandonedSite(home) == false)
            {
                saveData.AbandonedSiteSinceGameTime = -1f;
                return false;
            }
            if (saveData.AbandonedSiteSinceGameTime < 0f)
            {
                saveData.AbandonedSiteSinceGameTime = startGameTime;
                return false;
            }
            if (startGameTime - saveData.AbandonedSiteSinceGameTime < AbandonedSiteStayHours)
                return false;
            if (saveData.IsActive == true || saveData.IsSpawning == true)
                return false;

            VillageData? destination = AR.s.Village.FindNearestLivingShopVillage(home.Position, home.VillageId);
            if (destination == null)
                return false;

            Vector2 target = GetRelocationArrivalPosition(destination, entityId, startGameTime, worldSeed);
            float hours = Mathf.Max(RelocationMinHours, Vector2.Distance(saveData.Position, target) / RelocationTilesPerHour);

            NpcActivityState activity = saveData.Activity;
            activity.Kind = NpcActivityKind.Relocate;
            activity.Phase = NpcActivityPhase.Venturing;
            activity.TargetKey = destination.VillageId.ToString(CultureInfo.InvariantCulture);
            activity.PhaseStartedGameTime = startGameTime;
            activity.PhaseEndGameTime = startGameTime + hours;
            activity.HasMovementPath = true;
            activity.OriginPosition = saveData.Position;
            activity.TargetPosition = target;
            activity.ReturnPosition = target;
            activity.OutboundFraction = 1f;
            activity.OutcomeSeedGameTime = startGameTime;
            saveData.LastSimGameTime = startGameTime;
            return true;
        }

        /// <summary>
        /// 이주 도착 (Relocate 종료 시각). 소속과 마을 명단을 목적지로 옮기고 Idle로 돌린다. 위치는 이동 경로가 이미 도착점에 둔다
        /// (게임: System_NpcSim의 analytic 위치 갱신). 목적지가 사라졌으면 소속을 바꾸지 않는다 — 기한이 지난 상태라 다음 선택에서 다시 고른다.
        /// 이주 기록 Deed는 남기지 않는다 (대화 사연 자리 경쟁 D109 ① — 거주 이력은 NS4에서 함께 정한다).
        /// </summary>
        public void CompleteRelocation(int entityId, NpcSaveData saveData, float arrivalGameTime)
        {
            NpcActivityState activity = saveData.Activity;
            bool parsed = int.TryParse(activity.TargetKey, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int destinationId);

            activity.Phase = NpcActivityPhase.Idle;
            activity.Kind = NpcActivityKind.None;
            activity.TargetKey = string.Empty;
            activity.HasMovementPath = false;
            activity.PhaseStartedGameTime = arrivalGameTime;
            activity.PhaseEndGameTime = 0f;
            saveData.LastSimGameTime = arrivalGameTime;

            int fromVillageId = saveData.VillageId;
            if (parsed == true && destinationId != fromVillageId && AR.s.Village.GetVillage(destinationId) != null)
            {
                AR.s.Village.UnregisterNpcFromVillage(fromVillageId, entityId);
                saveData.VillageId = destinationId;
                AR.s.Village.RegisterNpcToVillage(destinationId, entityId);
                saveData.AbandonedSiteSinceGameTime = -1f;
                if (saveData.IsActive == true &&
                    AR.s.Component.TryGetComponent<NpcVillageComponent>(entityId, out NpcVillageComponent village))
                {
                    village.VillageId = destinationId;
                    AR.s.Component.SetComponent(entityId, village);
                }
                // 소속 마을이 대화 사실(고향·상점·소문 범위)에 들어가므로 캐시를 지운다.
                _narrative.InvalidateDialogues(entityId);
                Debug.Log($"[NpcRelocation] npc={entityId} v{fromVillageId} -> v{destinationId} at {arrivalGameTime:F1}h");
            }

            ResumeMaterializedNpcActivity(entityId);
        }

        /// <summary>목적지 마을 안 도착 좌표: 마을 중심에서 스폰 반경 안의 결정론 지점 (salt 190). 막힌 칸은 실체화 때 보정한다.</summary>
        private static Vector2 GetRelocationArrivalPosition(VillageData destination, int entityId, float startGameTime,
            int worldSeed)
        {
            VillageTable? table = AR.s.Data.GetVillageTable(destination.TableId);
            float radius = table != null ? table.SpawnRadius : 0f;
            if (radius <= 0f)
                return destination.Position;
            HistoryRng rng = NpcActivityResolver.CreateRng(entityId, startGameTime, worldSeed,
                NpcActivityResolver.RelocationArrivalSalt);
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float distance = (float)rng.NextDouble() * radius;
            return destination.Position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
        }
    }
}
