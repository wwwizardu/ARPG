#nullable enable
using ARPG.Map;
using ARPG.Manager;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG
{
    public class AR : PrefabSingleton<AR>
    {
        [SerializeField] private Manager.MessageManager _messageManager;
        [SerializeField] private Systems.SystemManager _systemManager;
        [SerializeField] private Component.ComponentManager _componentManager;
        [SerializeField] private Data.DataManager _dataManager;
        [SerializeField] private MapManager _mapManager;
        [SerializeField] private UIManager _uiManager;
        [SerializeField] private ARPG.Monster.MonsterManager _monsterManager;
        [SerializeField] private ARPG.Npc.NpcManager _npcManager;
        [SerializeField] private ARPG.Building.BuildingManager _buildingManager;

        [SerializeField] private Item.ItemManager _itemManager;
        [SerializeField] private Manager.LLMManager _llmManager;
        [SerializeField] private Manager.TimeManager _timeManager;
        [SerializeField] private Village.VillageManager _villageManager;
        [SerializeField] private UI.FloatingTextManager _floatingTextManager;

        private bool _initialized = false;

        private Base.SceneBase? _currentScene;

        private PlayerManager _playerManager = new PlayerManager();
        
        public bool IsInitialized => _initialized;

        public Base.SceneBase? CurrentScene => _currentScene;

        public Systems.SystemManager System => _systemManager;
        public Component.ComponentManager Component => _componentManager;
        public Data.DataManager Data => _dataManager;
        public MapManager Map => _mapManager;
        public UIManager UI => _uiManager;
        public Monster.MonsterManager Monster => _monsterManager;
        public Npc.NpcManager Npc => _npcManager;
        public Building.BuildingManager Building => _buildingManager;
        public Item.ItemManager Item => _itemManager;
        public Manager.MessageManager Message => _messageManager;

        public Manager.LLMManager LLM => _llmManager;
        public Manager.TimeManager Time => _timeManager;
        public Village.VillageManager Village => _villageManager;
        public UI.FloatingTextManager FloatingText => _floatingTextManager;
        public PlayerManager Player => _playerManager;

        public Base.EntityBase? MyPlayer => _playerManager.MyPlayers;

        protected override void Awake()
        {
            base.Awake();

            Initialize();
        }

        public async void Initialize()
        {
            // 가장 먼저: EntityId 시스템을 깨끗한 상태로 리셋. 다른 어떤 매니저도 CreateEntity 호출하기 전.
            ARPG.Utility.EntityIdHelper.Initialize();

            // 세이브 데이터 로드 (NpcSaveDatas, BuildingSaveDatas 포함)
            await _dataManager.Initialize();

            // 영구 EntityId 사전 예약 — 다른 매니저들이 CreateEntity 호출하기 전에 saved IDs를 등록.
            // 등록된 ID는 CreateEntity 탐색 시 자동 건너뜀 → 신규 엔티티가 saved ID와 충돌 안 함.
            // ★ 새로운 영구 EntityId 매니저가 추가되면 PreReserveSavedEntityIds에 등록 추가 필수.
            PreReserveSavedEntityIds();

            _componentManager.Initialize();
            _systemManager.Initialize();
            _uiManager.Initialize();
            _mapManager.Initialize();
            _itemManager.Initialize();
            _monsterManager.Initialize();
            _npcManager.Initialize();
            _buildingManager.Initialize();
            _messageManager.Initialize();
            _llmManager.Initialize();
            _timeManager.Initialize();
            _villageManager.Initialize();
            _floatingTextManager.Initialize();

            _initialized = true;

            Debug.Log("AR Initialized");
        }

        /// <summary>
        /// 세이브 데이터의 모든 영구 EntityId를 EntityIdHelper에 사전 등록한다.
        /// 다른 매니저들이 CreateEntity()로 신규 엔티티를 발급받기 전에 호출되어야 함 — 그래야
        /// CreateEntity가 saved ID와 충돌 없이 미등록 ID를 자동 탐색.
        ///
        /// 새로운 영구 EntityId를 가진 매니저가 추가될 때마다 여기에도 등록해야 한다.
        /// 누락 시 ID 충돌이 silent하게 발생할 수 있음 (RegisterExistingEntity가 LogError로 알려줌).
        /// </summary>
        private void PreReserveSavedEntityIds()
        {
            if (_dataManager.NpcSaveDatas != null)
            {
                foreach (var kvp in _dataManager.NpcSaveDatas)
                    ARPG.Utility.EntityIdHelper.RegisterExistingEntity(kvp.Key);
            }
            if (_dataManager.BuildingSaveDatas != null)
            {
                foreach (var kvp in _dataManager.BuildingSaveDatas)
                    ARPG.Utility.EntityIdHelper.RegisterExistingEntity(kvp.Key);
            }
        }

        public void Reset()
        {
            _dataManager.Reset();
            _uiManager.Reset();
            _mapManager.Reset();
            _monsterManager.Reset();
            _npcManager.Reset();
            _buildingManager.Reset();
            _messageManager.Reset();
            _timeManager.Reset();
            _villageManager.Reset();
            _floatingTextManager.Reset();
        }
        
        public void OnSceneLoadStart(Base.SceneBase inScene)
        {
            _currentScene = inScene;
        }
        
        public void OnLoadSceneComplete(Base.SceneBase inNewScene)
        {
            if (_currentScene == null)
            {
                Debug.LogError("[AR] OnLoadSceneComplete() - CurrentScene is null");
                return;
            }

            if (_currentScene.CurrentSceneType == Base.SceneBase.SceneType.Login)
            {

            }
            else if (_currentScene.CurrentSceneType == Base.SceneBase.SceneType.Game)
            {
                if (_currentScene is Scene.GameScene gameScene)
                {
                    _monsterManager.SetMorsterRoot(gameScene.MonsterRoot);
                    _npcManager.SetNpcRoot(gameScene.NpcRoot);
                    _buildingManager.SetBuildingRoot(gameScene.BuildingRoot);
                }
            }
        }

        private void Update()
        {
            if (_initialized == false)
                return;

            if(_currentScene == null)
                return;

            if (_currentScene.CurrentSceneType == Base.SceneBase.SceneType.Game)
            {
                // 몬스터 제거/활성화는 ECS 시스템(System_EntityDestroy, System_EntityActivation)이 처리
            }
        }
    }
}




