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
        [SerializeField] private MapManager _mapManager;
        [SerializeField] private UIManager _uiManager;
        [SerializeField] private ARPG.Monster.MonsterManager _monsterManager;

        private bool _initialized = false;

        private Base.SceneBase? _currentScene;

        private PlayerManager _playerManager = new PlayerManager();
        
        public bool IsInitialized => _initialized;
        public MapManager Map => _mapManager;
        public UIManager UI => _uiManager;
        public Monster.MonsterManager Monster => _monsterManager;

        public PlayerManager Player => _playerManager;

        public ArpgPlayer? MyPlayer => _playerManager.MyPlayers;

        protected override void Awake()
        {
            base.Awake();

            Initialize();
        }

        public void Initialize()
        {
            // 초기화 로직
            _uiManager.Initialize();
            _mapManager.Initialize();
            _monsterManager.Initialize();

            _initialized = true;

            Debug.Log("AR Initialized");
        }

        public void Reset()
        {
            _uiManager.Reset();
            _mapManager.Reset();
            _monsterManager.Reset();

        }
        


        public void OnLoadSceneComplete(Base.SceneBase inNewScene)
        {
            _currentScene = inNewScene;

            if (_currentScene is Scene.LoginScene loginScene)
            {

            }
            else if (_currentScene is Scene.GameScene gameScene)
            {
                _monsterManager.SetMorsterRoot(gameScene.MonsterRoot);
            }
        }

        private void Update()
        {
            if (_initialized == false)
                return;

            float deltaTime = Time.deltaTime;

            _mapManager.UpdateMapManager(deltaTime);
            _monsterManager.UpdateMpnsterManager(deltaTime);
        }
    }
}




