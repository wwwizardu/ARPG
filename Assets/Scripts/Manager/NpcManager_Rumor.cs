#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Creature;
using ARPG.Data;
using ARPG.History;
using ARPG.Village;
using GE = GlobalEnum;

namespace ARPG.Npc
{
    /// <summary>
    /// NPC 야망 P2b — 소문 (NPC_AMBITION_DESIGN §5.2·§6.2·§7.4, D50·D51·D56, 결정 D70~D79).
    /// - 모든 소문은 사실이다. 대상이 사라진 소문은 저장된 채 남을 수 있지만, 노출·사용 직전 IsRumorValid가 항상 걸러낸다 (D72).
    ///   NPC 쪽 사건(NPC 던전 완료·사망·장비 교체)은 그 마을 명단의 소문을 즉시 정리하고, Rest 종료 때 자기 목록을 정리한다.
    /// - 소문은 소속 마을 안에서만 퍼진다 (D75). 그래서 사건 정리는 그 마을 명단만 훑는다.
    /// - P4b: 판매(ItemSold)·배신(Betrayal) 소문의 생성과 늦은 발각은 NpcManager_Theft가 맡고, 여기서는 유효성·같은 대상·교환만 다룬다.
    /// - 상태는 전부 NpcSaveData.Rumors에 있다. 이 파일이 가진 인스턴스 필드는 휘발성 진단 카운터와 매번 비우는 스크래치 버퍼뿐이다.
    /// </summary>
    public partial class NpcManager
    {
        // NPC당 최대 보유 수 (§5.2)
        public const int MaxRumorsPerNpc = 8;
        // "유망한 소문"의 신선도: 사건 뒤 72 게임시간 이내 (D76)
        public const float PromisingRumorFreshHours = 72f;
        // "유망한 소문"의 가치 기준 (D76). 전리품 BasePrice × 수량. 진단(Ambition Sim)으로 정한 개발 튜닝 상수다.
        public const int PromisingRumorValueMin = 20;
        // 던전 소문 방향 표현: 입구가 마을 중심에서 이 거리(월드 유닛) 안이면 방향 없이 "이 근처"로 말한다 (P2c)
        public const float RumorBearingMinDistance = 2f;

        // 소문 진단 카운터 (휘발성). Reset에서 새로 만든다. 진단 도구는 시뮬레이션 동안 새 인스턴스로 바꿔 끼운다.
        private NpcRumorCounters _rumorCounters = new();
        // 교환 상대 후보 스크래치 버퍼 — 사용 전에 매번 Clear하므로 호출 사이에 상태를 남기지 않는다.
        private readonly List<int> _rumorPeerScratch = new();

        public NpcRumorCounters RumorCounters => _rumorCounters;

        /// <summary>
        /// 진단 전용 독립 감사: 노출·사용되는 소문의 유효성을 IsRumorValid와 다른 경로로 판정한다. false면 ExposedInvalid를 센다.
        /// 기본 null (게임 중에는 쓰지 않는다).
        /// </summary>
        public Func<NpcRumor, bool>? RumorExposureAudit { get; set; }

        /// <summary>진단 전용: 직접 겪은 소문이 생길 때마다 (보유자 Id, 소문)으로 호출된다. 전파 곡선 집계용. 기본 null.</summary>
        public Action<int, NpcRumor>? RumorCreatedObserver { get; set; }

        // ================= 검증 =================

        /// <summary>
        /// 소문의 대상이 지금도 존재하는가 (D51·D72 정본). DungeonInfo = 슬롯 현재 세대가 같음,
        /// ItemOwner = 소유자가 살아 있고 그 장비 슬롯에 같은 아이템 인스턴스가 있음,
        /// ItemSold = 판매자가 살아 있음(이미 일어난 판매라 뒤집히지 않음, D104), Betrayal = 가해자·피해자 둘 다 살아 있음(D105).
        /// </summary>
        public bool IsRumorValid(NpcRumor? rumor)
        {
            if (rumor == null)
                return false;

            switch (rumor.Kind)
            {
                case NpcRumorKind.DungeonInfo:
                    return AR.s.Dungeon != null &&
                           AR.s.Dungeon.TryGetNpcDungeonSlotGeneration(rumor.VillageId, rumor.SlotIndex, out int generation) == true &&
                           generation == rumor.Generation;
                case NpcRumorKind.ItemOwner:
                    return TryGetRumorItem(rumor, out _) == true;
                case NpcRumorKind.ItemSold:
                    return IsLivingRumorNpc(rumor.SubjectNpcId) == true;
                case NpcRumorKind.Betrayal:
                    return IsLivingRumorNpc(rumor.SubjectNpcId) == true && IsLivingRumorNpc(rumor.VictimNpcId) == true;
                default:
                    return false;
            }
        }

