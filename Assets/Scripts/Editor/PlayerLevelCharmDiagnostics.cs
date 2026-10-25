#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ARPG.Component;
using ARPG.Data;
using ARPG.Item;
using ARPG.Manager;
using ARPG.Skill.Combat;
using ARPG.Systems;
using ARPG.Tables;
using ARPG.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using GE = GlobalEnum;
using Random = UnityEngine.Random;

namespace ARPG.EditorTools
{
    /// <summary>
    /// 플레이어 레벨·적중 판정·참 판 진단 (PLAYER_LEVEL_CHARM_DESIGN.md §14). 플레이 모드에서 GameScene이 로드된 뒤 실행한다.
    /// - Hit Chance: §5.3 표의 적중률·피할 확률을 실제 스탯 재계산(System_StatCalculation)과 실제 피해 계산(DamageCalculator)으로
    ///   임시 엔티티에 굴려 대조하고, 회피 불가 태그가 판정을 건너뛰는지, 능력치 보너스 순서, 시트 변환 값을 확인한다.
    /// - Save Round Trip: 참이 박힌 판·판 자리·포인트·레벨을 실제 저장과 같은 경로(SaveDtoSnapshot + 작성기 설정)로 JSON화해
    ///   다시 읽고, 새 칸이 없는 옛 세이브가 열리는지 확인한다. 파일을 쓰지 않는다.
    /// 임시 엔티티는 진단이 끝나면 지운다. 실제 플레이어·세이브는 바꾸지 않는다.
    /// </summary>
    public static class PlayerLevelCharmDiagnostics
    {
        private const int SampleSeed = 20261024;
        private const int HitSamples = 200000;
        private const float EmpiricalTolerance = 0.006f;   // 표본 오차 (20만 회, 0.6%p)
        private const float DocTolerance = 0.01f;          // 문서 표는 % 정수 반올림

        private static readonly int[] UnevadableSkillIds = { 203, 206, 208, 52, 211, 212 };

        [MenuItem("ARPG/Diagnostics/Player Level Charm/Hit Chance", false, 620)]
        public static void RunHitChance()
        {
            if (IsReady() == false)
                return;

            Random.State previous = Random.state;
            List<int> tempEntities = new List<int>();
            StringBuilder report = new StringBuilder();
            int failures = 0;
            try
            {
                Random.InitState(SampleSeed);
                report.AppendLine("[PlayerLevelCharmDiagnostics] 적중률 점검 (PLAYER_LEVEL_CHARM_DESIGN §5)");
                failures += CheckSheetConversion(report);
                failures += CheckAttributeBonus(report, tempEntities);
                failures += CheckPlayerHitsMonster(report, tempEntities);
                failures += CheckMonsterHitsPlayer(report, tempEntities);
                failures += CheckUnevadableAndFloor(report, tempEntities);
                failures += CheckLiveProfile(report);
                report.AppendLine(failures == 0 ? "결과: PASS" : $"결과: FAIL {failures}건");
            }
            finally
            {
                for (int i = 0; i < tempEntities.Count; i++)
                    AR.s.Entity.DestroyEntity(tempEntities[i]);
                Random.state = previous;
                Debug.Log(report.ToString());
            }
        }

        [MenuItem("ARPG/Diagnostics/Player Level Charm/Save Round Trip", false, 621)]
        public static void RunSaveRoundTrip()
        {
            if (IsReady() == false)
                return;

            StringBuilder report = new StringBuilder();
            int failures = 0;
            report.AppendLine("[PlayerLevelCharmDiagnostics] 세이브 왕복 (PLAYER_LEVEL_CHARM_DESIGN §7)");
            try
            {
                failures += CheckRoundTrip(report);
                failures += CheckLegacySave(report);
                failures += CheckLegacyCharm(report);
                failures += CheckInstanceIdScan(report);
            }
            catch (Exception e)
            {
                failures++;
                report.AppendLine($"  예외: {e}");
            }
            report.AppendLine(failures == 0 ? "결과: PASS" : $"결과: FAIL {failures}건");
            Debug.Log(report.ToString());
        }

        // ========== 적중률 ==========

