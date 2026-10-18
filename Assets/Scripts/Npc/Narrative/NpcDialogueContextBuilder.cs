#nullable enable
using System.Collections.Generic;
using System.Text;
using ARPG.History;
using ARPG.Village;

namespace ARPG.Npc.Narrative
{
    /// <summary>
    /// 백본(NpcSaveData / 관계 / 연대기) + 시드 위협 → 대화 생성 문맥.
    /// NpcRelationBackfill.ExtractContext의 연대기 순회를 미러한다.
    /// 설계: NARRATIVE_ENGINE §4.2~4.3 / NARRATIVE_ENGINE_SPIKE §4.1
    /// </summary>
    public static class NpcDialogueContextBuilder
    {
        private const int MaxHistoryEvents = 5;

        public static NpcDialogueContext? Build(int npcId, ThreatSeed threat)
        {
            NpcSaveData? save = AR.s.Npc?.GetSaveData(npcId);
            if (save == null)
                return null;

            NpcDialogueContext ctx = new() { NpcId = npcId };

            // ── NPC 블록 ──
            // 인스턴스 고유 이름 우선 (표기 정책 단일 진입점). 대사 프롬프트에 공유 테이블 이름 대신 개인 이름이 들어간다.
            ctx.NpcName = AR.s.Npc != null ? AR.s.Npc.GetDisplayName(npcId) : $"NPC#{npcId}";

            StringBuilder npc = new();
            npc.AppendLine($"이름: {ctx.NpcName}");
            NpcGender npcGender = AR.s.Npc != null ? AR.s.Npc.GetGender(npcId) : NpcGender.Unknown;
            string gender = npcGender switch
            {
                NpcGender.Male => "남성",
                NpcGender.Female => "여성",
                _ => "미상",
            };
            npc.AppendLine($"성별: {gender}");
            npc.AppendLine($"직업: {save.JobType}");
            if (string.IsNullOrEmpty(save.Description) == false)
                npc.AppendLine($"소개: {save.Description}");
            npc.AppendLine(
                $"성격(0~100): 친화 {save.NpcStat.Friendliness}, 정직 {save.NpcStat.Honesty}, 탐욕 {save.NpcStat.Greed}, " +
                $"충성 {save.NpcStat.Loyalty}, 용기 {save.NpcStat.Courage}, 호기심 {save.NpcStat.Curiosity}, " +
                $"자존심 {save.NpcStat.Pride}, 인내 {save.NpcStat.Patience}");
            ctx.NpcBlock = npc.ToString().TrimEnd();

            // ── 연대기 문맥 (settlementId 기준) ──
            int settlementId = AR.s.Village?.GetVillage(save.VillageId)?.SettlementId ?? -1;
            ChronicleSaveData? chronicle = AR.s.Data?.Chronicle;

            StringBuilder hist = new();

            if (chronicle != null && settlementId >= 0)
            {
                SettlementSnapshot? self = FindSettlement(chronicle, settlementId);
                if (self != null)
                {
                    string ruled = self.Fate == SettlementFate.Conquered && self.KingdomId >= 0
                        ? $", 정복국 왕국#{self.KingdomId}"
                        : string.Empty;
                    hist.AppendLine($"현재 정착지: {self.Race} 종족, 상태 {self.Fate}{ruled}");
                }

                int added = 0;
                for (int i = 0; i < chronicle.Events.Count && added < MaxHistoryEvents; i++)
                {
                    ChronicleEvent e = chronicle.Events[i];
                    if (e.SubjectSettlementId == settlementId || e.ObjectSettlementId == settlementId)
                    {
                        hist.AppendLine(FlattenEvent(e));
                        if (ctx.ContextEventIds.Contains(e.EventId) == false)
                            ctx.ContextEventIds.Add(e.EventId);
                        added++;
                    }
                }
            }
            else if (settlementId < 0
                && AR.s.Village != null
                && AR.s.Village.TryGetVillageProfile(save.VillageId, out VillageProfile homeProfile))
            {
                // 손제작 마을(연대기 미연결): 정착지 스냅샷이 없으므로 마을 Profile 종족으로 홈 정체성만 접지한다.
                hist.AppendLine($"현재 정착지: {homeProfile.Race} 종족 마을");
            }

            // ── 감정 블록 (+ 감정 원인 사건을 연대기에 보강) ──
            StringBuilder sent = new();
            for (int i = 0; i < save.Sentiments.Count; i++)
            {
                NpcSentiment s = save.Sentiments[i];
                string target = s.TargetSettlementId >= 0 ? $" (대상 정착지#{s.TargetSettlementId})" : string.Empty;
                sent.AppendLine($"- {s.Type}{target}");
                if (s.OriginEventId >= 0)
                {
                    if (ctx.ContextEventIds.Contains(s.OriginEventId) == false)
                        ctx.ContextEventIds.Add(s.OriginEventId);
                    ChronicleEvent? oe = FindEvent(chronicle, s.OriginEventId);
                    if (oe != null)
                        hist.AppendLine(FlattenEvent(oe));
                }
            }
            ctx.SentimentBlock = sent.ToString().TrimEnd();
            ctx.HistoryBlock = hist.ToString().TrimEnd();

            // ── 관계 블록 ──
            BuildRelations(ctx, npcId);

            // ── 상황(위협) 시드 — D2 ──
            if (threat.NearbyMonsters)
            {
                ctx.SituationBlock = string.IsNullOrEmpty(threat.Note) == false
                    ? threat.Note
                    : "마을 근처에 몬스터가 출몰해 마을 밖으로 나가기가 위험하다.";
            }

            return ctx;
        }

