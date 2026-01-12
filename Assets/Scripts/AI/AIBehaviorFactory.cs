using ARPG.Component;
using ARPG.AI.Behaviors;
using System.Collections.Generic;

namespace ARPG.AI
{
    public static class AIBehaviorFactory
    {
        private static readonly Dictionary<AIBehaviorType, IAIBehavior> _behaviors = new Dictionary<AIBehaviorType, IAIBehavior>();

        static AIBehaviorFactory()
        {
            // 행동 인스턴스 등록
            _behaviors[AIBehaviorType.Melee] = new MeleeAIBehavior();
            _behaviors[AIBehaviorType.Ranged] = new RangedAIBehavior();
            // _behaviors[AIBehaviorType.Patrol] = new PatrolAIBehavior();
            // _behaviors[AIBehaviorType.Defensive] = new DefensiveAIBehavior();
            // 필요한 행동 타입 추가...
        }

        public static IAIBehavior GetBehavior(AIBehaviorType behaviorType)
        {
            if (_behaviors.TryGetValue(behaviorType, out IAIBehavior behavior))
            {
                return behavior;
            }

            UnityEngine.Debug.LogWarning($"Behavior type {behaviorType} not found, using Melee as default");
            return _behaviors[AIBehaviorType.Melee];
        }
    }
}