        private bool IsLivingRumorNpc(int entityId)
        {
            return entityId >= 0 && _npcSaveDict.TryGetValue(entityId, out NpcSaveData? npc) == true &&
                   npc.Condition != CharacterConditions.Dead && npc.Activity.Phase != NpcActivityPhase.Dead;
        }

        /// <summary>ItemOwner 소문이 가리키는 아이템(유효할 때만). 표시용 이름·ItemId는 여기서 읽는다.</summary>
        public bool TryGetRumorItem(NpcRumor rumor, out ItemData? item)
        {
            item = null;
            if (rumor.Kind != NpcRumorKind.ItemOwner || rumor.ItemInstanceId == 0 ||
                _npcSaveDict.TryGetValue(rumor.SubjectNpcId, out NpcSaveData? owner) == false)
                return false;
            if (owner.Condition == CharacterConditions.Dead || owner.Activity.Phase == NpcActivityPhase.Dead)
                return false;
            if (owner.InventoryEquip == null || rumor.EquipSlot < 0 || rumor.EquipSlot >= owner.InventoryEquip.Length)
                return false;

            ItemData? equipped = owner.InventoryEquip[rumor.EquipSlot];
            if (equipped == null || equipped.ItemInstanceId != rumor.ItemInstanceId)
                return false;
            item = equipped;
            return true;
        }

        /// <summary>
        /// 던전 소문의 장소 (P2c, D60 개정): 말하는 NPC 마을 중심에서 입구 좌표까지의 8방위(0 북, 1 북동 … 7 북서, 화면 위 = 북).
        /// 입구가 없으면 Nearby(방향 없음), 말하는 NPC 마을이 아니면 OtherVillage, 슬롯·마을이 없으면 Unavailable. bearing은 Bearing일 때만 0~7.
        /// 유효성은 판정하지 않는다 — 호출자가 IsRumorValid를 먼저 본다.
        /// </summary>
        public NpcRumorPlace ResolveRumorDungeonPlace(NpcRumor rumor, int speakerVillageId, out int bearing)
        {
            bearing = -1;
            if (rumor.Kind != NpcRumorKind.DungeonInfo || AR.s.Dungeon == null)
                return NpcRumorPlace.Unavailable;
            if (rumor.VillageId != speakerVillageId)
                return NpcRumorPlace.OtherVillage;
            VillageData? village = AR.s.Village != null ? AR.s.Village.GetVillage(rumor.VillageId) : null;
            int listIndex = AR.s.Dungeon.GetNpcDelveSlotListIndex(rumor.VillageId, rumor.SlotIndex);
            if (village == null || listIndex < 0 ||
                AR.s.Dungeon.TryGetNpcDelveSlot(rumor.VillageId, listIndex, out _, out _, out bool hasEntrance,
                    out UnityEngine.Vector2 entrance) == false)
                return NpcRumorPlace.Unavailable;
            if (hasEntrance == false)
                return NpcRumorPlace.Nearby;

            UnityEngine.Vector2 offset = entrance - village.Position;
            if (offset.sqrMagnitude < RumorBearingMinDistance * RumorBearingMinDistance)
                return NpcRumorPlace.Nearby;
            float angleFromNorth = UnityEngine.Mathf.Atan2(offset.x, offset.y) * UnityEngine.Mathf.Rad2Deg;
            bearing = (UnityEngine.Mathf.RoundToInt(angleFromNorth / 45f) % 8 + 8) % 8;
            return NpcRumorPlace.Bearing;
        }

        /// <summary>
        /// "유망한 소문" (§6.2, D76): 들은 원정 성공 보고, 확인 전, 소속 마을 던전, 가치 ≥ 기준, 사건 뒤 72h 이내, 지금도 유효.
        /// </summary>
        public bool IsPromisingRumor(NpcSaveData holder, NpcRumor rumor, float now)
        {
            return rumor.SourceNpcId >= 0 && rumor.Checked == false && rumor.VillageId == holder.VillageId &&
                   HasPromisingFacts(rumor, now) == true && IsRumorValid(rumor) == true;
        }