        private static int CheckSheetConversion(StringBuilder report)
        {
            int failures = 0;
            int unevadableCount = 0;
            bool listedOk = true;
            List<SkillTable> skills = AR.s.Data.GetAllSkills();
            for (int i = 0; i < skills.Count; i++)
            {
                if ((skills[i].Tags & GE.SkillTag.Unevadable) != 0)
                    unevadableCount++;
            }
            for (int i = 0; i < UnevadableSkillIds.Length; i++)
            {
                SkillTable? skill = AR.s.Data.GetSkill(UnevadableSkillIds[i]);
                if (skill == null || (skill.Tags & GE.SkillTag.Unevadable) == 0)
                    listedOk = false;
            }
            failures += Check(report, listedOk && unevadableCount == UnevadableSkillIds.Length,
                $"회피 불가 태그: 지정 6개 모두 있음={listedOk}, 전체 {unevadableCount}개 (6)");

            MonsterTable? mantis = AR.s.Data.GetMonster(1015);
            MonsterTable? archer = AR.s.Data.GetMonster(1017);
            MonsterTable? wolf = AR.s.Data.GetMonster(1009);
            failures += Check(report, mantis != null && archer != null && wolf != null
                && Mathf.Approximately(mantis.EvasionMul, 1.5f) && Mathf.Approximately(archer.EvasionMul, 1.5f)
                && Mathf.Approximately(wolf.EvasionMul, 1f) && Mathf.Approximately(wolf.AccuracyMul, 1f),
                $"몬스터 종 배율: 1015 회피 {mantis?.EvasionMul} (1.5), 1017 회피 {archer?.EvasionMul} (1.5), 1009 {wolf?.AccuracyMul}/{wolf?.EvasionMul} (1/1)");

            MonsterAffixTable? swift = AR.s.Data.GetMonsterAffix(111);
            failures += Check(report, swift != null && swift.Stat1 == GE.Stat.EvasionInc && swift.Value1 == 200,
                $"접사 날렵한(111): {swift?.Stat1} {swift?.Value1} (EvasionInc 200)");

            StatTable? player = AR.s.Data.GetPlayer(1)?.Stat;
            StatTable? npc = AR.s.Data.GetNpc(3001)?.Stat;
            StatTable? monster = AR.s.Data.GetMonster(1023)?.Stat;
            failures += Check(report, player != null && player.MaxHp == 180 && player.MaxMp == 98
                && npc != null && npc.MaxHp == 130 && npc.MaxMp == 98
                && monster != null && monster.Str == 0 && monster.Dex == 0 && monster.Int == 0,
                $"기본값: 플레이어 HP {player?.MaxHp}/마나 {player?.MaxMp} (180/98), NPC {npc?.MaxHp}/{npc?.MaxMp} (130/98), 몬스터 1023 능력치 {monster?.Str}/{monster?.Dex}/{monster?.Int} (0)");
            return failures;
        }

        /// <summary>능력치 보너스는 최종 능력치로 계산해 가산 항으로 넣고, HP 증가 %가 그 위에 붙는다 (§4.1).</summary>
        private static int CheckAttributeBonus(StringBuilder report, List<int> temp)
        {
            int id = CreateTarget(temp, baseEvasion: 0, baseStr: 10, baseDex: 5, baseInt: 2, baseHp: 180, baseMp: 98);
            AR.s.Data.AddStatModifier(id, StatModifierSource.PlayerAttribute, 0, GE.Stat.Str, StatModifierType.Add, 5);
            AR.s.Data.AddStatModifier(id, StatModifierSource.Buff, 0, GE.Stat.Hp, StatModifierType.Multiply, 50);
            RecalculateStats();

            AR.s.Component.TryGetComponent<StatComponent>(id, out StatComponent stat);
            // (180 + 15 × 2) × 1.5 = 315, 마나 98 + 2 = 100, 회피 5 × 2 = 10
            return Check(report, stat.FinalMaxHp == 315 && stat.FinalMaxMp == 100 && stat.FinalEvasion == 10 && stat.FinalStr == 15,
                $"능력치 보너스: 힘 {stat.FinalStr}·HP {stat.FinalMaxHp} (15·315 = (180 + 30) × 1.5), 마나 {stat.FinalMaxMp} (100), 회피 {stat.FinalEvasion} (10)");
        }