        private static void BuildRelations(NpcDialogueContext ctx, int npcId)
        {
            IReadOnlyList<NpcRelationEntry>? relations = AR.s.Npc?.GetRelations(npcId);
            if (relations == null || relations.Count == 0)
                return;

            StringBuilder rel = new();
            for (int i = 0; i < relations.Count; i++)
            {
                NpcRelationEntry r = relations[i];
                bool isFrom = r.FromEntityId == npcId;
                int otherId = isFrom ? r.ToEntityId : r.FromEntityId;
                string otherName = ResolveNpcName(otherId);
                string label = RelationLabel(r.Type, isFrom);
                rel.AppendLine($"- {label}: {otherName}");

                if (r.OriginEventId >= 0 && ctx.ContextEventIds.Contains(r.OriginEventId) == false)
                    ctx.ContextEventIds.Add(r.OriginEventId);
            }
            ctx.RelationsBlock = rel.ToString().TrimEnd();
        }

        private static string ResolveNpcName(int otherId)
        {
            return AR.s.Npc != null ? AR.s.Npc.GetDisplayName(otherId) : $"NPC#{otherId}";
        }

        /// <summary>상대를 화자 기준으로 부르는 라벨. isFrom = 화자가 관계의 From인가. (DialogueTopicSelector 사별 토픽도 공유)</summary>
        public static string RelationLabel(NpcRelationType type, bool isFrom) => type switch
        {
            NpcRelationType.Spouse => "배우자",
            NpcRelationType.Sibling => "형제",
            NpcRelationType.Friend => "친구",
            NpcRelationType.Rival => "라이벌",
            NpcRelationType.ParentOf => isFrom ? "자녀" : "부모",
            NpcRelationType.MentorOf => isFrom ? "제자" : "스승",
            NpcRelationType.GrudgeAgainst => isFrom ? "원한 상대" : "원한을 품은 자",
            _ => "관계",
        };

        private static string FlattenEvent(ChronicleEvent e)
        {
            string obj = e.ObjectSettlementId >= 0 ? $" → 정착지#{e.ObjectSettlementId}" : string.Empty;
            string kingdom = e.KingdomId >= 0 ? $" (왕국#{e.KingdomId})" : string.Empty;
            return $"{e.Year}년, {e.Type} — 정착지#{e.SubjectSettlementId}{obj}{kingdom} [event {e.EventId}]";
        }

        private static SettlementSnapshot? FindSettlement(ChronicleSaveData chronicle, int settlementId)
        {
            for (int i = 0; i < chronicle.Settlements.Count; i++)
                if (chronicle.Settlements[i].SettlementId == settlementId)
                    return chronicle.Settlements[i];
            return null;
        }

        private static ChronicleEvent? FindEvent(ChronicleSaveData? chronicle, int eventId)
        {
            if (chronicle == null)
                return null;
            for (int i = 0; i < chronicle.Events.Count; i++)
                if (chronicle.Events[i].EventId == eventId)
                    return chronicle.Events[i];
            return null;
        }
    }
}
