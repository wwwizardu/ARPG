using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "CustomTile", menuName = "ARPG/Custom Tile")]
public class CustomTile : TileBase
{
    [SerializeField] private Sprite _sprite;
    [SerializeField] private uint _customData;

    public Sprite Sprite => _sprite;
    public uint CustomData => _customData;

    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        tileData.transform = Matrix4x4.identity;
        tileData.flags = TileFlags.LockTransform;
        tileData.colliderType = Tile.ColliderType.Sprite;
        tileData.sprite = _sprite;
    }

    public void SetCustomData(uint data)
    {
        _customData = data;
    }

    public void SetSprite(Sprite sprite)
    {
        _sprite = sprite;
    }
}