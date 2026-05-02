using ARPG.Component;
using ARPG.AI.StateHandlers;
using System.Collections.Generic;

namespace ARPG.AI
{
    /// <summary>
    /// AI 행동 프로필 팩토리
    /// (AIBehaviorType, AIState) 조합으로 적절한 IAIStateHandler를 반환
    /// 각 BehaviorType별로 상태 핸들러 배열(프로필)을 등록
    /// </summary>
    public static class AIBehaviorFactory
    {
        // AIState enum 인덱스로 접근하는 핸들러 배열
        private static readonly Dictionary<AIBehaviorType, IAIStateHandler[]> _profiles = new Dictionary<AIBehaviorType, IAIStateHandler[]>();

        static AIBehaviorFactory()
        {
            // 공통 핸들러 (인스턴스 재사용)
            var idle = new IdleStateHandler();
            var patrol = new PatrolStateHandler();
            var chase = new ChaseStateHandler();
            var meleeAttack = new MeleeAttackStateHandler();
            var rangedAttack = new RangedAttackStateHandler();
            var retreat = new RetreatStateHandler();
            var flee = new FleeStateHandler();
            var build = new BuildStateHandler();

            // AIState enum 순서: Idle=0, Patrol=1, Chase=2, Attack=3, Retreat=4, Flee=5, Return=6, Build=7
            _profiles[AIBehaviorType.Melee] = new IAIStateHandler[]
                { idle, null, chase, meleeAttack, retreat, null, null, null };

            _profiles[AIBehaviorType.Ranged] = new IAIStateHandler[]
                { idle, null, chase, rangedAttack, retreat, null, null, null };

            _profiles[AIBehaviorType.Patrol] = new IAIStateHandler[]
                { idle, patrol, chase, meleeAttack, retreat, flee, null, build };

            _profiles[AIBehaviorType.PatrolRanged] = new IAIStateHandler[]
                { idle, patrol, chase, rangedAttack, retreat, flee, null, build };
        }

        /// <summary>
        /// BehaviorType과 현재 AIState에 맞는 핸들러 반환
        /// </summary>
        public static IAIStateHandler GetStateHandler(AIBehaviorType type, AIState state)
        {
            if (_profiles.TryGetValue(type, out var handlers))
            {
                int index = (int)state;
                if (index >= 0 && index < handlers.Length)
                {
                    return handlers[index];
                }
            }

            return null;
        }
    }
}
