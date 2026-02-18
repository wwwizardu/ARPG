using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "CustomTile", menuName = "ARPG/Custom Tile")]
public class CustomTile : TileBase
{
    [SerializeField] private Sprite _sprite;
    [SerializeField] private GlobalEnum.TileLayer _layer;
    [SerializeField] private uint _customData;
    [SerializeField] private bool _isWalkable = true;

    public Sprite Sprite => _sprite;
    public GlobalEnum.TileLayer Layer => _layer;
    public uint CustomData => _customData;
    public bool IsWalkable => _isWalkable;

    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        tileData.transform = Matrix4x4.identity;
        tileData.flags = TileFlags.LockTransform;
        tileData.sprite = _sprite;

        if(_layer == GlobalEnum.TileLayer.Ground)
        {
            tileData.colliderType = Tile.ColliderType.Sprite;    
        }
        else
        {
            tileData.colliderType = Tile.ColliderType.Grid;
        }
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