        /// <summary>보유 중인 유망한 소문 중 가치가 가장 높은 것(같으면 최신). 없으면 false.</summary>
        public bool TryGetPromisingRumor(NpcSaveData holder, float now, out NpcRumor? best)
        {
            best = null;
            List<NpcRumor> rumors = holder.Rumors;
            for (int i = 0; i < rumors.Count; i++)
            {
                NpcRumor rumor = rumors[i];
                if (IsPromisingRumor(holder, rumor, now) == false)
                    continue;
                if (best == null || rumor.Value > best.Value ||
                    (rumor.Value == best.Value && rumor.EventGameTime > best.EventGameTime))
                    best = rumor;
            }
            return best != null;
        }

        /// <summary>대상이 사라진 소문을 지운다 (D72). 지운 수.</summary>
        public int PruneInvalidRumors(NpcSaveData saveData)
        {
            List<NpcRumor> rumors = saveData.Rumors;
            int removed = 0;
            for (int i = rumors.Count - 1; i >= 0; i--)
            {
                if (IsRumorValid(rumors[i]) == true)
                    continue;
                // 지울 때만 표시한다. 마을 명단 전원을 훑는 호출이라 입구에서 표시하면 명단 전원이 복사된다.
                MarkSaveDirty(saveData.EntityId);
                rumors.RemoveAt(i);
                removed++;
            }
            _rumorCounters.PrunedInvalid += removed;
            if (removed > 0)
                _narrative.InvalidateDialogues(saveData.EntityId);   // 소문 토픽이 바뀜 (P2c)
            return removed;
        }

        /// <summary>마을 명단 전원의 무효 소문을 지운다. 소문은 소속 마을 안에서만 퍼지므로 (D75) 그 명단이면 충분하다.</summary>
        public void PurgeInvalidRumorsInVillage(int villageId)
        {
            VillageData? village = villageId >= 0 ? AR.s.Village.GetVillage(villageId) : null;
            if (village == null)
                return;
            List<int> roster = village.NpcEntityIds;
            for (int i = 0; i < roster.Count; i++)
            {
                if (_npcSaveDict.TryGetValue(roster[i], out NpcSaveData? member) == true && member.Rumors.Count > 0)
                    PruneInvalidRumors(member);
            }
        }

        // ================= 생성 · 사건 정리 =================

        /// <summary>
        /// Delve 성공 + 전리품을 받은 참가자의 직접 소문 (D71). 대상 슬롯이 원정 중 바뀌었으면 만들지 않는다.
        /// </summary>
        public void AddFirsthandDelveRumor(int holderId, NpcSaveData holder, int villageId, int slotIndex, int generation,
            int subjectNpcId, int value, float when)
        {
            MarkSaveDirty(holderId);
            if (villageId < 0 || slotIndex < 0)
                return;
            NpcRumor rumor = new NpcRumor
            {
                Kind = NpcRumorKind.DungeonInfo,
                VillageId = villageId,
                SlotIndex = slotIndex,
                Generation = generation,
                SubjectNpcId = subjectNpcId,
                Value = Math.Max(0, value),
                EventGameTime = when,
            };
            if (IsRumorValid(rumor) == false)
                return;
            AddRumor(holder, rumor, when);
            _rumorCounters.CreatedDelve++;
            RumorCreatedObserver?.Invoke(holderId, rumor);
        }

        /// <summary>
        /// 원정에서 돌아온(살아 있는) 참가자가 그 슬롯에 대해 가진 소문을 "확인함"으로 표시한다 (D76 — 유망에서 빠짐).
        /// 성공해 직접 소문이 생기면 그것이 같은 대상의 더 새 사건으로 교체한다.
        /// </summary>
        public void MarkDelveRumorsChecked(NpcSaveData holder, int villageId, int slotIndex)
        {
            MarkSaveDirty(holder.EntityId);
            if (villageId < 0 || slotIndex < 0)
                return;
            List<NpcRumor> rumors = holder.Rumors;
            for (int i = 0; i < rumors.Count; i++)
            {
                NpcRumor rumor = rumors[i];
                if (rumor.Kind == NpcRumorKind.DungeonInfo && rumor.VillageId == villageId && rumor.SlotIndex == slotIndex)
                    rumor.Checked = true;
            }
        }

