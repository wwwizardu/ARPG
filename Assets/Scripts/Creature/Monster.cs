#nullable enable
using UnityEngine;
using ARPG.Tables;

namespace ARPG.Creature
{
    public class Monster : CharacterBase
    {
        protected Tables.MonsterTable? _monsterTable = null;
        public MonsterTable MonsterTable => _monsterTable!;
        
        public override void Initialize()
        {
            base.Initialize();
        }

        public override void InitializeECSComponents()
        {
            base.InitializeECSComponents();

            // AI 관련 컴포넌트 추가
            AR.s.Component.AddComponent<Component.AIComponent>(_entityId, new Component.AIComponent
            {
                AITableID = 0,
                TargetEntityId = 0,
                LastKnownTargetPos = Vector2.zero
            });

            AR.s.Component.AddComponent<Component.AIPerceptionComponent>(_entityId, new Component.AIPerceptionComponent
            {
                DetectionRange = 5f,
                AttackRange = 0.8f,
                LoseTargetRange = 10f,
                FieldOfView = 360f,
                LastDetectionTime = 0f
            });            

            AR.s.Component.AddComponent<Component.AIBehaviorTypeComponent>(_entityId, new Component.AIBehaviorTypeComponent
            {
                BehaviorType = Component.AIBehaviorType.Melee,
                AggroRange = 10f,
                AttackRange = 1f
            });
            AR.s.Component.AddComponent<Component.AIStateComponent>(_entityId, new Component.AIStateComponent
            {
                CurrentState = Component.AIState.Idle,
                SpawnPosition = Vector2.zero
            });

            // 기본 스킬 생성 (스킬 ID 2)
            CreateSkill(0, 2); 
        }

        public override void Reset()
        {
            base.Reset();
        }

        public override bool LoadTable(int inId)
        {
            _monsterTable = AR.s.Data.GetMonster(inId);
            if (_monsterTable == null)
            {
                Debug.LogError($"[Monster] LoadTable - MonsterTable not found for Id: {inId}");
                return false;
            }

            _table = _monsterTable;

            return true;
        }

        public override (int, int) GetAttackDamage()
        {
            if (_monsterTable?.Weapon == null)
                return (0, 0);

            return (_monsterTable.Weapon.DamageMin, _monsterTable.Weapon.DamageMax);
        }

        public override void OnEntityDestroy()
        {
            if (AR.s.Monster != null)
            {
                AR.s.Monster.UnregisterMonster(this);
            }
        }
    }
}
