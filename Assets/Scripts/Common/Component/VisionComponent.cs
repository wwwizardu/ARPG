namespace ARPG.Component
{
    /// <summary>
    /// 시야를 주는 엔티티(현재는 플레이어만)에 붙는다. System_FogVision이 이 반경 안에서 벽에 가리지 않은 칸을 계산한다
    /// (FOG_OF_WAR_DESIGN §4). 나중에 장비 옵션으로 시야를 늘리면 이 값만 스탯에서 채운다.
    /// </summary>
    public struct VisionComponent
    {
        public float Radius;   // 칸(= 유닛) 단위
    }
}