        /// <summary>
        /// NPC 던전 완료 뒤처리 (D71·D72): 그 마을 명단의 무효 소문(옛 세대)을 지우고, 성공한 참가자에게
        /// "○○ 일행이 끝냈다 — 그 자리에 새 던전"을 가리키는 DungeonCleared 소문을 준다 (대상 = 완료 뒤 새 세대).
        /// </summary>
        public void OnNpcDungeonCompleted(NpcDelveReport report, int slotIndex, int subjectNpcId,
            IReadOnlyList<int> completionLootValues, float when)
        {
            PurgeInvalidRumorsInVillage(report.VillageId);
            if (AR.s.Dungeon == null ||
                AR.s.Dungeon.TryGetNpcDungeonSlotGeneration(report.VillageId, slotIndex, out int newGeneration) == false)
                return;

            for (int i = 0; i < report.MemberIds.Count; i++)
            {
                if (report.Outcomes[i] != NpcHuntOutcome.Success)
                    continue;
                int memberId = report.MemberIds[i];
                if (_npcSaveDict.TryGetValue(memberId, out NpcSaveData? member) == false)
                    continue;
                // 직접 소문을 만든 참가자도 옛 세대 소문은 위 정리에서 이미 지워졌다.
                PruneInvalidRumors(member);
                NpcRumor rumor = new NpcRumor
                {
                    Kind = NpcRumorKind.DungeonInfo,
                    Cleared = true,
                    VillageId = report.VillageId,
                    SlotIndex = slotIndex,
                    Generation = newGeneration,
                    SubjectNpcId = subjectNpcId,
                    Value = i < completionLootValues.Count ? Math.Max(0, completionLootValues[i]) : 0,
                    EventGameTime = when,
                };
                AddRumor(member, rumor, when);
                _rumorCounters.CreatedCleared++;
                RumorCreatedObserver?.Invoke(memberId, rumor);
            }
        }

        /// <summary>
        /// 업그레이드 장착 뒤처리 (ApplyUpgrade, D71·D72): 교체돼 무효가 된 이 소유자의 ItemOwner 소문을 마을 명단에서 지우고,
        /// 소유자 본인에게 새 장비의 ItemOwner 소문(직접)을 준다.
        /// </summary>
        public void NotifyNpcEquipmentChanged(int entityId, NpcSaveData saveData, GE.EquipSlotType slot, ItemData item,
            float gameTime)
        {
            MarkSaveDirty(entityId);
            if (item == null || item.ItemInstanceId == 0)
                return;
            PurgeInvalidRumorsInVillage(saveData.VillageId);
            if (saveData.Rumors.Count > 0)
                PruneInvalidRumors(saveData);

            NpcRumor rumor = new NpcRumor
            {
                Kind = NpcRumorKind.ItemOwner,
                SubjectNpcId = entityId,
                EquipSlot = (int)slot,
                ItemInstanceId = item.ItemInstanceId,
                Value = GetItemValue(item),
                EventGameTime = gameTime,
            };
            AddRumor(saveData, rumor, gameTime);
            _rumorCounters.CreatedItemOwner++;
            RumorCreatedObserver?.Invoke(entityId, rumor);
        }

        /// <summary>사망 뒤처리 (D72): 본인 소문을 비우고, 이 NPC 때문에 무효가 된 소문(소유자인 ItemOwner, 판매자인 ItemSold,
        /// 가해자 또는 피해자인 Betrayal — D104·D105)을 마을 명단에서 지운다. Condition을 Dead로 바꾼 뒤 호출해야 한다.</summary>
        public void PurgeRumorsOnNpcDeath(int entityId, NpcSaveData saveData)
        {
            MarkSaveDirty(entityId);
            saveData.Rumors.Clear();
            _narrative.InvalidateDialogues(entityId);
            PurgeInvalidRumorsInVillage(saveData.VillageId);
        }

        // ================= 보관 (상한 8, 대상별 1개) =================

