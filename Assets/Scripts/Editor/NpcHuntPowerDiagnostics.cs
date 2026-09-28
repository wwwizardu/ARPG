#nullable enable
using System.Collections.Generic;
using System.Text;
using ARPG.Creature;
using ARPG.Data;
using ARPG.History;
using ARPG.Npc;
using ARPG.Tables;
using UnityEditor;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.EditorTools
{
    /// <summary>
    /// Hunt 전투력 교체(용기 → 장비, NPC_AMBITION_DESIGN D31·D32)의 재정규화 진단.
    /// 교체 전 식과 새 식의 기대 성공률·부상률·사망률·Food를 해석적으로(판정 곡선의 기대값) 비교한다.
    /// - World: 현재 로드된 세이브의 생존 NPC (실제 장비·성향)
    /// - Fresh: 생성 규칙 그대로 새로 뽑은 NPC 집단 (NpcEquipTable 슬롯 확률 + ItemManager 장비 롤, 결정론 RNG)
    /// 실제 세이브는 바꾸지 않는다. 플레이 모드(GameScene 로드 후)에서만 동작한다.
    /// P1c 튜닝 도구(D43)가 이 파일을 확장해 N게임일 시뮬레이션·CSV 출력을 붙일 수 있게 계산 단위를 분리해 둔다.
    /// </summary>
    public static class NpcHuntPowerDiagnostics
    {
        private const int FreshSampleCount = 2000;
        private const int PartySampleCount = 2000;
        private const int FreshSampleSeed = 20260927;

        private struct HuntExpectation
        {
            public double Capability;
            public double Success;
            public double Wounded;
            public double Dead;
            public double FoodPerSuccess;
        }

        private struct PartyExpectation
        {
            public double MemberSuccess;
            public double MemberWounded;
            public double MemberDead;
            public double FoodPerSuccessfulParty;
        }

        private struct PowerSample
        {
            public int Courage;
            public int SkillLevel;
            public NpcPowerBreakdown Power;
        }

        [MenuItem("ARPG/Npc/Diagnostics/Hunt Power Normalization", false, 430)]
        public static void Run()
        {
            if (EditorApplication.isPlaying == false || AR.s == null || AR.s.Npc == null || AR.s.Item == null)
            {
                Debug.LogError("[NpcHuntPowerDiagnostics] 플레이 모드에서 GameScene이 로드된 뒤 실행하세요.");
                return;
            }

            List<PowerSample> world = CollectWorldSamples(out List<NpcSaveData> worldSaves);
            List<PowerSample> fresh = GenerateFreshSamples(worldSaves);

            StringBuilder report = new StringBuilder();
            report.AppendLine("[NpcHuntPowerDiagnostics] Hunt 전투력 재정규화 (교체 전 = 용기+SkillLevel, 새 식 = 장비+SkillLevel)");
            report.AppendLine($"기준 전투력 HuntReferencePower={NpcActivityResolver.HuntReferencePower}, " +
                $"capability@ref={NpcActivityResolver.HuntCapabilityAtReference}, " +
                $"slope={NpcActivityResolver.HuntCapabilityPerPowerRatio}");
            AppendPopulation(report, "World(현재 세이브)", world);
            AppendPopulation(report, $"Fresh(생성 규칙, N={fresh.Count})", fresh);
            AppendAmbitionState(report, worldSaves);
            Debug.Log(report.ToString());
        }

        private static List<PowerSample> CollectWorldSamples(out List<NpcSaveData> saves)
        {
            saves = new List<NpcSaveData>();
            List<PowerSample> result = new List<PowerSample>();
            List<NpcSaveData> all = new List<NpcSaveData>(AR.s.Npc.SaveDataView.Values);
            for (int i = 0; i < all.Count; i++)
            {
                NpcSaveData saveData = all[i];
                if (saveData.Condition == CharacterConditions.Dead || saveData.Activity.Phase == NpcActivityPhase.Dead)
                    continue;
                saves.Add(saveData);
                result.Add(new PowerSample
                {
                    Courage = saveData.NpcStat.Courage,
                    SkillLevel = saveData.SkillLevel,
                    Power = NpcPowerEvaluator.EvaluateBreakdown(saveData.InventoryEquip, saveData.SkillLevel),
                });
            }
            return result;
        }

        /// <summary>
        /// NpcEquipHelper.SeedInitialEquipment와 같은 규칙(슬롯 확률 → CreateInventoryItemData → 슬롯 장착)을
        /// 결정론 RNG로 반복해 새 NPC 집단을 만든다. NpcTableId 비율은 현재 세계의 비율을 따른다.
        /// </summary>
        private static List<PowerSample> GenerateFreshSamples(List<NpcSaveData> worldSaves)
        {
            List<int> tableIds = new List<int>();
            for (int i = 0; i < worldSaves.Count; i++)
                tableIds.Add(worldSaves[i].NpcTableId);
            if (tableIds.Count == 0)
                return new List<PowerSample>();

            HistoryRng rng = new HistoryRng(FreshSampleSeed);
            List<PowerSample> result = new List<PowerSample>(FreshSampleCount);
            int slotMax = (int)GE.EquipSlotType.Max;
            for (int n = 0; n < FreshSampleCount; n++)
            {
                int tableId = tableIds[rng.Range(0, tableIds.Count)];
                ItemData?[] equip = new ItemData?[slotMax];
                NpcTable? npcTable = AR.s.Data.GetNpc(tableId);
                NpcEquipTable? equipTable = npcTable != null && npcTable.EquipTableId > 0
                    ? AR.s.Data.GetNpcEquipTable(npcTable.EquipTableId)
                    : null;
                if (equipTable != null)
                {
                    for (int s = 0; s < slotMax; s++)
                    {
                        GE.EquipSlotType slot = (GE.EquipSlotType)s;
                        int itemId = equipTable.GetItemId(slot);
                        int chance = equipTable.GetEquipChance(slot);
                        if (itemId <= 0 || chance <= 0)
                            continue;
                        if (chance < 100 && rng.Range(0, 100) >= chance)
                            continue;
                        equip[s] = AR.s.Item.CreateInventoryItemData(itemId, 1, rng);
                    }
                }

                result.Add(new PowerSample
                {
                    Courage = rng.Range(0, 101),
                    SkillLevel = 0,
                    Power = NpcPowerEvaluator.EvaluateBreakdown(equip, 0),
                });
            }
            return result;
        }

        private static void AppendPopulation(StringBuilder report, string label, List<PowerSample> samples)
        {
            if (samples.Count == 0)
            {
                report.AppendLine($"== {label}: 표본 없음");
                return;
            }

            List<float> powers = new List<float>(samples.Count);
            double attack = 0d, defense = 0d, life = 0d, equipment = 0d, skill = 0d;
            HuntExpectation legacy = new HuntExpectation();
            HuntExpectation current = new HuntExpectation();
            List<float> legacyCaps = new List<float>(samples.Count);
            List<float> newCaps = new List<float>(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                PowerSample sample = samples[i];
                powers.Add(sample.Power.Total);
                attack += sample.Power.Attack;
                defense += sample.Power.Defense;
                life += sample.Power.Life;
                equipment += sample.Power.EquipmentScore;
                skill += sample.Power.SkillScore;

                float legacyCap = GetLegacyCapability(sample.Courage, sample.SkillLevel);
                float newCap = NpcActivityResolver.GetHuntCapability(sample.Power.Total);
                legacyCaps.Add(legacyCap);
                newCaps.Add(newCap);
                Accumulate(ref legacy, legacyCap, GetLegacyFoodBase(sample.Courage, sample.SkillLevel), true);
                Accumulate(ref current, newCap, GetFoodBase(newCap), false);
            }

            int count = samples.Count;
            powers.Sort();
            report.AppendLine($"== {label}: NPC {count}");
            report.AppendLine($"  NpcPower mean={Average(powers):F2} p10={Percentile(powers, 0.10f):F2} " +
                $"p50={Percentile(powers, 0.50f):F2} p90={Percentile(powers, 0.90f):F2} p98={Percentile(powers, 0.98f):F2} " +
                $"min={powers[0]:F2} max={powers[count - 1]:F2}");
            report.AppendLine($"  구성 평균: Attack={attack / count:F2}(×{NpcPowerEvaluator.AttackWeight}) " +
                $"Defense={defense / count:F2}(×{NpcPowerEvaluator.DefenseWeight}) " +
                $"Life={life / count:F2}(×{NpcPowerEvaluator.LifeWeight}) Equip={equipment / count:F2} Skill={skill / count:F2}");
            report.AppendLine("  솔로 Hunt   | capability | 성공    | 부상    | 사망    | 실패    | Food/성공 | Food/Hunt");
            AppendExpectationRow(report, "교체 전", legacy, count);
            AppendExpectationRow(report, "새 식  ", current, count);

            PartyExpectation legacyParty = EstimateParty(legacyCaps, true);
            PartyExpectation newParty = EstimateParty(newCaps, false);
            report.AppendLine("  파티 Hunt(2~4인 무작위) | 파티원 성공 | 부상 | 사망 | Food/성공 파티");
            report.AppendLine($"    교체 전 | {legacyParty.MemberSuccess:P1} | {legacyParty.MemberWounded:P1} | " +
                $"{legacyParty.MemberDead:P2} | {legacyParty.FoodPerSuccessfulParty:F2}");
            report.AppendLine($"    새 식   | {newParty.MemberSuccess:P1} | {newParty.MemberWounded:P1} | " +
                $"{newParty.MemberDead:P2} | {newParty.FoodPerSuccessfulParty:F2}");
        }

        private static void AppendExpectationRow(StringBuilder report, string label, HuntExpectation sum, int count)
        {
            double success = sum.Success / count;
            double wounded = sum.Wounded / count;
            double dead = sum.Dead / count;
            double foodPerSuccess = sum.Success > 0d ? sum.FoodPerSuccess / sum.Success : 0d;
            report.AppendLine($"    {label} | {sum.Capability / count:F3}      | {success:P1} | {wounded:P1} | " +
                $"{dead:P2} | {1d - success - wounded - dead:P1} | {foodPerSuccess:F3}     | {success * foodPerSuccess:F3}");
        }

        private static void AppendAmbitionState(StringBuilder report, List<NpcSaveData> saves)
        {
            int initialized = 0;
            int strong = 0;
            long goldSum = 0;
            int goldMin = int.MaxValue;
            int goldMax = int.MinValue;
            int overCap = 0;
            int inventoryItems = 0;
            for (int i = 0; i < saves.Count; i++)
            {
                NpcSaveData saveData = saves[i];
                if (saveData.InitialPower > 0f)
                    initialized++;
                if (saveData.PowerCapBonus > 0.20001f)
                    strong++;
                goldSum += saveData.Gold;
                goldMin = Mathf.Min(goldMin, saveData.Gold);
                goldMax = Mathf.Max(goldMax, saveData.Gold);
                float cap = NpcManager.GetNpcPowerCap(saveData);
                if (cap > 0f && NpcPowerEvaluator.Evaluate(saveData) > cap + 0.0001f)
                    overCap++;
                for (int k = 0; k < saveData.Inventory.Count; k++)
                {
                    if (saveData.Inventory[k] != null)
                        inventoryItems++;
                }
            }

            int count = Mathf.Max(1, saves.Count);
            report.AppendLine($"== 개인 상한·Gold 상태 (World 생존 {saves.Count})");
            report.AppendLine($"  InitialPower 초기화 {initialized}/{saves.Count}, 강자(편차>+20%) {strong} ({(float)strong / count:P1}), " +
                $"개인 상한 초과 {overCap}");
            report.AppendLine($"  Gold 평균 {(float)goldSum / count:F1} (min {(saves.Count > 0 ? goldMin : 0)}, " +
                $"max {(saves.Count > 0 ? goldMax : 0)}), 개인 인벤토리 아이템 {inventoryItems}");
        }

        /// <param name="legacy">true = 교체 전 판정 곡선(사망 0.08→0.01), false = 현재 NpcActivityResolver 곡선</param>
        private static void Accumulate(ref HuntExpectation sum, float capability, int foodBase, bool legacy)
        {
            float death;
            float wounded;
            float successChance;
            if (legacy == true)
            {
                death = Mathf.Lerp(0.08f, 0.01f, capability);
                wounded = Mathf.Lerp(0.32f, 0.08f, capability);
                successChance = Mathf.Lerp(0.40f, 0.86f, capability);
            }
            else
            {
                NpcActivityResolver.GetSoloHuntChances(capability, out death, out wounded, out successChance);
            }
            double success = successChance;
            sum.Capability += capability;
            sum.Success += success;
            sum.Wounded += wounded;
            sum.Dead += death;
            // Food/성공 = 기본량 + 20% 확률 +1. 성공 확률로 가중해 합산한다.
            sum.FoodPerSuccess += success * (foodBase + 0.20d);
        }

        /// <summary>파티 판정은 capability 합과 파티원 개인 capability로만 정해진다. 무작위 2~4인 조합의 기대값.</summary>
        private static PartyExpectation EstimateParty(List<float> capabilities, bool legacy)
        {
            PartyExpectation result = new PartyExpectation();
            if (capabilities.Count < 2)
                return result;

            HistoryRng rng = new HistoryRng(FreshSampleSeed + 1);
            int memberTotal = 0;
            double partySuccessWeight = 0d;
            float[] members = new float[4];
            for (int n = 0; n < PartySampleCount; n++)
            {
                int size = rng.Range(2, 5);
                float partyPower = 0f;
                for (int m = 0; m < size; m++)
                {
                    members[m] = capabilities[rng.Range(0, capabilities.Count)];
                    partyPower += members[m];
                }

                double allFail = 1d;
                for (int m = 0; m < size; m++)
                {
                    float support = Mathf.Clamp01((partyPower - members[m]) / (size - 1));
                    float capability = Mathf.Clamp01(members[m] * 0.70f + support * 0.30f);
                    float death;
                    float wounded;
                    float successChance;
                    if (legacy == true)
                    {
                        death = Mathf.Lerp(0.045f, 0.004f, capability);
                        wounded = Mathf.Lerp(0.24f, 0.035f, capability);
                        successChance = Mathf.Lerp(0.48f, 0.92f, capability);
                    }
                    else
                    {
                        NpcActivityResolver.GetPartyMemberHuntChances(capability, out death, out wounded,
                            out successChance);
                    }
                    double success = successChance;
                    result.MemberSuccess += success;
                    result.MemberWounded += wounded;
                    result.MemberDead += death;
                    allFail *= 1d - success;
                    memberTotal++;
                }

                double partySuccess = 1d - allFail;
                partySuccessWeight += partySuccess;
                int foodBase = Mathf.Clamp(Mathf.RoundToInt(partyPower * 2f), 2, 6);
                result.FoodPerSuccessfulParty += partySuccess * (foodBase + 0.25d);
            }

            result.MemberSuccess /= memberTotal;
            result.MemberWounded /= memberTotal;
            result.MemberDead /= memberTotal;
            result.FoodPerSuccessfulParty = partySuccessWeight > 0d ? result.FoodPerSuccessfulParty / partySuccessWeight : 0d;
            return result;
        }

        // ---- 교체 전 식 (2026-09-27 이전 NpcActivityResolver). 비교용으로만 남긴다. ----
        private static float GetLegacyCapability(int courage, int skillLevel)
        {
            float c = Mathf.Clamp01(courage / 100f);
            float s = Mathf.Clamp01(skillLevel / 10f);
            return Mathf.Clamp01(0.20f + c * 0.55f + s * 0.25f);
        }

        private static int GetLegacyFoodBase(int courage, int skillLevel)
        {
            float capability = Mathf.Clamp01(courage / 100f + skillLevel / 20f);
            return capability >= 0.70f ? 3 : capability >= 0.35f ? 2 : 1;
        }

        private static int GetFoodBase(float capability)
        {
            return capability >= NpcActivityResolver.HuntFoodHighCapability ? 3
                : capability >= NpcActivityResolver.HuntFoodMidCapability ? 2 : 1;
        }

        private static float Average(List<float> values)
        {
            double sum = 0d;
            for (int i = 0; i < values.Count; i++)
                sum += values[i];
            return values.Count > 0 ? (float)(sum / values.Count) : 0f;
        }

        private static float Percentile(List<float> sorted, float p)
        {
            if (sorted.Count == 0)
                return 0f;
            int index = Mathf.Clamp(Mathf.FloorToInt(p * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[index];
        }
    }
}
