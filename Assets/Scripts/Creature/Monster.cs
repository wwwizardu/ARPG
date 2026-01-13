#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using ARPG.AI;
using ARPG.Item;
using ARPG.Tables;

namespace ARPG.Creature
{
    public class Monster : CharacterBase
    {
        protected Tables.MonsterTable? _monsterTable = null;
        private bool _activated = false;
        private AIBase? _ai = null;
        private int _instanceId = -1;

        private float _thinkTime = 0f;

        public MonsterTable MonsterTable => _monsterTable!;
        
        public override void Initialize()
        {
            base.Initialize();

            _team = GlobalEnum.TeamType.Monster;
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
                AttackRange = 2f
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

            _ai?.Reset();
        }

        public override bool LoadTable(int inId)
        {
            _monsterTable = AR.s.Data.GetMonster(inId);
            if (_monsterTable == null)
            {
                Debug.LogError($"[Monster] LoadTable - MonsterTable not found for Id: {inId}");
                return false;
            }

            // Ai Table에 따른 ai 생성
            if (_monsterTable.AiTable == null)
                return false;

            if (_monsterTable.AiTable.AiType == GlobalEnum.AiType.NormalMonster)
            {
                _ai = new BasicMonsterAI(this, _monsterTable.AiTable);
                _ai.Initialize();

                //_characterInfo.SkillController.CreateSkill(_monsterTable.AiTable.SkillId1);
            }

            _table = _monsterTable;

            return true;
        }

        public override void OnHit(CharacterBase? inAttacker, bool isOnHit, GlobalEnum.DamageType inDamageType, int inDamage)
        {
            base.OnHit(inAttacker, isOnHit, inDamageType, inDamage);

            // if(inAttacker != null && isOnHit == true)
            // {
            //     if (inDamageType == GlobalEnum.DamageType.Physics)
            //     {
            //         int BloodingRate = inAttacker.Stat.GetStat(GlobalEnum.Stat.BloodingRate) + 50;
            //         if(UnityEngine.Random.Range(0, 100) < BloodingRate)
            //         {
            //             int bloodingDamage = Mathf.FloorToInt(inDamage * 0.3f);
            //             AddBuff(1, bloodingDamage);
            //         }
            //     }                
            // }
        }

        public override (int, int) GetAttackDamage()
        {
            if (_monsterTable?.Weapon == null)
                return (0, 0);

            return (_monsterTable.Weapon.DamageMin, _monsterTable.Weapon.DamageMax);
        }

        protected override void Dead()
        {
            base.Dead();

            DropItems();
        }

        public void Activate()
        {
            _activated = true;
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            _activated = false;
            gameObject.SetActive(false);
        }

        public bool IsActivated()
        {
            return _activated;
        }

        public void SetInstanceId(int instanceId)
        {
            _instanceId = instanceId;
        }

        public int GetInstanceId()
        {
            return _instanceId;
        }

        protected override void OnUpdate()
        {
            if (_activated == false)
                return;

            base.OnUpdate();

        }

        protected override void OnFixedUpdateCharacter(float inDeltaTime)
        {
            if (_activated == false)
                return;

            base.OnFixedUpdateCharacter(inDeltaTime);

            _thinkTime += inDeltaTime;
            if(0.1f < _thinkTime)
            {
                _ai?.Think();
                _thinkTime -= 0.1f;
            }
        }

        protected override void UpdateInput()
        {
            // if (_activated == false || _ai == null)
            //     return;

            // var (inputDirection, velocity) = _ai.CalculateMove();

            // _inputDirection = inputDirection;
            // _velocity = velocity;

            // if (_velocity.IsZero() == false)
            // {
            //     Vector3 movement = new Vector3(_velocity.x, _velocity.y, 0) * Time.deltaTime;
            //     transform.position += movement;
            // }
        }

        protected void DropItems()
        {
            if (Table == null || AR.s?.Data == null)
            {
                Debug.LogError("[Monster] Table is null");
                return;
            }

            var DropTable = AR.s?.Data.GetDrop(MonsterTable.DropId);
            if (DropTable == null)
            {
                Debug.LogError($"[Monster] DropTable is null. DropId: {MonsterTable.DropId}");
                return;
            }

            // 드랍 아이템 결정
            int totalRate = DropTable.NothingRate + DropTable.CurrencyRate + DropTable.EquipmentRate;
            int randomValue = UnityEngine.Random.Range(0, totalRate);

            // 아무것도 안떨어짐
            if (randomValue < DropTable.NothingRate)
                return;

            int dropItemId = 0;
            // 화폐 vs 장비 결정
            if (randomValue < DropTable.NothingRate + DropTable.CurrencyRate)
            {
                // 화폐 드랍
                dropItemId = SelectRandomCurrencyItem(DropTable.CurrencyId);
            }
            else
            {
                // 장비 드랍
                dropItemId = SelectRandomEquipmentItem(DropTable.EquipmentId);
            }

            // Addressable을 사용하여 비동기로 아이템 GameObject 생성
            DropItemObjectAsync(dropItemId);
        }

        private int SelectRandomCurrencyItem(int inCurrencyTableId)
        {
            var currencyTable = AR.s?.Data.GetDropCurrency(inCurrencyTableId);
            if (currencyTable == null || currencyTable.DropList == null)
                return 0;

            return SelectRandomIdFromDropInfoList(currencyTable.DropList, "currency");
        }

        private int SelectRandomEquipmentItem(int inEquipmentTableId)
        {
            var equipmentTable = AR.s?.Data.GetDropEquipment(inEquipmentTableId);
            if (equipmentTable == null || equipmentTable.DropList == null)
                return 0;

            return SelectRandomIdFromDropInfoList(equipmentTable.DropList, "equipment");
        }

        private int SelectRandomIdFromDropInfoList(List<Tables.DropInfo> inDropList, string inDropType)
        {
            if (inDropList == null || inDropList.Count == 0)
                return 0;

            // DropList의 전체 확률 합계 계산
            int totalDropRate = 0;
            foreach (var dropInfo in inDropList)
            {
                totalDropRate += dropInfo.Rate;
            }

            // 랜덤 값으로 아이템 선택
            int randomDropValue = UnityEngine.Random.Range(0, totalDropRate);
            int cumulativeRate = 0;

            foreach (var dropInfo in inDropList)
            {
                cumulativeRate += dropInfo.Rate;
                if (randomDropValue < cumulativeRate)
                {
                    Debug.Log($"[Monster] Dropping {inDropType}: ItemId={dropInfo.Id}, RandomValue({randomDropValue}), Rate={dropInfo.Rate}/{totalDropRate}");
                    return dropInfo.Id;
                }
            }

            return 0;
        }

        private async void DropItemObjectAsync(int inItemId)
        {
            if(await AR.s.Item.CreateItem(inItemId, 1, transform.position) == false)
            {
                Debug.LogError($"[Monster] DropItemObjectAsync - Failed to create item with Id({inItemId})");
            }
        }
    }
}