        /// <summary>
        /// 소문을 보관한다 (D73). 같은 대상은 1개만 두고 더 새 사건만 교체한다. 가득 차면
        /// 무효 → 유망 아닌 것 중 가장 오래된 것 → 가치 낮은 것 순으로 버린다 (새 소문도 비교 대상).
        /// </summary>
        public NpcRumorAddResult AddRumor(NpcSaveData holder, NpcRumor rumor, float now)
        {
            MarkSaveDirty(holder.EntityId);
            if (rumor == null || rumor.Kind == NpcRumorKind.None)
                return NpcRumorAddResult.Invalid;

            List<NpcRumor> rumors = holder.Rumors;
            for (int i = 0; i < rumors.Count; i++)
            {
                if (IsSameRumorTarget(rumors[i], rumor) == false)
                    continue;
                if (rumors[i].EventGameTime >= rumor.EventGameTime)
                {
                    _rumorCounters.AddDuplicate++;
                    return NpcRumorAddResult.Duplicate;
                }
                rumors[i] = rumor;
                _narrative.InvalidateDialogues(holder.EntityId);   // 소문 토픽이 바뀜 (P2c)
                return NpcRumorAddResult.Replaced;
            }

            if (rumors.Count < MaxRumorsPerNpc)
            {
                rumors.Add(rumor);
                _narrative.InvalidateDialogues(holder.EntityId);
                return NpcRumorAddResult.Added;
            }

            // 가득 참: 새 소문을 포함해 가장 낮은 순위를 버린다. 순위 = (무효 0 < 유망 아님 1 < 유망 2), 같으면 오래된 것, 가치 낮은 것.
            int worstIndex = -1;   // -1 = 새 소문
            int worstRank = GetRumorKeepRank(holder, rumor, now);
            NpcRumor worst = rumor;
            for (int i = 0; i < rumors.Count; i++)
            {
                NpcRumor candidate = rumors[i];
                int rank = GetRumorKeepRank(holder, candidate, now);
                if (IsLowerKeepPriority(rank, candidate, worstRank, worst) == false)
                    continue;
                worstIndex = i;
                worstRank = rank;
                worst = candidate;
            }

            if (worstIndex < 0)
            {
                _rumorCounters.CapDrops++;
                return NpcRumorAddResult.DroppedByCap;
            }
            rumors[worstIndex] = rumor;
            _rumorCounters.CapEvictions++;
            _narrative.InvalidateDialogues(holder.EntityId);
            return NpcRumorAddResult.AddedWithEviction;
        }

        /// <summary>
        /// 같은 대상인가: DungeonInfo = (마을, 슬롯), ItemOwner = (소유자, 장비 슬롯), ItemSold = (판매자, 인스턴스),
        /// Betrayal = (가해자, 피해자, 사건 시각).
        /// </summary>
        public static bool IsSameRumorTarget(NpcRumor a, NpcRumor b)
        {
            if (a.Kind != b.Kind)
                return false;
            return a.Kind switch
            {
                NpcRumorKind.DungeonInfo => a.VillageId == b.VillageId && a.SlotIndex == b.SlotIndex,
                NpcRumorKind.ItemOwner => a.SubjectNpcId == b.SubjectNpcId && a.EquipSlot == b.EquipSlot,
                NpcRumorKind.ItemSold => a.SubjectNpcId == b.SubjectNpcId && a.ItemInstanceId == b.ItemInstanceId,
                NpcRumorKind.Betrayal => a.SubjectNpcId == b.SubjectNpcId && a.VictimNpcId == b.VictimNpcId &&
                                         a.EventGameTime == b.EventGameTime,
                _ => false,
            };
        }

        // ================= Rest 종료 공용 진입점 (D107) =================

        /// <summary>
        /// Rest 정상 종료 처리의 공용 진입점 (D107). 게임(System_NpcSim)·파티 합류 경로(D90 ①)·진단이 모두 이것을 부른다.
        /// 자기 도난 단서 정리(D103) → 소문 교환(D74, 받아들인 소문이 단서와 맞으면 늦은 발각) → 절도 판정(D100, NpcManager_Theft) 1회
        /// → 생활비(GOLD_BALANCE D131·D132, NpcManager_LivingExpense).
        /// allowTheft가 false면(파티 합류 경로) 절도와 생활비는 하지 않는다 (단서 정리·교환은 한다).
        /// 상태 전이(Kind None 등)는 호출자가 이어서 한다 — RNG가 Rest 시작 시각 기준이라 상태를 바꾸기 전에 불러야 한다.
        /// </summary>
        public void ResolveRestEnd(int entityId, NpcSaveData saveData, float restStartGameTime, float gameTime,
            int worldSeed, bool allowTheft)
        {
            MarkSaveDirty(entityId);
            if (saveData.TheftClues.Count > 0)
                PruneTheftClues(saveData, gameTime);
            ExchangeRumorsOnRestEnd(entityId, saveData, restStartGameTime, gameTime, worldSeed);
            if (allowTheft == true)
            {
                ResolveTheftOnRestEnd(entityId, saveData, restStartGameTime, gameTime, worldSeed);
                ResolveLivingExpenseOnRestEnd(entityId, saveData, restStartGameTime, worldSeed);
            }
        }