        /// <summary>§5.3 "플레이어가 같은 레벨 몬스터를 칠 때 (정확도 투자 없음)" 표.</summary>
        private static int CheckPlayerHitsMonster(StringBuilder report, List<int> temp)
        {
            int failures = 0;
            int playerAccuracy = HitChanceRules.GetLevelAccuracy(50);
            failures += CheckHitCase(report, temp, "균형형 (50 대 50)", playerAccuracy,
                HitChanceRules.GetMonsterEvasion(50, 1f), 0, 0.90f);
            failures += CheckHitCase(report, temp, "10레벨 높은 균형형 (50 대 60)", playerAccuracy,
                HitChanceRules.GetMonsterEvasion(60, 1f), 0, 0.88f);
            failures += CheckHitCase(report, temp, "회피 종 ×1.5", playerAccuracy,
                HitChanceRules.GetMonsterEvasion(50, 1.5f), 0, 0.86f);
            failures += CheckHitCase(report, temp, "접사 날렵한 (회피 증가 +200%)", playerAccuracy,
                HitChanceRules.GetMonsterEvasion(50, 1f), 200, 0.75f);
            failures += CheckHitCase(report, temp, "회피 종 + 날렵한", playerAccuracy,
                HitChanceRules.GetMonsterEvasion(50, 1.5f), 200, 0.67f);
            return failures;
        }

        /// <summary>§5.3 "같은 레벨 몬스터가 플레이어를 칠 때 피할 확률 (장비·참 없음)" 표. 민첩은 기본 5 + 넣은 포인트(스탯 보정)다.</summary>
        private static int CheckMonsterHitsPlayer(StringBuilder report, List<int> temp)
        {
            int failures = 0;
            // (레벨, 넣은 민첩, 문서 피할 확률)
            int[] levels = { 1, 50, 50, 50, 100, 100, 100 };
            int[] dexPoints = { 0, 0, 82, 245, 0, 165, 495 };
            float[] docEvade = { 0.09f, 0.017f, 0.23f, 0.46f, 0.009f, 0.24f, 0.48f };
            for (int i = 0; i < levels.Length; i++)
            {
                int id = CreateTarget(temp, baseEvasion: 0, baseStr: 10, baseDex: 5, baseInt: 2, baseHp: 100000, baseMp: 98);
                if (dexPoints[i] > 0)
                    AR.s.Data.AddStatModifier(id, StatModifierSource.PlayerAttribute, 0, GE.Stat.Dex, StatModifierType.Add, dexPoints[i]);
                RecalculateStats();
                AR.s.Component.TryGetComponent<StatComponent>(id, out StatComponent stat);
                int accuracy = HitChanceRules.GetMonsterAccuracy(levels[i], 1f);
                float expectedHit = HitChanceRules.GetHitChance(accuracy, stat.FinalEvasion);
                float measuredHit = MeasureHitRate(accuracy, false, id);
                float expectedEvade = 1f - expectedHit;
                float measuredEvade = 1f - measuredHit;
                bool ok = Mathf.Abs(measuredHit - expectedHit) <= EmpiricalTolerance
                    && Mathf.Abs(expectedEvade - docEvade[i]) <= DocTolerance;
                failures += Check(report, ok,
                    $"몬스터 {levels[i]}레벨 → 민첩 {stat.FinalDex}(회피 {stat.FinalEvasion}): 피할 확률 식 {expectedEvade * 100f:F1}% · 실측 {measuredEvade * 100f:F1}% (문서 {docEvade[i] * 100f:0.#}%)");
            }
            return failures;
        }

        private static int CheckUnevadableAndFloor(StringBuilder report, List<int> temp)
        {
            int failures = 0;
            int id = CreateTarget(temp, baseEvasion: 100000, baseStr: 0, baseDex: 0, baseInt: 0, baseHp: 100000, baseMp: 0);
            RecalculateStats();
            float unevadableHit = MeasureHitRate(100, true, id);
            failures += Check(report, unevadableHit == 1f,
                $"회피 불가 태그: 회피 100000 상대에게 적중 {unevadableHit * 100f:F2}% (100%, 판정 건너뜀)");
            float floorHit = MeasureHitRate(100, false, id);
            failures += Check(report, Mathf.Abs(floorHit - HitChanceRules.MIN_HIT_CHANCE) <= EmpiricalTolerance,
                $"적중률 하한: 정확도 100 대 회피 100000 → {floorHit * 100f:F1}% (25%)");

            int zero = CreateTarget(temp, baseEvasion: 0, baseStr: 0, baseDex: 0, baseInt: 0, baseHp: 100000, baseMp: 0);
            RecalculateStats();
            float zeroHit = MeasureHitRate(0, false, zero);
            failures += Check(report, zeroHit == 1f, $"회피 0 상대: 정확도 0이어도 적중 {zeroHit * 100f:F2}% (100%)");
            return failures;
        }

