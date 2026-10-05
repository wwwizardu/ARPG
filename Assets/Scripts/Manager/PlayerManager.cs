#nullable enable
using System.Collections.Generic;
using ARPG.Base;
using ARPG.Component;
using ARPG.Data;
using UnityEngine;

namespace ARPG.Manager
{
    public class PlayerManager
    {
        private List<EntityBase> _players = new();
        private EntityBase? _myPlayer = null;

        private PlayerData _playerData = null!;
        private Item.Inventory _inventory = new Item.Inventory();

        public EntityBase? MyPlayers => _myPlayer;
        public Item.Inventory Inventory => _inventory;

        public void AddPlayer(EntityBase player)
        {
            if (player == null)
                return;

            if (_players.Contains(player) == false)
            {
                _players.Add(player);
            }

            _myPlayer = player;
        }

        public List<EntityBase> GetAllPlayers()
        {
            return _players;
        }

        /// <summary>
        /// PlayerData 연결 및 인벤토리 초기화
        /// EntityFactory.CreatePlayer에서 호출
        /// </summary>
        public void InitializePlayerData()
        {
            if (AR.s?.Data?.Player == null)
            {
                Debug.LogError("[PlayerManager] InitializePlayerData - AR.s.Data.Player is null");
                return;
            }

            _playerData = AR.s.Data.Player;
            _inventory.Initialize(_playerData._inventory, _playerData._inventory.Count);
            _inventory.Changed = () => AR.s.Data.MarkPlayerSaveDirty(_playerData, PlayerSaveGroup.Inventory);
        }

        /// <summary>
        /// PlayerData에서 저장된 EntityId를 반환
        /// </summary>
        public int GetSavedPlayerId()
        {
            if (AR.s?.Data?.Player == null)
                return -1;

            return AR.s.Data.Player.PlayerId;
        }

        /// <summary>
        /// StatComponent에서 현재 HP/MP를, TransformComponent에서 현재 위치를 PlayerData에 저장
        /// </summary>
        public void Save(PlayerData inPlayerData)
        {
            if (AR.s.Dungeon.HasPlayerSaveOverride) return;
            if (_myPlayer == null)
                return;

            if (AR.s.Component.TryGetComponent<StatComponent>(_myPlayer.EntityId, out var statComponent) == false)
            {
                Debug.LogError("[PlayerManager] Save - StatComponent not found");
                return;
            }

            AR.s.Data.MarkPlayerSaveDirty(inPlayerData, PlayerSaveGroup.Vitality | PlayerSaveGroup.Location);
            inPlayerData.CurrentHp = statComponent.CurrentHp;
            inPlayerData.CurrentMp = statComponent.CurrentMp;

            // 현재 위치 저장 (권위 소스는 TransformComponent — System_Render가 transform과 동기화)
            if (AR.s.Component.TryGetComponent<TransformComponent>(_myPlayer.EntityId, out var transformComponent) == true)
            {
                if (AR.s.Dungeon.IsInside)
                {
                    inPlayerData.DungeonPositionX = transformComponent.Position.x;
                    inPlayerData.DungeonPositionY = transformComponent.Position.y;
                }
                else
                {
                    inPlayerData.PositionX = transformComponent.Position.x;
                    inPlayerData.PositionZ = transformComponent.Position.y;
                }
            }
        }
    }
}
