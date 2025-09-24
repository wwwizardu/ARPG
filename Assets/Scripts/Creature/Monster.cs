#nullable enable
using UnityEngine;
using UnityEngine.AddressableAssets;
using ARPG.AI;

namespace ARPG.Creature
{
    public class Monster : CharacterBase
    {
        private bool _activated = false;
        private MonsterAIBase? _ai = null;
        private int _instanceId = -1;

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

            if (DropTable.DropRate <= UnityEngine.Random.Range(0, 100))
                return;

            // Addressable을 사용하여 비동기로 아이템 GameObject 생성
            DropItemAsync();
        }

        private async void DropItemAsync()
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
