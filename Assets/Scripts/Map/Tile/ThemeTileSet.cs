using UnityEngine;
using UnityEngine.Tilemaps;

// 테마별 타일 세트 정의
// TileType과 TileSet의 인덱스가 일치해야 함
public class ThemeTileSet : ScriptableObject
{
    [Header("Theme Info")]
    public string themeName;
    public Sprite themeIcon;

    [Header("Tiles")]
    public TileBase[] TileSet;

    [Header("Object")]
    public TileBase[] ObjectSet;

}
