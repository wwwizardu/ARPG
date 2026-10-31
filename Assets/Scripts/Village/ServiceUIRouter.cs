#nullable enable
using ARPG.Component;
using ARPG.UI;
using UnityEngine;

namespace ARPG.Village
{
    /// <summary>
    /// Interact(E) 키 입력 시 PlayerNearbyServicesComponent를 보고 가장 가까운 후보의 UI 라우팅.
    /// 후보: 마을 시설(Shop/Forge/Inn/Shrine) + V4 VillageHub(100)·Gate(벽 세그먼트) + 활성 NPC + 던전 입구·상자 + 월드 광맥.
    /// 정책: 임계 거리(System_VillageServiceProximity 상수) 안에 들어온 후보 중 거리 sqr 최소.
    /// 각 UI 클래스의 Bind(entityId)로 어떤 엔티티 기준인지 전달.
    /// 후보 선택 로직은 System_VillageInteractionPrompt의 프롬프트 표시와 공유한다 (1건물=1상호작용,
    /// 프롬프트에 보이는 대상과 E키가 여는 대상이 항상 같도록).
    /// </summary>
    public static class ServiceUIRouter
    {
        public enum CandidateKind { None, Shop, Forge, Inn, Shrine, Hub, Gate, Npc, Dungeon, DungeonChest, OreVein }

        // 던전 안 NPC 상호작용 임계 — 월드 NPC 임계(System_VillageServiceProximity, 1.5타일)와 같다
        private const float DUNGEON_NPC_RANGE = 1.5f;

        public struct Candidate
        {
            public CandidateKind Kind;
            public int EntityId;          // 시설/NPC/광맥 후보의 엔티티. Gate는 -1
            public int GateVillageId;     // Gate 후보 전용
            public int GateSegmentId;
            public bool GateIsOpen;
            public float DistanceSqr;
            public int TileX;             // 프롬프트 표시 위치 (시설/Gate 타일)
            public int TileY;
            public bool HasTile;          // false = NPC(타일 좌표 없음, Transform 사용)
            public string DungeonId;
            public int DungeonObjectId;
        }

