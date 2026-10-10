namespace ARPG.Map
{
    /// <summary>
    /// 시야를 막는 칸의 종류 (FOG_OF_WAR_DESIGN §4). MapManager.GetVisionBlocker가 IsTileBlocked와 같은 규칙으로 나눈다.
    /// 벽 뒤는 밝기 35%, 시설 뒤는 50%의 반투명 그림자로 가린다. 둘 다 그 뒤의 이동 엔티티를 숨긴다.
    /// </summary>
    public enum VisionBlocker : byte
    {
        None = 0,
        Wall = 1,       // 맵 밖, 타일 Blocked 비트 (던전 벽, 마을 성벽·닫힌 문, 막힌 타일 오브젝트)
        Facility = 2,   // 월드 건물 점유 칸 (침대·모루·노점 같은 마을 시설)
    }
}
