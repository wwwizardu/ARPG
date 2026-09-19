using UnityEngine;
using System.Collections;
using ARPG.Component;
using ARPG.Factory;
using Cysharp.Threading.Tasks;

namespace ARPG.Scene
{
    public class GameScene : Base.SceneBase
    {
        [SerializeField] private CameraController _cameraController;
        [SerializeField] private Transform _monsterRoot;
        [SerializeField] private Transform _npcRoot;
        [SerializeField] private Transform _buildingRoot;

        public Transform MonsterRoot => _monsterRoot;
        public Transform NpcRoot => _npcRoot;
        public Transform BuildingRoot => _buildingRoot;

        protected override IEnumerator OnInitialize()
        {
            yield return base.OnInitialize();

            // 매니저에 root transform 등록 — Map.CreateMap 이후 첫 spawn이 fire 되기 전에 반드시 세팅되어야 함
            AR.s.Monster.SetMorsterRoot(_monsterRoot);
            AR.s.Npc.SetNpcRoot(_npcRoot);
            AR.s.Building.SetBuildingRoot(_buildingRoot);

            // EntityFactory를 통해 플레이어 생성 (Addressable 비동기)
            yield return CreatePlayerAsync().ToCoroutine();

            AR.s.OnLoadSceneComplete(this);

            Debug.Log("GameScene initialized.");
        }

        private async UniTask CreatePlayerAsync()
        {
            // 저장된 위치에서 스폰 (신규 플레이어는 PositionX/Z = 0 → 원점)
            Vector3 spawnPos = Vector3.zero;
            var playerData = AR.s.Data?.Player;
            if (playerData != null)
            {
                spawnPos = new Vector3(playerData.PositionX, playerData.PositionZ, 0f);
            }

            var (playerEntityId, player) = await EntityFactory.CreatePlayer(1, spawnPos);
            if (playerEntityId < 0 || player == null)
            {
                Debug.LogError("Failed to create player entity.");
                return;
            }

            _cameraController.Initialize(player);
            AR.s.Player.AddPlayer(player);

            AR.s.WorldMap.CreateMap(AR.s.Data.WorldSeed, player.transform.position);
            if (await AR.s.Dungeon.RestoreSessionAsync() == false) return;
            if (AR.s.Dungeon.IsInside == false) AR.s.Item.RestoreContextDrops();

            // 맵 생성 완료 후 청크 로더 활성화
            if (AR.s.Component.TryGetComponent<MapChunkLoaderComponent>(playerEntityId, out var chunkLoader))
            {
                chunkLoader.IsInitialized = true;
                AR.s.Component.SetComponent(playerEntityId, chunkLoader);
            }

            // CreateMap이 초기 NPC SaveData 배치까지 동기적으로 마친 뒤 새 월드를 한 번 영속화한다.
            await AR.s.Data.SaveInitialWorldIfNeededAsync();
        }
    }
}
