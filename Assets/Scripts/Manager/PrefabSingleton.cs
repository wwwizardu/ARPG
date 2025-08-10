using UnityEngine;

/// <summary>
/// 유니티 Prefab용 싱글톤 함수
/// </summary>
public abstract class PrefabSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static bool _appIsClosing = false;
    private static T _instance = null;
    private static object _lock = new object();

    public static T Instance
    {
        get
        {
            if (Application.isPlaying && _appIsClosing)
            {
                return null;
            }

            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<T>();

                    if (_instance == null)
                    {
                        _instance = Instantiate(Resources.Load<T>(typeof(T).Name));
                        if (_instance == null)
                        {
                            GameObject go = new GameObject();
                            _instance = go.AddComponent<T>();

                            Debug.LogError($"[PrefabSingleton] Instance - wrong type({typeof(T)})");
                        }
                    }

                    _instance.gameObject.name = "_" + typeof(T).ToString();

                    if (Application.isPlaying)
                    {
                        DontDestroyOnLoad(_instance.gameObject);
                    }

                    UnityEngine.Debug.Log($"[{_instance.name}] PrefabSingleton created!");
                }

                return _instance;
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////

    protected virtual void Awake()
    {
        if (_instance != null && _instance != this)
        {
            GameObject.Destroy(gameObject);
            return;
        }

        //if (_instance == null)
        //{
        //    // Instance 를 호출하여 생성
        //    if (Instance == null)
        //    {
        //        Debug.Log($"[{nameof(T)}] Instance == null!");
        //    }
        //    return;
        //}

        OnAwake();
    }

    protected virtual void OnApplicationQuit()
    {
        _appIsClosing = true;
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    protected virtual void OnAwake() { }
}