        // ================= 교환 (Rest 종료, §7.4, D74) =================

        /// <summary>
        /// Rest 종료 시점 1회 (D74): 자기 목록 정리 → 친화력 확률로 교환 여부 → 같은 마을 대기 NPC 1명(친화력이 높으면 가끔 2명)과
        /// 소문을 1개씩 주고받는다. 받을지는 받는 쪽 호기심으로 굴린다. 호감도·관계는 바꾸지 않는다.
        /// 결정론 RNG(Rest 중이던 NPC, Rest 시작 시각, worldSeed, salt 120~125). ResolveRestEnd를 거쳐서만 부른다.
        /// </summary>
        private void ExchangeRumorsOnRestEnd(int entityId, NpcSaveData saveData, float restStartGameTime, float gameTime,
            int worldSeed)
        {
            _rumorCounters.RestEnds++;
            if (saveData.Rumors.Count > 0)
                PruneInvalidRumors(saveData);
            if (saveData.VillageId < 0)
                return;
            VillageData? village = AR.s.Village.GetVillage(saveData.VillageId);
            if (village == null)
                return;

            int friendliness = saveData.NpcStat.Friendliness;
            HistoryRng exchangeRng = NpcActivityResolver.CreateRng(entityId, restStartGameTime, worldSeed,
                NpcActivityResolver.RumorExchangeSalt);
            double exchangeRoll = exchangeRng.NextDouble();
            double secondPeerRoll = exchangeRng.NextDouble();
            if (exchangeRoll >= NpcActivityResolver.GetRumorExchangeChance(friendliness))
                return;
            _rumorCounters.ExchangeRollsPassed++;

            _rumorPeerScratch.Clear();
            List<int> roster = village.NpcEntityIds;
            for (int i = 0; i < roster.Count; i++)
            {
                if (IsEligibleRumorPeer(entityId, saveData, roster[i]) == true)
                    _rumorPeerScratch.Add(roster[i]);
            }

            int candidateCount = _rumorPeerScratch.Count;
            if (candidateCount == 0)
            {
                _rumorCounters.NoPeer++;
                return;
            }

            int peerCount = secondPeerRoll < NpcActivityResolver.GetRumorSecondPeerChance(friendliness) ? 2 : 1;
            if (peerCount > candidateCount)
                peerCount = candidateCount;

            HistoryRng pickRng = NpcActivityResolver.CreateRng(entityId, restStartGameTime, worldSeed,
                NpcActivityResolver.RumorPeerPickSalt);
            for (int i = 0; i < peerCount; i++)
            {
                // 부분 셔플: i번째 자리에 남은 후보 중 하나를 결정론으로 뽑아 둔다.
                int pick = pickRng.Range(i, candidateCount);
                int peerId = _rumorPeerScratch[pick];
                _rumorPeerScratch[pick] = _rumorPeerScratch[i];
                _rumorPeerScratch[i] = peerId;
                ExchangeWithPeer(entityId, saveData, peerId, i, restStartGameTime, gameTime, worldSeed);
            }
            _rumorPeerScratch.Clear();
        }

        /// <summary>
        /// 줄 소문 1개: 유효하고 받는 쪽이 같은 대상의 같거나 더 새 사건을 갖지 않은 것 중 (유망한 사실 > 가치 > 최신) 순.
        /// 고른 소문은 노출로 센다. 없으면 null.
        /// </summary>
        public NpcRumor? PickRumorToGive(NpcSaveData giver, NpcSaveData receiver, float now)
        {
            NpcRumor? best = null;
            bool bestPromising = false;
            List<NpcRumor> rumors = giver.Rumors;
            for (int i = 0; i < rumors.Count; i++)
            {
                NpcRumor rumor = rumors[i];
                if (IsRumorValid(rumor) == false)
                {
                    _rumorCounters.FilteredInvalidAtUse++;
                    continue;
                }
                if (HasSameOrNewerRumor(receiver, rumor) == true)
                {
                    _rumorCounters.DuplicateSkips++;
                    continue;
                }

                bool promising = HasPromisingFacts(rumor, now);
                if (best == null || (promising == true && bestPromising == false) ||
                    (promising == bestPromising && (rumor.Value > best.Value ||
                        (rumor.Value == best.Value && rumor.EventGameTime > best.EventGameTime))))
                {
                    best = rumor;
                    bestPromising = promising;
                }
            }

            if (best != null)
                RecordRumorExposure(best);
            return best;
        }