        /// <summary>
        /// 거리 최소 후보 선택 — 프롬프트 표시와 E키 라우팅이 같은 결과를 공유한다.
        /// </summary>
        public static bool TryGetBestCandidate(in PlayerNearbyServicesComponent cached,
            out Candidate candidate)
        {
            candidate = new Candidate { Kind = CandidateKind.None, EntityId = -1 };
            if (AR.s.UI.HasOpenInteractionMenu || AR.s.Dungeon.GameplayPaused) return false;
            if (AR.s.Npc.IsPlayerInDuel == true) return false;
            if (AR.s.Dungeon.IsInside) return TryGetDungeonCandidate(out candidate);
            if (AR.s.Dungeon.GameplayPaused) return false;
            // Prompt and E both compare current positions, including moving NPCs.
            AR.s.System.GetSystem<Systems.System_VillageServiceProximity>()?.OnUpdate(0);
            var nearby = cached;
            if (AR.s.Component.TryGetComponent<PlayerNearbyServicesComponent>(AR.s.Data.CurrentPlayerEntityId, out var refreshed))
                nearby = refreshed;
            float bestSqr = float.MaxValue;

            if ((nearby.AvailableServices & ProvidedService.Shop) != 0
                && nearby.NearestShopEntityId >= 0
                && nearby.NearestShopDistanceSqr < bestSqr)
            {
                SetFacilityCandidate(ref candidate, CandidateKind.Shop,
                    nearby.NearestShopEntityId, nearby.NearestShopDistanceSqr);
                bestSqr = nearby.NearestShopDistanceSqr;
            }
            if ((nearby.AvailableServices & ProvidedService.Forge) != 0
                && nearby.NearestForgeEntityId >= 0
                && nearby.NearestForgeDistanceSqr < bestSqr)
            {
                SetFacilityCandidate(ref candidate, CandidateKind.Forge,
                    nearby.NearestForgeEntityId, nearby.NearestForgeDistanceSqr);
                bestSqr = nearby.NearestForgeDistanceSqr;
            }
            // Inn — VILLAGE_NPC_BOUNDARY_DESIGN에 따라 휴식/세이브 전용 건물 서비스 UI만 연다.
            if ((nearby.AvailableServices & ProvidedService.Inn) != 0
                && nearby.NearestInnEntityId >= 0
                && nearby.NearestInnDistanceSqr < bestSqr)
            {
                SetFacilityCandidate(ref candidate, CandidateKind.Inn,
                    nearby.NearestInnEntityId, nearby.NearestInnDistanceSqr);
                bestSqr = nearby.NearestInnDistanceSqr;
            }
            if ((nearby.AvailableServices & ProvidedService.Shrine) != 0
                && nearby.NearestShrineEntityId >= 0
                && nearby.NearestShrineDistanceSqr < bestSqr)
            {
                SetFacilityCandidate(ref candidate, CandidateKind.Shrine,
                    nearby.NearestShrineEntityId, nearby.NearestShrineDistanceSqr);
                bestSqr = nearby.NearestShrineDistanceSqr;
            }
            // V4: VillageHub(100) — 마을 거점 이용 (체크포인트 세이브)
            if (nearby.NearestHubEntityId >= 0
                && nearby.NearestHubDistanceSqr < bestSqr)
            {
                SetFacilityCandidate(ref candidate, CandidateKind.Hub,
                    nearby.NearestHubEntityId, nearby.NearestHubDistanceSqr);
                bestSqr = nearby.NearestHubDistanceSqr;
            }
            // V4: 기능성 Gate — 개폐 상호작용
            if (nearby.NearestGateVillageId >= 0
                && nearby.NearestGateDistanceSqr < bestSqr)
            {
                candidate = new Candidate
                {
                    Kind = CandidateKind.Gate,
                    EntityId = -1,
                    GateVillageId = nearby.NearestGateVillageId,
                    GateSegmentId = nearby.NearestGateSegmentId,
                    GateIsOpen = nearby.NearestGateIsOpen,
                    DistanceSqr = nearby.NearestGateDistanceSqr,
                    TileX = nearby.NearestGateTileX,
                    TileY = nearby.NearestGateTileY,
                    HasTile = true,
                };
                bestSqr = nearby.NearestGateDistanceSqr;
            }
            if (nearby.NearestNpcEntityId >= 0 && nearby.NearestNpcDistanceSqr < bestSqr)
            {
                candidate = new Candidate
                {
                    Kind = CandidateKind.Npc,
                    EntityId = nearby.NearestNpcEntityId,
                    DistanceSqr = nearby.NearestNpcDistanceSqr,
                    HasTile = false,
                };
            }
            // Civic은 현재 UI 라우팅 대상 아님 (Phase E 이상)

            if (candidate.Kind == CandidateKind.Npc) bestSqr = candidate.DistanceSqr;
            if (AR.s.Dungeon.TryGetInteraction(out var dungeon) && dungeon.DistanceSqr < bestSqr)
            {
                candidate = dungeon;
                bestSqr = dungeon.DistanceSqr;
            }
            // 월드 광맥 (WORLD_ORE_VEIN_PLAN §7.1): 1.5칸 안의 가장 가까운 광맥이 다른 후보보다 가까우면 채광
            if (AR.s.OreVein.TryGetInteraction(out var vein) && vein.DistanceSqr < bestSqr)
                candidate = vein;

            return candidate.Kind != CandidateKind.None;
        }