        /// <summary>살아 있는 플레이어의 기본 스킬 공격 프로필이 정확도를 싣는지.</summary>
        private static int CheckLiveProfile(StringBuilder report)
        {
            int playerId = AR.s.Data.CurrentPlayerEntityId;
            if (AR.s.Component.TryGetComponent<StatComponent>(playerId, out StatComponent stat) == false)
            {
                report.AppendLine("  (살아 있는 플레이어 없음 — 프로필 확인 건너뜀)");
                return 0;
            }

            int skillEntityId = EntityManager.GetDeterministicId(playerId, EntityIdCategory.Skill, 0);
            System_SkillAttackProfileCalculation.BuildProfile(skillEntityId);
            if (AR.s.Component.TryGetComponent<SkillAttackProfileComponent>(skillEntityId, out SkillAttackProfileComponent profile) == false)
            {
                report.AppendLine("  (기본 스킬 없음 — 프로필 확인 건너뜀)");
                return 0;
            }

            PlayerData? data = AR.s.Data.Player;
            int level = data != null ? data.Level : 1;
            return Check(report, profile.Accuracy == stat.FinalAccuracy && stat.BaseAccuracy == HitChanceRules.GetLevelAccuracy(level)
                && profile.Unevadable == false,
                $"플레이어(레벨 {level}) 기본 정확도 {stat.BaseAccuracy} (A(L) {HitChanceRules.GetLevelAccuracy(level)}), 최종 {stat.FinalAccuracy}, 스킬 프로필 {profile.Accuracy}, 회피 {stat.FinalEvasion}");
        }

        private static int CheckHitCase(StringBuilder report, List<int> temp, string label, int accuracy,
            int baseEvasion, int evasionIncrease, float docHit)
        {
            int id = CreateTarget(temp, baseEvasion, 0, 0, 0, 100000, 0);
            if (evasionIncrease > 0)
                AR.s.Data.AddStatModifier(id, StatModifierSource.MonsterAffix, 111, GE.Stat.EvasionInc, StatModifierType.Add, evasionIncrease);
            RecalculateStats();
            AR.s.Component.TryGetComponent<StatComponent>(id, out StatComponent stat);
            float expected = HitChanceRules.GetHitChance(accuracy, stat.FinalEvasion);
            float measured = MeasureHitRate(accuracy, false, id);
            bool ok = Mathf.Abs(measured - expected) <= EmpiricalTolerance && Mathf.Abs(expected - docHit) <= DocTolerance;
            return Check(report, ok,
                $"{label}: 정확도 {accuracy} 대 회피 {stat.FinalEvasion} → 식 {expected * 100f:F1}% · 실측 {measured * 100f:F1}% (문서 {docHit * 100f:0}%)");
        }

        /// <summary>실제 DamageCalculator.Calculate를 HitSamples번 불러 맞은 비율을 잰다 (피해는 적용하지 않는다).</summary>
        private static float MeasureHitRate(int accuracy, bool unevadable, int targetId)
        {
            SkillAttackProfileComponent profile = new SkillAttackProfileComponent
            {
                PhysAddedMin = 10,
                PhysAddedMax = 10,
                BaseDamageMul = 1f,
                SkillDamageMul = 1f,
                CritMultiplier = 1.5f,
                Accuracy = accuracy,
                Unevadable = unevadable,
            };
            int hits = 0;
            for (int i = 0; i < HitSamples; i++)
            {
                if (DamageCalculator.Calculate(profile, targetId).IsEvaded == false)
                    hits++;
            }
            return hits / (float)HitSamples;
        }

