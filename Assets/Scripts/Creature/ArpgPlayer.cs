#nullable enable
using ARPG.Component;
using ARPG.Data;
using ARPG.Tables;
using ARPG.UI;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Creature
{
    public class ArpgPlayer : CharacterBase
    {
        protected CreatureTable? _playerTable = null;
        private Input.ArpgInputAction.PlayerActions? _input = null;

        private PlayerData _playerData = null!;
        private Item.Inventory _inventory = new Item.Inventory();

        public override Tables.CreatureTable Table => _playerTable!;
        public Item.Inventory Inventory { get { return _inventory; } }
        
        public override void Initialize()
        {
            base.Initialize();

            _input = AR.s.UI.Input.Player;

            transform.position = new Vector3(0, 0, -0.1f); // Set initial position

            _inventory.Initialize(AR.s.Data.Player._inventory, AR.s.Data.Player._inventory.Count);

            Debug.Log("ArpgPlayer initialized.");
        }

        public override void InitializeECSComponents()
        {
            base.InitializeECSComponents();

            // 기본 스킬 생성 (스킬 ID 1)
            CreateSkill(0, 1); 

            // InputComponent 추가
            InputComponent inputComponent = new()
            {
                MoveDirection = Vector2.zero,
                MousePosition = Vector2.zero,
                IsAttacking = false,
                IsInteracting = false,
                IsSprinting = false
            };
            AR.s.Component.AddComponent(_entityId, inputComponent);

            // MapChunkLoaderComponent 추가
            MapChunkLoaderComponent mapChunkLoader = new()
            {
                CurrentChunk = new Vector2Int(-100000, -100000),
                LoadRadius = 2,
                IsInitialized = false
            };
            AR.s.Component.AddComponent(_entityId, mapChunkLoader);
        }

        public override void Reset()
        {
            base.Reset();
            // Reset player-specific state if needed
            Debug.Log("ArpgPlayer reset.");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            // 엔티티 ID 제거 (스킬 엔티티도 함께 제거됨)
            if (_entityId != 0)
            {
                EntityIdHelper.DestroyEntity(_entityId);
                Debug.Log($"ArpgPlayer entity {_entityId} destroyed");
            }
        }

        public override bool LoadTable(int inTableId)
        {
            _playerTable = AR.s.Data.GetPlayer(inTableId);
            if (_playerTable == null)
            {
                Debug.LogError($"[CharacterBase] LoadData - CreatureTable not found for Id: {inTableId}");
                return false;
            }

            return true;
        }

        public override bool Load(int inTableId)
        {
            if (base.Load(inTableId) == false)
                return false;

            if(AR.s?.Data?.Player == null)
            {
                Debug.LogError($"[ArpgPlayer] Load - AR.s.Data.Player is null, Id({inTableId})");
                return false;
            }

            _playerData = AR.s.Data.Player;
            _entityId = _playerData.PlayerId;

            return true;
        }

        public override float GetAttackSpeed()
        {
            // 플레이어는 장착하고 있는 무기의 공격 속도를 가지고 온다.
            ItemData? item = _playerData.GetEquipedItem(GlobalEnum.EquipSlotType.WeaponLeft);
            if(item == null)
                return 0f;
            
            if (item.Equipment == null)
            {
                Debug.LogError("[ArpgPlayer] GetAttackSpeed() - item?.Equipment is null");
                return 0f;
            }

            return item.Equipment.GetAttackSpeed();
        }

        public override (int, int) GetAttackDamage()
        {
            ItemData? item = _playerData.GetEquipedItem(GlobalEnum.EquipSlotType.WeaponLeft);
            if (item?.Equipment?.Table == null)
                return (0, 0);

            return (item.Equipment.Table.DamageMin, item.Equipment.Table.DamageMax);
        }


        public void Save(PlayerData inPlayerData)
        {
            if(AR.s.Component.TryGetComponent<StatComponent>(_entityId, out var statComponent) == false)
            {
                Debug.LogError("[ArpgPlayer] Save - StatComponent not found");
                return;
            }

            inPlayerData.CurrentHp = statComponent.CurrentHp;
            inPlayerData.CurrentMp = statComponent.CurrentMp;
        }

        // protected override void InitializeSkill()
        // {
        //     _skillController.Initialize(this, _gamePlayer?.SkillData?.SkillDatas);
        // }

        // private Vector2 CalculateSafeMovement(Vector2 inputDirection)
        // {
        //     if (AR.s == null || AR.s.Map == null || _characterInfo == null || _characterInfo.BoxCollider == null)
        //         return Vector2.zero;

        //     float deltaTime = Time.deltaTime;
        //     float desiredMoveDistance = MoveSpeed * deltaTime;
        //     Vector2 desiredMovement = inputDirection * desiredMoveDistance;

        //     // BoxCollider의 bounds 정보
        //     Bounds bounds = _characterInfo.BoxCollider.bounds;
        //     Vector2 currentCenter = bounds.center;
        //     Vector2 halfSize = bounds.size * 0.5f;
        //     float margin = 0.01f;

        //     // X축과 Y축 각각 안전한 이동 거리 계산
        //     float safeX = CalculateSafeDistance(desiredMovement.x, currentCenter, halfSize, margin, true);
        //     float safeY = CalculateSafeDistance(desiredMovement.y, currentCenter, halfSize, margin, false);

        //     return new Vector2(safeX, safeY);
        // }

        private float CalculateSafeDistance(float desiredMove, Vector2 center, Vector2 halfSize, float margin, bool isXAxis)
        {
            if (AR.s == null || AR.s.Map == null)
                return desiredMove;

            // 이동이 거의 없으면 그대로 반환
            if (Mathf.Abs(desiredMove) < 0.0001f)
                return desiredMove;

            int checkPoints = 3;
            float bestSafeDistance = desiredMove;

            // 이동 방향에 따라 체크할 엣지 결정
            if (isXAxis) // X축 이동
            {
                if (desiredMove > 0) // 오른쪽
                {
                    // 오른쪽 엣지 체크
                    for (int i = 0; i < checkPoints; i++)
                    {
                        float t = i / (float)(checkPoints - 1);
                        float y = center.y - halfSize.y + (halfSize.y * 2f * t);

                        // 현재 엣지 위치
                        float currentEdge = center.x + halfSize.x - margin;

                        // 목표 엣지 위치
                        float targetEdge = currentEdge + desiredMove;

                        // 목표 위치가 벽인지 체크
                        if (!AR.s.Map.IsWalkable(new Vector2(targetEdge, y)))
                        {
                            // 벽까지의 안전한 거리 계산
                            Vector3 wallCellCenter = AR.s.Map.GetCellCenterWorld(new Vector2(targetEdge, y));
                            float wallLeftEdge = wallCellCenter.x - 0.5f; // 타일 크기 1 가정
                            float safeDistance = wallLeftEdge - currentEdge - margin;

                            if (safeDistance < bestSafeDistance)
                            {
                                bestSafeDistance = Mathf.Max(0f, safeDistance);
                            }
                        }
                    }
                }
                else if (desiredMove < 0) // 왼쪽
                {
                    // 왼쪽 엣지 체크
                    for (int i = 0; i < checkPoints; i++)
                    {
                        float t = i / (float)(checkPoints - 1);
                        float y = center.y - halfSize.y + (halfSize.y * 2f * t);

                        float currentEdge = center.x - halfSize.x + margin;
                        float targetEdge = currentEdge + desiredMove;

                        if (!AR.s.Map.IsWalkable(new Vector2(targetEdge, y)))
                        {
                            Vector3 wallCellCenter = AR.s.Map.GetCellCenterWorld(new Vector2(targetEdge, y));
                            float wallRightEdge = wallCellCenter.x + 0.5f;
                            float safeDistance = wallRightEdge - currentEdge + margin;

                            if (safeDistance > bestSafeDistance)
                            {
                                bestSafeDistance = Mathf.Min(0f, safeDistance);
                            }
                        }
                    }
                }
            }
            else // Y축 이동
            {
                if (desiredMove > 0) // 위
                {
                    // 위쪽 엣지 체크
                    for (int i = 0; i < checkPoints; i++)
                    {
                        float t = i / (float)(checkPoints - 1);
                        float x = center.x - halfSize.x + (halfSize.x * 2f * t);

                        float currentEdge = center.y + halfSize.y - margin;
                        float targetEdge = currentEdge + desiredMove;

                        if (!AR.s.Map.IsWalkable(new Vector2(x, targetEdge)))
                        {
                            Vector3 wallCellCenter = AR.s.Map.GetCellCenterWorld(new Vector2(x, targetEdge));
                            float wallBottomEdge = wallCellCenter.y - 0.5f;
                            float safeDistance = wallBottomEdge - currentEdge - margin;

                            if (safeDistance < bestSafeDistance)
                            {
                                bestSafeDistance = Mathf.Max(0f, safeDistance);
                            }
                        }
                    }
                }
                else if (desiredMove < 0) // 아래
                {
                    // 아래쪽 엣지 체크
                    for (int i = 0; i < checkPoints; i++)
                    {
                        float t = i / (float)(checkPoints - 1);
                        float x = center.x - halfSize.x + (halfSize.x * 2f * t);

                        float currentEdge = center.y - halfSize.y + margin;
                        float targetEdge = currentEdge + desiredMove;

                        if (!AR.s.Map.IsWalkable(new Vector2(x, targetEdge)))
                        {
                            Vector3 wallCellCenter = AR.s.Map.GetCellCenterWorld(new Vector2(x, targetEdge));
                            float wallTopEdge = wallCellCenter.y + 0.5f;
                            float safeDistance = wallTopEdge - currentEdge + margin;

                            if (safeDistance > bestSafeDistance)
                            {
                                bestSafeDistance = Mathf.Min(0f, safeDistance);
                            }
                        }
                    }
                }
            }

            return bestSafeDistance;
        }
    }
}
 