        /// <summary>
        /// 후보의 상호작용 프롬프트 텍스트 (V0 InteractiveFacility 계약 문자열).
        /// System_VillageInteractionPrompt가 "[E] {prompt}"로 표시한다.
        /// </summary>
        public static string GetPrompt(in Candidate candidate)
        {
            switch (candidate.Kind)
            {
                case CandidateKind.Dungeon: return "던전 입장";
                case CandidateKind.DungeonChest: return "상자 열기";
                case CandidateKind.OreVein: return AR.s.OreVein.GetInteractionPrompt(candidate.EntityId);
                case CandidateKind.Shop:
                    return GetFacilityPrompt(VillageInitialFacilityCatalog.MERCHANT_STALL_TABLE_ID, "거래");
                case CandidateKind.Forge:
                    return GetFacilityPrompt(VillageInitialFacilityCatalog.FORGE_TABLE_ID, "제련·수리");
                case CandidateKind.Inn:
                    return GetFacilityPrompt(VillageInitialFacilityCatalog.INN_TABLE_ID, "휴식");
                case CandidateKind.Shrine:
                    return GetFacilityPrompt(VillageInitialFacilityCatalog.SHRINE_TABLE_ID, "기도");
                case CandidateKind.Hub:
                    return GetFacilityPrompt(VillageInitialFacilityCatalog.VILLAGE_HUB_TABLE_ID, "마을 거점 이용");
                case CandidateKind.Gate:
                    return candidate.GateIsOpen ? "문 닫기" : "문 열기";
                case CandidateKind.Npc:
                    return "살펴보기";
                default:
                    return string.Empty;
            }
        }