        private static int CreateTarget(List<int> temp, int baseEvasion, int baseStr, int baseDex, int baseInt, int baseHp, int baseMp)
        {
            int id = AR.s.Entity.CreateEntity();
            temp.Add(id);
            StatComponent stat = new StatComponent
            {
                BaseStr = baseStr,
                BaseDex = baseDex,
                BaseInt = baseInt,
                BaseMaxHp = baseHp,
                BaseMaxMp = baseMp,
                BaseEvasion = baseEvasion,
                BaseMoveSpeedMul = 100,
                BaseMaxFireResist = BalanceConstants.BaseMaxResistance,
                BaseMaxIceResist = BalanceConstants.BaseMaxResistance,
                BaseMaxLightningResist = BalanceConstants.BaseMaxResistance,
                BaseMaxPoisonResist = BalanceConstants.BaseMaxResistance,
            };
            stat.CopyBaseToFinal();
            stat.SetCurrentHpDirect(baseHp);
            AR.s.Component.AddComponent(id, stat);
            AR.s.Component.AddComponent(id, new StatDirtyTag());
            return id;
        }

        /// <summary>실제 스탯 재계산 시스템을 한 번 돌린다 (dirty 엔티티만 처리한다).</summary>
        private static void RecalculateStats()
        {
            new System_StatCalculation().OnUpdate(0f);
        }

        // ========== 세이브 왕복 ==========

        private static int CheckRoundTrip(StringBuilder report)
        {
            int failures = 0;
            PlayerData source = BuildSamplePlayer(out ItemData slotBoard, out ItemData bagBoard);
            string json = WriteLikeSave(source);
            PlayerData? loaded = JsonConvert.DeserializeObject<PlayerData>(json, JsonSettings.Default);
            if (loaded == null)
                return Check(report, false, "세이브 왕복: JSON을 다시 읽지 못함");
            loaded.LoadCompleted();

            failures += Check(report, loaded.Level == 37 && loaded.Exp == 1234 && loaded.AllocatedStr == 50
                && loaded.AllocatedDex == 60 && loaded.AllocatedInt == 40 && loaded.UnspentAttributePoints == 30,
                $"레벨·경험치·포인트: 레벨 {loaded.Level} 경험치 {loaded.Exp} 힘/민첩/지능 {loaded.AllocatedStr}/{loaded.AllocatedDex}/{loaded.AllocatedInt} 남음 {loaded.UnspentAttributePoints} (37·1234·50/60/40·30)");

            ItemData? loadedSlotBoard = loaded._charmBoardSlots.Length == PlayerLevelRules.CHARM_BOARD_SLOT_COUNT ? loaded._charmBoardSlots[0] : null;
            failures += Check(report, loadedSlotBoard?.CharmBoard != null
                && loadedSlotBoard.CharmBoard.BlockedMask == slotBoard.CharmBoard!.BlockedMask
                && loadedSlotBoard.CharmBoard.Charms.Count == slotBoard.CharmBoard.Charms.Count
                && loaded._charmBoardSlots[1] == null,
                $"판 자리: {loaded._charmBoardSlots.Length}칸, 0번 판 막힌 칸 {loadedSlotBoard?.CharmBoard?.BlockedMask} 박힌 참 {loadedSlotBoard?.CharmBoard?.Charms.Count}개 (같아야 함)");

            bool charmsEqual = loadedSlotBoard?.CharmBoard != null;
            for (int i = 0; charmsEqual == true && i < slotBoard.CharmBoard.Charms.Count; i++)
            {
                CharmPlacement a = slotBoard.CharmBoard.Charms[i];
                CharmPlacement b = loadedSlotBoard!.CharmBoard!.Charms[i];
                charmsEqual = b.Charm?.Charm != null && a.AnchorCell == b.AnchorCell && a.Rotation == b.Rotation
                    && a.Charm.ItemInstanceId == b.Charm.ItemInstanceId && a.Charm.Charm!.Shape == b.Charm.Charm.Shape
                    && a.Charm.Charm.ItemLevel == b.Charm.Charm.ItemLevel && a.Charm.Charm.Mods.Count == b.Charm.Charm.Mods.Count
                    && b.Charm.Table != null;
                for (int m = 0; charmsEqual == true && m < a.Charm.Charm!.Mods.Count; m++)
                {
                    ModInstance ma = a.Charm.Charm.Mods[m];
                    ModInstance mb = b.Charm!.Charm!.Mods[m];
                    charmsEqual = ma.ModTableId == mb.ModTableId && ma.Tier == mb.Tier && ma.Value1 == mb.Value1
                        && mb.Slot == ma.Slot && (mb.Slot == GE.ModSlot.Prefix || mb.Slot == GE.ModSlot.Postfix)
                        && mb.Table != null;
                }
                charmsEqual = charmsEqual == true && a.Charm.Charm!.GetGrade() == b.Charm!.Charm!.GetGrade();
            }
            failures += Check(report, charmsEqual, "박힌 참: 위치·회전·인스턴스 Id·모양·아이템 레벨·옵션(티어·값·접두/접미 칸)·등급·표 연결이 같다");

            ItemData? loadedBag = loaded._inventory.Count > 0 ? loaded._inventory[0] : null;
            failures += Check(report, loadedBag?.CharmBoard != null && loadedBag.CharmBoard.Charms.Count == bagBoard.CharmBoard!.Charms.Count
                && loaded._inventory[1]?.Charm != null,
                $"가방: 참이 박힌 판(참 {loadedBag?.CharmBoard?.Charms.Count}개)과 낱개 참이 유지된다");
            return failures;
        }

