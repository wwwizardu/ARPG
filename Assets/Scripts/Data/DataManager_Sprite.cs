using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.U2D;
using UnityEngine.AddressableAssets;

namespace ARPG.Data
{
    public partial class DataManager : MonoBehaviour
    {
        private SpriteAtlas _atlasBase = null;

        public Sprite GetSprite(string inName)
        {
            return _atlasBase.GetSprite(inName);
        }

        private async Task LoadBaseSpriteAtlas()
        {
            var handle = Addressables.LoadAssetAsync<SpriteAtlas>("Atlas/Base");
            _atlasBase = await handle.Task;
        }
    }
}


