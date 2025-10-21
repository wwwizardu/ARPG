using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.U2D;
using UnityEngine.AddressableAssets;
using System.Collections.Generic;
using ARPG.Tables;
using UnityEngine.UI;

namespace ARPG.Data
{
    public partial class DataManager : MonoBehaviour
    {
        [Header("Icon")]
        [SerializeField] private GameObject _iconObject;
        [SerializeField] private Transform _iconObjectRoot;
        private SpriteAtlas _atlasBase = null;
        private SpriteAtlas _atlasIcon = null;

        private List<GameObject> _iconCache = new();

        public Sprite GetSprite(string inName)
        {
            return _atlasBase.GetSprite(inName);
        }

        public Sprite GetIcon(string inName)
        {
            return _atlasIcon.GetSprite(inName);
        }

        public GameObject GetIconPrefab(BuffEffect inBuffEffect)
        {
            if (_iconObject == null)
            {
                Debug.LogError("[DataManager] GetIconPrefab - _iconObject is null");
                return null;
            }

            GameObject iconObject;

            // 캐시에서 오브젝트 가져오기
            if (_iconCache.Count > 0)
            {
                iconObject = _iconCache[^1];
                _iconCache.RemoveAt(_iconCache.Count - 1);
            }
            else
            {
                iconObject = Instantiate(_iconObject, _iconObjectRoot);
            }

            Image iconImage = iconObject.GetComponent<Image>();
            if (iconImage == null)
            {
                Debug.LogError("[DataManager] GetIconPrefab - Image component not found on icon object");
                Destroy(iconObject);
                return null;
            }

            Sprite iconSprite = GetIcon(inBuffEffect.Type.ToString());
            if (iconSprite == null)
            {
                Debug.LogWarning($"[DataManager] GetIconPrefab - Icon sprite not found for BuffEffectType: {inBuffEffect.Type}");
                return null;
            }

            iconImage.sprite = iconSprite;
            iconObject.SetActive(true);
            return iconObject;
        }

        public void RestoreIconPrefab(GameObject inIconObject)
        {
            if (inIconObject == null)
            {
                Debug.LogWarning("[DataManager] RestoreIconPrefab - Icon object is null");
                return;
            }

            inIconObject.transform.SetParent(_iconObjectRoot);
            inIconObject.SetActive(false);
            _iconCache.Add(inIconObject);
        }

        private async Task LoadBaseSpriteAtlas()
        {
            var handle = Addressables.LoadAssetAsync<SpriteAtlas>("Atlas/Base");
            _atlasBase = await handle.Task;

            var handleIcon = Addressables.LoadAssetAsync<SpriteAtlas>("Atlas/Icon");
            _atlasIcon = await handleIcon.Task;
        }


    }
}