        /// <summary>새 칸(포인트·판 자리)이 없는 옛 세이브: 저장된 레벨, 포인트 0 → (레벨 − 1) × 5, 빈 판 자리 9칸 (§7).</summary>
        private static int CheckLegacySave(StringBuilder report)
        {
            PlayerData source = BuildSamplePlayer(out _, out _);
            JObject obj = JObject.Parse(WriteLikeSave(source));
            obj.Remove(nameof(PlayerData.AllocatedStr));
            obj.Remove(nameof(PlayerData.AllocatedDex));
            obj.Remove(nameof(PlayerData.AllocatedInt));
            obj.Remove(nameof(PlayerData.UnspentAttributePoints));
            obj.Remove(nameof(PlayerData._charmBoardSlots));
            obj[nameof(PlayerData.Level)] = 1;
            obj[nameof(PlayerData.Exp)] = 0;

            PlayerData? loaded = JsonConvert.DeserializeObject<PlayerData>(obj.ToString(Formatting.None), JsonSettings.Default);
            if (loaded == null)
                return Check(report, false, "옛 세이브: 읽지 못함");
            loaded.LoadCompleted();

            bool slotsEmpty = loaded._charmBoardSlots != null && loaded._charmBoardSlots.Length == PlayerLevelRules.CHARM_BOARD_SLOT_COUNT;
            for (int i = 0; slotsEmpty == true && i < loaded._charmBoardSlots!.Length; i++)
                slotsEmpty = loaded._charmBoardSlots[i] == null;
            int failures = Check(report, loaded.Level == 1 && loaded.Exp == 0 && loaded.UnspentAttributePoints == 0
                && loaded.AllocatedStr + loaded.AllocatedDex + loaded.AllocatedInt == 0 && slotsEmpty,
                $"옛 세이브(새 칸 없음): 레벨 {loaded.Level}, 포인트 남음 {loaded.UnspentAttributePoints}, 판 자리 {loaded._charmBoardSlots?.Length}칸 비어 있음={slotsEmpty}");

            // 레벨만 있고 포인트 칸이 없는 경우 남은 포인트를 레벨로 채운다
            obj[nameof(PlayerData.Level)] = 12;
            PlayerData? leveled = JsonConvert.DeserializeObject<PlayerData>(obj.ToString(Formatting.None), JsonSettings.Default);
            leveled?.LoadCompleted();
            failures += Check(report, leveled != null && leveled.UnspentAttributePoints == 55,
                $"포인트 칸 없는 12레벨: 남은 포인트 {leveled?.UnspentAttributePoints} (55)");
            return failures;
        }

