#nullable enable
using ARPG;
using ARPG.Creature;
using ARPG.Map;
using ARPG.Manager;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG
{
    public class AR : PrefabSingleton<AR>
    {
        [SerializeField] private Data.DataManager _dataManager;
        [SerializeField] private MapManager _mapManager;
        [SerializeField] private UIManager _uiManager;
        [SerializeField] private ARPG.Monster.MonsterManager _monsterManager;

        [SerializeField] private Item.ItemManager _itemManager;

        private bool _initialized = false;

        private Base.SceneBase? _currentScene;

        private PlayerManager _playerManager = new PlayerManager();
        
        public bool IsInitialized => _initialized;

        public Base.SceneBase? CurrentScene => _currentScene;

        public Data.DataManager Data => _dataManager;
        public MapManager Map => _mapManager;
        public UIManager UI => _uiManager;
        public Monster.MonsterManager Monster => _monsterManager;
        public Item.ItemManager Item => _itemManager;

        public PlayerManager Player => _playerManager;

        public ArpgPlayer? MyPlayer => _playerManager.MyPlayers;

        protected override void Awake()
        {
            base.Awake();

            Initialize();
        }

        public async void Initialize()
        {
            // 초기화 로직
            await _dataManager.Initialize();
            _uiManager.Initialize();
            _mapManager.Initialize();
            _itemManager.Initialize();
            _monsterManager.Initialize();
            
            _initialized = true;

            Debug.Log("AR Initialized");
        }

        public void Reset()
        {
            _dataManager.Reset();
            _uiManager.Reset();
            _mapManager.Reset();
            _monsterManager.Reset();

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
                float deltaTime = Time.deltaTime;
                
                _mapManager.UpdateMapManager(deltaTime);
                _monsterManager.UpdateMpnsterManager(deltaTime);                
            }
        }
    }
}




