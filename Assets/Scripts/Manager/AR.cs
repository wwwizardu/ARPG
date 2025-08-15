using ARPG;
using ARPG.Map;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class AR : PrefabSingleton<AR>
{
    [SerializeField] private MapManager _mapManager;
    [SerializeField] private UIManager _uiManager;

    private bool _initialized = false;

    public bool IsInitialized => _initialized;
    public MapManager Map => _mapManager;
    public UIManager UI => _uiManager;

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

        _initialized = true;

        Debug.Log("AR Initialized");
    }

    public void Reset()
    {
        _uiManager.Reset();
        _mapManager.Reset();
    }
}