        /// <summary>
        /// 2026-10-25 참 등급 개편 전 세이브 (§6.3): 옵션 칸 "Charm"은 Mod 표의 접두·접미로, 3×3 참(아이템 8006)은
        /// 같은 기준 칸의 2×2 참으로 바뀐다. 옵션·티어는 그대로고, 값은 지금 참 값 표 범위를 벗어난 것만 범위 안으로 맞춘다.
        /// 샘플 값은 2×2 T8 범위(최대 HP 27~35, 힘 13~17, 화염 저항 11) 위·안·아래를 하나씩 둔다. 바뀐 2×2는 판에 그대로 들어간다.
        /// </summary>
        private static int CheckLegacyCharm(StringBuilder report)
        {
            int[] originals = { 40, 15, 1 };
            const string legacyMods = "[{\"ModTableId\":1000,\"Slot\":\"Charm\",\"Tier\":8,\"Value1\":40,\"Value2\":0},"
                + "{\"ModTableId\":1300,\"Slot\":\"Charm\",\"Tier\":8,\"Value1\":15,\"Value2\":0},"
                + "{\"ModTableId\":1203,\"Slot\":\"Charm\",\"Tier\":8,\"Value1\":1,\"Value2\":0}]";
            const string boardJson = "{\"Id\":8000,\"ItemInstanceId\":900001,\"CharmBoard\":{\"BlockedMask\":0,\"Charms\":[{\"Charm\":"
                + "{\"Id\":8006,\"ItemInstanceId\":900002,\"Charm\":{\"Shape\":\"Square3\",\"ItemLevel\":72,\"Mods\":" + legacyMods + "}},"
                + "\"AnchorCell\":0,\"Rotation\":0}]}}";
            ItemData? board = JsonConvert.DeserializeObject<ItemData>(boardJson, JsonSettings.Default);
            if (board?.CharmBoard == null)
                return Check(report, false, "옛 참: JSON을 읽지 못함");
            board.OnLoadCompleted();

            ItemData? charm = board.CharmBoard.Charms.Count == 1 ? board.CharmBoard.Charms[0].Charm : null;
            CharmData? data = charm?.Charm;
            List<int> scratch = new List<int>();
            bool shapeOk = charm != null && data != null && charm.Id == CharmRules.GetCharmItemId(GE.CharmShape.Square2)
                && charm.Table != null && data.Shape == GE.CharmShape.Square2
                && CharmRules.CanPlace(board.CharmBoard, data.Shape, 0, 0, 0, scratch) == true;
            bool modsOk = data != null && data.Mods.Count == 3
                && data.Mods[0].Slot == GE.ModSlot.Prefix && data.Mods[0].Tier == 8
                && data.Mods[1].Slot == GE.ModSlot.Postfix && data.Mods[2].Slot == GE.ModSlot.Postfix
                && data.GetGrade() == GE.EquipmentGrade.Rare;

            // 값: 범위 밖만 범위 안으로 (위 → 최대, 안 → 그대로, 아래 → 최소)
            bool valuesOk = data != null && data.Mods.Count == originals.Length;
            int[] side = new int[3];
            StringBuilder valueLine = new StringBuilder();
            for (int i = 0; valuesOk == true && i < originals.Length; i++)
            {
                ModInstance mod = data!.Mods[i];
                CharmModTierTable? valueRow = AR.s.Data.GetCharmModTier(mod.ModTableId, mod.Tier);
                if (valueRow == null || CharmRules.TryGetOptionValueRange(valueRow, GE.CharmShape.Square2, out int min, out int max) == false)
                {
                    valuesOk = false;
                    break;
                }
                valuesOk &= mod.Value1 == Mathf.Clamp(originals[i], min, max);
                side[originals[i] > max ? 0 : originals[i] < min ? 2 : 1]++;
                valueLine.Append($" {originals[i]}→{mod.Value1}({min}~{max})");
            }
            bool coversAll = side[0] > 0 && side[1] > 0 && side[2] > 0;
            return Check(report, shapeOk && modsOk && valuesOk && coversAll,
                $"옛 참: 3×3 → {data?.Shape} (아이템 {charm?.Id}, 판에 그대로 들어감={shapeOk}), 옵션 칸 Charm → 최대 HP 접두·힘·화염 저항 접미, 등급 {data?.GetGrade()} (희귀)={modsOk}, 값 범위 맞춤{valueLine}={valuesOk}, 범위 위·안·아래 모두 확인={coversAll}");
        }

