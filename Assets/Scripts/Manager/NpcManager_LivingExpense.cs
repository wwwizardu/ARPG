#nullable enable
using System.Collections.Generic;
using ARPG.Creature;
using ARPG.Data;
using ARPG.History;

namespace ARPG.Npc
{
    /// <summary>
    /// 마을 골드 밸런스 — NPC 쪽 골드 흐름 (GOLD_BALANCE_DESIGN D131·D132).
    /// 세계 물가 지수용 NPC Gold 합계, Rest 종료 생활비(일부 소멸), 진단 카운터를 둔다.
    /// </summary>
    public partial class NpcManager
    {
        // D131: 예비금 = 치료비 × 3. 예비금을 넘은 Gold의 70%를 생활비로 쓴다.
        // 정수 비율로 둔다 — 부동소수 곱의 내림이 1G씩 어긋나지 않게 한다.
        public const int LivingExpenseReserveTreatmentMultiple = 3;
        public const int LivingExpensePercent = 70;
        // D132 B-주: 생활비의 1.8%(천분율 18)는 사라지고 나머지는 소속 마을 금고로 간다.
        public const int LivingExpenseBurnPerMille = 18;

        // 골드 흐름 진단 카운터 (휘발성). Reset에서 새로 만든다. 진단 도구는 시뮬레이션 동안 새 인스턴스로 바꿔 끼운다.
        private NpcGoldFlowCounters _goldFlowCounters = new();

        public NpcGoldFlowCounters GoldFlowCounters => _goldFlowCounters;

        /// <summary>살아 있는 NPC의 Gold 합계 (세계 물가 지수의 NPC 몫, D126). 사망한 NPC의 Gold는 소멸한 것으로 본다(D46).</summary>
        public long SumLivingNpcGold()
        {
            long total = 0;
            foreach (KeyValuePair<int, NpcSaveData> pair in _npcSaveDict)
            {
                NpcSaveData saveData = pair.Value;
                if (saveData.Condition == CharacterConditions.Dead)
                    continue;
                total += saveData.Gold;
            }
            return total;
        }

        private void ResetLivingExpenseRuntime()
        {
            _goldFlowCounters = new NpcGoldFlowCounters();
        }

        /// <summary>
        /// Rest 정상 종료 생활비 (D131·D132 B-주). ResolveRestEnd가 절도 판정 다음에만 부른다 — 파티 합류 경로(allowTheft false)는 내지 않는다.
        /// 예비금(치료비 × 3)을 넘은 Gold의 70%(내림)를 쓰고, 그중 1.8%는 사라진다(1G 미만 소수 부분은 결정론 확률 반올림, salt 180).
        /// 나머지는 소속 마을 금고로 간다. 마을 입금이 실패하면 아무것도 빼지 않는다. 즉시 저장하지 않는다 (D47).
        /// </summary>
        private void ResolveLivingExpenseOnRestEnd(int entityId, NpcSaveData saveData, float restStartGameTime, int worldSeed)
        {
            MarkSaveDirty(entityId);
            if (saveData.VillageId < 0)
                return;
            int reserve = GetTreatmentFee() * LivingExpenseReserveTreatmentMultiple;
            int surplus = saveData.Gold - reserve;
            if (surplus <= 0)
                return;
            int spend = (int)((long)surplus * LivingExpensePercent / 100);
            if (spend <= 0)
                return;

            long burnScaled = (long)spend * LivingExpenseBurnPerMille;
            int burn = (int)(burnScaled / 1000);
            int burnRemainder = (int)(burnScaled % 1000);
            if (burnRemainder > 0)
            {
                HistoryRng rng = NpcActivityResolver.CreateRng(entityId, restStartGameTime, worldSeed,
                    NpcActivityResolver.LivingExpenseBurnSalt);
                if (rng.NextDouble() * 1000.0 < burnRemainder)
                    burn++;
            }

            int deposit = spend - burn;
            if (deposit > 0 && AR.s.Village.DepositNpcGold(saveData.VillageId, deposit) == false)
                return;

            saveData.Gold -= spend;
            _goldFlowCounters.LivingSpendCount++;
            _goldFlowCounters.LivingSpendDeposited += deposit;
            _goldFlowCounters.LivingSpendBurned += burn;
        }
    }

    /// <summary>
    /// NPC 골드 흐름 진단 카운터 (휘발성, 세이브 대상 아님). NpcManager 인스턴스가 소유하고 Reset에서 새로 만든다.
    /// 진단 도구가 시뮬레이션 동안 새 인스턴스로 바꿔 끼워 읽고, 닫힌 경제 검산식에 소멸분을 넣는다.
    /// </summary>
    public sealed class NpcGoldFlowCounters
    {
        public long LivingSpendCount;       // 생활비를 낸 Rest 종료 수
        public long LivingSpendDeposited;   // 생활비 중 마을 금고로 간 Gold
        public long LivingSpendBurned;      // 생활비 중 소멸한 Gold (D132 B-주)
    }
}
