using UnityEngine;
using UnityEngine.Tilemaps;

// 청사진(.bytes)이 깐 ground 셀의 인덱스 → TileBase 직접 룩업용 통합 자산.
// 인덱스 0은 비워둔다(passthrough — 청사진이 ground를 지정하지 않은 셀, cluster biome로 fallback).
// 인덱스 1부터 실제 ground 타일을 채운다.
[CreateAssetMenu(fileName = "GroundTileSet", menuName = "ARPG/Ground Tile Set")]
public class GroundTileSet : ScriptableObject
{
    [Tooltip("인덱스 = Ground Tile Id. 0번 슬롯은 비워둘 것 (0 = passthrough).")]
    public TileBase[] tiles;
}