        /// <summary>저장 로드 때 다음 아이템 인스턴스 Id를 고르는 스캔이 판에 박힌 참까지 보는지.</summary>
        private static int CheckInstanceIdScan(StringBuilder report)
        {
            ItemData board = new ItemData { Id = CharmRules.BOARD_ITEM_ID, ItemInstanceId = 10, CharmBoard = new CharmBoardData() };
            board.CharmBoard.Charms.Add(new CharmPlacement
            {
                Charm = new ItemData { Id = CharmRules.CHARM_ITEM_ID_FIRST, ItemInstanceId = 987654, Charm = new CharmData() },
            });
            var method = typeof(DataManager).GetMethod("ScanItemInstanceId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            object[] args = { board, 0 };
            method?.Invoke(null, args);
            int maxId = (int)args[1];
            return Check(report, maxId == 987654, $"인스턴스 Id 스캔: 박힌 참 Id {maxId} (987654)");
        }

        private static PlayerData BuildSamplePlayer(out ItemData slotBoard, out ItemData bagBoard)
        {
            PlayerData player = new PlayerData
            {
                PlayerId = 1,
                PlayerTableId = 1,
                Level = 37,
                Exp = 1234,
                AllocatedStr = 50,
                AllocatedDex = 60,
                AllocatedInt = 40,
                UnspentAttributePoints = 30,
            };
            for (int i = 0; i < GE.PLAYER_INVENTORY_SLOTCOUNT_MAX; i++)
                player._inventory.Add(null);

            slotBoard = AR.s.Item.CreateRandomCharmBoard()!;
            slotBoard.CharmBoard!.BlockedMask = 1 << 8;     // 오른쪽 아래 1칸 막힘
            PlaceCharm(slotBoard, GE.CharmShape.Line2, 0, 0);           // 0·1칸 (가로)
            PlaceCharm(slotBoard, GE.CharmShape.LShape, 3, 0);          // 3·6·7칸
            player._charmBoardSlots[0] = slotBoard;

            bagBoard = AR.s.Item.CreateRandomCharmBoard()!;
            bagBoard.CharmBoard!.BlockedMask = 0;
            PlaceCharm(bagBoard, GE.CharmShape.Square2, 0, 0);          // 0·1·3·4칸
            PlaceCharm(bagBoard, GE.CharmShape.Line3, 6, 0);            // 6·7·8칸 (아랫줄 가로)
            player._inventory[0] = bagBoard;
            player._inventory[1] = AR.s.Item.CreateCharm(GE.CharmShape.Square2, GE.EquipmentGrade.Magic, 45);
            return player;
        }

        private static void PlaceCharm(ItemData board, GE.CharmShape shape, int anchor, int rotation)
        {
            ItemData charm = AR.s.Item.CreateCharm(shape, GE.EquipmentGrade.Rare, 72)!;
            List<int> scratch = new List<int>();
            if (CharmRules.CanPlace(board.CharmBoard!, shape, rotation, anchor, -1, scratch) == false)
                throw new InvalidOperationException($"샘플 참 배치 실패: {shape} 기준 {anchor} 회전 {rotation}");
            board.CharmBoard!.Charms.Add(new CharmPlacement { Charm = charm, AnchorCell = anchor, Rotation = rotation });
        }

        /// <summary>실제 저장과 같은 경로: SaveDtoSnapshot 사본을 작성기 설정(Formatting.None + StringEnumConverter)으로 JSON화.</summary>
        private static string WriteLikeSave(object source)
        {
            JsonSerializerSettings settings = new JsonSerializerSettings();
            settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
            return JsonConvert.SerializeObject(new SaveDtoSnapshot().Capture(source), Formatting.None, settings);
        }

        // ========== 공통 ==========

        private static bool IsReady()
        {
            if (EditorApplication.isPlaying == false || AR.s == null || AR.s.Data == null || AR.s.Item == null)
            {
                Debug.LogError("[PlayerLevelCharmDiagnostics] 플레이 모드에서 GameScene이 로드된 뒤 실행하세요.");
                return false;
            }
            return true;
        }

        private static int Check(StringBuilder report, bool ok, string message)
        {
            report.Append(ok == true ? "  [PASS] " : "  [FAIL] ").AppendLine(message);
            return ok == true ? 0 : 1;
        }
    }
}