        /// <summary>
        /// 건네받은 소문을 복사해 보관한다 (출처 = 준 NPC, Hops + 1, 확인 표시 해제). 받아들였으면 true.
        /// 받아들인 소문이 ItemOwner·ItemSold이고 받는 쪽의 도난 단서와 맞으면(같은 인스턴스, 주체 = 실제 범인) 그 자리에서 늦은 발각이다
        /// (D103 — 소문 교환 도중 관계가 바뀌는 D74의 예외. RNG 없음).
        /// </summary>
        public bool ReceiveRumor(NpcSaveData receiver, NpcRumor source, int giverId, float now)
        {
            MarkSaveDirty(receiver.EntityId);
            NpcRumor copy = source.Clone();
            copy.SourceNpcId = giverId;
            copy.Hops = source.Hops + 1;
            copy.Checked = false;
            NpcRumorAddResult result = AddRumor(receiver, copy, now);
            if (result != NpcRumorAddResult.Added && result != NpcRumorAddResult.Replaced &&
                result != NpcRumorAddResult.AddedWithEviction)
                return false;

            _rumorCounters.Accepted++;
            int hopIndex = copy.Hops >= 4 ? 3 : Math.Max(0, copy.Hops - 1);
            _rumorCounters.ReceivedByHops[hopIndex]++;
            if (copy.Kind == NpcRumorKind.ItemSold)
                _rumorCounters.AcceptedItemSold++;
            else if (copy.Kind == NpcRumorKind.Betrayal)
                _rumorCounters.AcceptedBetrayal++;

            if ((copy.Kind == NpcRumorKind.ItemOwner || copy.Kind == NpcRumorKind.ItemSold) && receiver.TheftClues.Count > 0)
                TryDiscoverTheftFromRumor(receiver, copy, now);
            return true;
        }

        /// <summary>노출·사용 1건을 센다. 진단 감사가 붙어 있으면 다른 경로로 유효성을 다시 본다 (항상 true여야 한다).</summary>
        public void RecordRumorExposure(NpcRumor rumor)
        {
            _rumorCounters.Exposed++;
            if (RumorExposureAudit != null && RumorExposureAudit(rumor) == false)
                _rumorCounters.ExposedInvalid++;
        }

        /// <summary>
        /// 확인 원정 대상 (D76): 보유자의 유망한 소문이 가리키는 슬롯. 없으면 -1.
        /// 고르면 노출·확인 원정으로 센다 (TryPrepareDelveTarget이 부른다).
        /// </summary>
        private int PickConfirmationDelveSlot(int holderId, int villageId, float now)
        {
            if (holderId < 0 || _npcSaveDict.TryGetValue(holderId, out NpcSaveData? holder) == false ||
                holder.VillageId != villageId || TryGetPromisingRumor(holder, now, out NpcRumor? rumor) == false ||
                rumor == null)
                return -1;

            RecordRumorExposure(rumor);
            _rumorCounters.ConfirmationDelves++;
            return rumor.SlotIndex;
        }

        private void ExchangeWithPeer(int entityId, NpcSaveData saveData, int peerId, int peerIndex, float restStartGameTime,
            float gameTime, int worldSeed)
        {
            if (_npcSaveDict.TryGetValue(peerId, out NpcSaveData? peer) == false)
                return;

            _rumorCounters.ExchangeAttempts++;
            // 양쪽이 줄 것을 먼저 고른다 — 방금 받은 소문을 되돌려 주지 않게.
            NpcRumor? fromPeer = PickRumorToGive(peer, saveData, gameTime);
            NpcRumor? fromSelf = PickRumorToGive(saveData, peer, gameTime);
            if (fromPeer == null && fromSelf == null)
            {
                _rumorCounters.NothingToOffer++;
                return;
            }

            bool transferred = false;
            if (fromPeer != null)
            {
                _rumorCounters.Offers++;
                HistoryRng rng = NpcActivityResolver.CreateRng(entityId, restStartGameTime, worldSeed,
                    NpcActivityResolver.RumorAcceptBySelfSaltBase + peerIndex);
                if (rng.NextDouble() < NpcActivityResolver.GetRumorAcceptChance(saveData.NpcStat.Curiosity))
                    transferred |= ReceiveRumor(saveData, fromPeer, peerId, gameTime);
                else
                    _rumorCounters.RejectedByCuriosity++;
            }
            if (fromSelf != null)
            {
                _rumorCounters.Offers++;
                HistoryRng rng = NpcActivityResolver.CreateRng(entityId, restStartGameTime, worldSeed,
                    NpcActivityResolver.RumorAcceptByPeerSaltBase + peerIndex);
                if (rng.NextDouble() < NpcActivityResolver.GetRumorAcceptChance(peer.NpcStat.Curiosity))
                    transferred |= ReceiveRumor(peer, fromSelf, entityId, gameTime);
                else
                    _rumorCounters.RejectedByCuriosity++;
            }
            if (transferred == true)
                _rumorCounters.ExchangesWithTransfer++;
        }

