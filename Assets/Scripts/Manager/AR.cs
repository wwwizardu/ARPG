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
        [SerializeField] private Manager.TooltipManager _tooltipManager;

        private bool _initialized = false;

        private Base.SceneBase? _currentScene;

        private PlayerManager _playerManager = new PlayerManager();
        private PlayerSkillManager _playerSkillManager = new PlayerSkillManager();

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
        public PlayerSkillManager PlayerSkill => _playerSkillManager;
        public TooltipManager Tooltip => _tooltipManager;

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

            // 세이브 데이터 로드 — DataManager가 WorldData deserialize 직후 saved 영구 EntityId(NPC/Building)를
            // EntityIdHelper에 사전 예약하므로, 이후 단계의 CreateEntity()는 saved ID와 충돌하지 않음.
            await _dataManager.Initialize();

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
            _tooltipManager.Initialize();

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
            _buildingManager.Reset();
            _messageManager.Reset();
            _timeManager.Reset();
            _villageManager.Reset();
            _floatingTextManager.Reset();
            _tooltipManager.Reset();
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

            // Scene 별 root transform 등록은 각 Scene이 OnInitialize 단계에서 직접 매니저에 알려준다 — Map.CreateMap 이후 spawn이 fire 되기 전에 반드시 세팅되어야 하기 때문.
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