        public static bool Open(in PlayerNearbyServicesComponent nearby)
        {
            if (AR.s.Npc.IsPlayerInDuel == true)
            {
                AR.s.UI.SetNotify("결투 중에는 상호작용할 수 없습니다.");
                return false;
            }
            if (TryGetBestCandidate(nearby, out Candidate candidate) == false)
                return false;

            int bestEntityId = candidate.EntityId;
            float bestSqr = candidate.DistanceSqr;
            switch (candidate.Kind)
            {
                case CandidateKind.Dungeon:
                    _ = AR.s.Dungeon.EnterAsync(candidate.DungeonId);
                    return true;
                case CandidateKind.DungeonChest:
                    return AR.s.Dungeon.OpenChest(candidate.DungeonObjectId);
                case CandidateKind.OreVein:
                    // 진행·취소·완료는 System_PlayerMining이 판단한다
                    return AR.s.OreVein.TryBeginMining(AR.s.Data.CurrentPlayerEntityId, candidate.EntityId);
                case CandidateKind.Shop:
                {
                    Debug.Log($"[Service] v{nearby.NearestVillageId} 열기: Shop (entityId={bestEntityId}, sqr={bestSqr:F2})");
                    var ui = AR.s.UI.Show<UIShopMerchant>(AddressablePath.ShopMerchant, UIManager.Layer.Main);
                    ui?.Bind(bestEntityId);
                    return true;
                }
                case CandidateKind.Forge:
                {
                    Debug.Log($"[Service] v{nearby.NearestVillageId} 열기: Forge (entityId={bestEntityId}, sqr={bestSqr:F2})");
                    var ui = AR.s.UI.Show<UIForge>(AddressablePath.Forge, UIManager.Layer.Main);
                    ui?.Bind(bestEntityId);
                    return true;
                }
                case CandidateKind.Inn:
                {
                    Debug.Log($"[Service] v{nearby.NearestVillageId} 열기: Inn (entityId={bestEntityId}, sqr={bestSqr:F2})");
                    var ui = AR.s.UI.Show<UIInn>(AddressablePath.Inn, UIManager.Layer.Main);
                    ui?.Bind(bestEntityId);
                    return true;
                }
                case CandidateKind.Shrine:
                {
                    Debug.Log($"[Service] v{nearby.NearestVillageId} 열기: Shrine (entityId={bestEntityId}, sqr={bestSqr:F2})");
                    var ui = AR.s.UI.Show<UIShrine>(AddressablePath.Shrine, UIManager.Layer.Main);
                    ui?.Bind(bestEntityId);
                    return true;
                }
                case CandidateKind.Hub:
                {
                    // V4: VillageHub 확정 기능 = 체크포인트 세이브 (V0 계약: 체크포인트·세이브·귀환 중 확정 기능).
                    Debug.Log($"[Service] v{nearby.NearestVillageId} 거점 이용: 체크포인트 세이브 (entityId={bestEntityId}, sqr={bestSqr:F2})");
                    AR.s.Data.Save();
                    AR.s.UI.SetNotify("마을 거점: 진행 상황을 기록했습니다");
                    return true;
                }
                case CandidateKind.Gate:
                {
                    Debug.Log($"[Service] v{candidate.GateVillageId} Gate 개폐: seg{candidate.GateSegmentId} (sqr={bestSqr:F2})");
                    return AR.s.Village.ToggleGate(candidate.GateVillageId,
                        candidate.GateSegmentId);
                }
                case CandidateKind.Npc:
                {
                    Debug.Log($"[Service] NPC 열기: entityId={bestEntityId}, sqr={bestSqr:F2}");
                    var ui = AR.s.UI.Show<UINpcInspect>(AddressablePath.NpcInspect, UIManager.Layer.Main);
                    ui?.Bind(bestEntityId);
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// 던전 안 후보: 상자 + 실체화된 NPC(동행 NPC). NPC 대화 메뉴에서 던전 안 결투 신청을 할 수 있게 한다 (NPC_COMBAT_DESIGN §7.2 D19·D23).
        /// 월드용 근접 집계(System_VillageServiceProximity)는 던전에서 돌지 않고, 동행 NPC는 월드 청크 인덱스에 없으므로 NpcTag 풀을 직접 본다.
        /// </summary>
        private static bool TryGetDungeonCandidate(out Candidate candidate)
        {
            bool found = AR.s.Dungeon.TryGetInteraction(out candidate);
            if (AR.s.Dungeon.IsBusy == true)
                return found;

            int playerId = AR.s.Data.CurrentPlayerEntityId;
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<TransformComponent>(playerId, out var playerTransform) == false)
                return found;

            float bestSqr = found == true ? candidate.DistanceSqr : DUNGEON_NPC_RANGE * DUNGEON_NPC_RANGE;
            SparseSet<NpcTag> npcPool = cm.GetComponentPool<NpcTag>();
            for (int i = 0; i < npcPool.Count; i++)
            {
                int npcId = npcPool.GetEntityId(i);
                if (cm.HasComponent<DestroyTag>(npcId) == true ||
                    cm.TryGetComponent<TransformComponent>(npcId, out var npcTransform) == false)
                    continue;

                float sqr = (npcTransform.Position - playerTransform.Position).sqrMagnitude;
                if (sqr > DUNGEON_NPC_RANGE * DUNGEON_NPC_RANGE || sqr >= bestSqr)
                    continue;

                bestSqr = sqr;
                found = true;
                candidate = new Candidate
                {
                    Kind = CandidateKind.Npc,
                    EntityId = npcId,
                    DistanceSqr = sqr,
                    HasTile = false,
                };
            }
            return found;
        }

        private static void SetFacilityCandidate(ref Candidate candidate,
            CandidateKind kind, int entityId, float distanceSqr)
        {
            candidate = new Candidate
            {
                Kind = kind,
                EntityId = entityId,
                DistanceSqr = distanceSqr,
                HasTile = false,
            };
            if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(entityId,
                    out var po) == true)
            {
                candidate.TileX = po.TileX;
                candidate.TileY = po.TileY;
                candidate.HasTile = true;
            }
        }

        private static string GetFacilityPrompt(int tableId, string fallback)
        {
            if (VillageInitialFacilityCatalog.TryGet(tableId,
                    out VillageInitialFacilityDefinition definition) == true)
                return definition.Prompt;
            return fallback;
        }
    }
}
