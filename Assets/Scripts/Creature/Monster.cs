#nullable enable
using UnityEngine;
using UnityEngine.AddressableAssets;
using ARPG.AI;

namespace ARPG.Creature
{
    public class Monster : CharacterBase
    {
        protected new Tables.MonsterTable? _table = null;
        private bool _activated = false;
        private MonsterAIBase? _ai = null;
        private int _instanceId = -1;

        public new Tables.MonsterTable? Table { get { return _table; } }

        public override void Initialize()
        {
            base.Initialize();

            _ai = new BasicMonsterAI(this);
            _ai.Initialize();
        }

        public override void Reset()
        {
            base.Reset();

            _ai?.Reset();
        }

        public override bool LoadTable(int inId)
        {
            _table = AR.s.Data.GetMonster(inId);
            if (_table == null)
            {
                Debug.LogError($"[Monster] LoadTable - MonsterTable not found for Id: {inId}");
                return false;
            }

            return true;
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
        }

        protected override void UpdateInput()
        {
            if (_activated == false || _ai == null)
                return;

            var (inputDirection, velocity) = _ai.Think();

            _inputDirection = inputDirection;
            _velocity = velocity;

            if (_velocity.IsZero() == false)
            {
                Vector3 movement = new Vector3(_velocity.x, _velocity.y, 0) * Time.deltaTime;
                transform.position += movement;
            }
        }

        protected void DropItems()
        {
            if (Table == null || AR.s?.Data == null)
            {
                Debug.LogError("[Monster] Table is null");
                return;
            }

            var DropTable = AR.s?.Data.GetDrop(Table.DropId);
            if (DropTable == null)
            {
                Debug.LogError($"[Monster] DropTable is null. DropId: {Table.DropId}");
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
                var currencyTable = AR.s?.Data.GetDropCurrency(DropTable.CurrencyId);
                if (currencyTable != null)
                {
                    
                    Debug.Log($"[Monster] Dropping currency from table: {DropTable.CurrencyId}");
                    // TODO: 실제 화폐 드랍 로직 구현
                }
            }
            else
            {
                // 장비 드랍
                var equipmentTable = AR.s?.Data.GetDropEquipment(DropTable.EquipmentId);
                if (equipmentTable != null)
                {
                    Debug.Log($"[Monster] Dropping equipment from table: {DropTable.EquipmentId}");
                    // TODO: 실제 장비 드랍 로직 구현
                }
            }


            // Addressable을 사용하여 비동기로 아이템 GameObject 생성
            DropItemObjectAsync();
        }

        private async void DropItemObjectAsync()
        {
            try
            {


                var handle = Addressables.InstantiateAsync("Item/Item", transform.position, Quaternion.identity);
                var itemObject = await handle.Task;

                if (itemObject != null)
                {
                    Debug.Log("[Monster] Item dropped successfully");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Monster] Failed to instantiate item: {ex.Message}");
            }
        }
    }
}