        /// <summary>
        /// 교환 상대 조건 (D74): 같은 마을, 생존, 대기(Phase=Idle — 휴식·거래·활동 전), 파티 아님, 던전 동행·플레이어 결투 아님,
        /// Rival·Grudge 아님. 실체화된 NPC도 포함한다(교환은 화면에 보이는 연출이 없다).
        /// 파티 모집 후보(GetPartyCandidateRejectReason)는 거래 중 제외·20타일 범위·수락 확률이 붙으므로 따로 둔다 (D85).
        /// </summary>
        private bool IsEligibleRumorPeer(int selfId, NpcSaveData self, int candidateId)
        {
            if (candidateId == selfId || _npcSaveDict.TryGetValue(candidateId, out NpcSaveData? candidate) == false)
                return false;
            if (candidate.Condition == CharacterConditions.Dead || candidate.VillageId != self.VillageId)
                return false;
            NpcActivityState activity = candidate.Activity;
            if (activity.Phase != NpcActivityPhase.Idle || activity.PartyId >= 0)
                return false;
            if (activity.Kind != NpcActivityKind.None && activity.Kind != NpcActivityKind.Rest &&
                activity.Kind != NpcActivityKind.Trade)
                return false;
            if (IsCompanionInDungeon(candidateId) == true || IsInPlayerDuel(candidateId) == true)
                return false;
            return IsHostilePartyRelation(selfId, candidateId) == false;
        }

        /// <summary>받는 쪽이 같은 대상의 같거나 더 새 사건을 이미 가졌는가 (같은 사건 재수신 차단 — 확인 표시가 되살아나지 않음).</summary>
        private static bool HasSameOrNewerRumor(NpcSaveData receiver, NpcRumor rumor)
        {
            List<NpcRumor> rumors = receiver.Rumors;
            for (int i = 0; i < rumors.Count; i++)
            {
                if (IsSameRumorTarget(rumors[i], rumor) == true && rumors[i].EventGameTime >= rumor.EventGameTime)
                    return true;
            }
            return false;
        }

        /// <summary>보유자와 무관한 "유망한 사실": 원정 성공 보고, 가치 ≥ 기준, 사건 뒤 72h 이내.</summary>
        private static bool HasPromisingFacts(NpcRumor rumor, float now)
        {
            if (rumor.Kind != NpcRumorKind.DungeonInfo || rumor.Cleared == true || rumor.Value < PromisingRumorValueMin)
                return false;
            float age = now - rumor.EventGameTime;
            return age >= -0.01f && age <= PromisingRumorFreshHours;
        }

        private int GetRumorKeepRank(NpcSaveData holder, NpcRumor rumor, float now)
        {
            if (IsRumorValid(rumor) == false)
                return 0;
            return IsPromisingRumor(holder, rumor, now) == true ? 2 : 1;
        }

        /// <summary>candidate가 current보다 먼저 버려야 하는가: 순위 낮음 → 오래됨 → 가치 낮음.</summary>
        private static bool IsLowerKeepPriority(int candidateRank, NpcRumor candidate, int currentRank, NpcRumor current)
        {
            if (candidateRank != currentRank)
                return candidateRank < currentRank;
            if (candidate.EventGameTime != current.EventGameTime)
                return candidate.EventGameTime < current.EventGameTime;
            return candidate.Value < current.Value;
        }

        private void ResetRumorRuntime()
        {
            _rumorCounters = new NpcRumorCounters();
            _rumorPeerScratch.Clear();
            RumorExposureAudit = null;
            RumorCreatedObserver = null;
        }
    }
}
