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

        [SerializeField] private Item.ItemManager _itemManager;
        [SerializeField] private Manager.LLMManager _llmManager;
        [SerializeField] private Manager.TimeManager _timeManager;
        [SerializeField] private Village.VillageManager _villageManager;

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
        public Item.ItemManager Item => _itemManager;
        public Manager.MessageManager Message => _messageManager;

        public Manager.LLMManager LLM => _llmManager;
        public Manager.TimeManager Time => _timeManager;
        public Village.VillageManager Village => _villageManager;
        public PlayerManager Player => _playerManager;

        public Base.EntityBase? MyPlayer => _playerManager.MyPlayers;

        protected override void Awake()
        {
            base.Awake();

            Initialize();
        }

        public async void Initialize()
        {
            // 초기화 로직
            await _dataManager.Initialize();
            _componentManager.Initialize();
            _systemManager.Initialize();
            _uiManager.Initialize();
            _mapManager.Initialize();
            _itemManager.Initialize();
            _monsterManager.Initialize();
            _npcManager.Initialize();
            _messageManager.Initialize();
            _llmManager.Initialize();
            _timeManager.Initialize();
            _villageManager.Initialize();

            _initialized = true;

            Debug.Log("AR Initialized");
        }

        public void Reset()
        {
            _dataManager.Reset();
            _uiManager.Reset();
            _mapManager.Reset();
            _monsterManager.Reset();
            _npcManager.Reset();
            _messageManager.Reset();
            _timeManager.Reset();
            _villageManager.Reset();
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




