namespace ARPG.Component
{
    /// <summary>
    /// HP가 변경되었음을 표시하는 태그 컴포넌트
    ///
    /// 사용 시나리오:
    /// 1. StatComponent.SetCurrentHp() 호출 시 자동으로 추가
    /// 2. System_HpCheck에서 이 태그가 있는 엔티티만 체크
    /// 3. 체크 완료 후 태그 제거
    /// </summary>
    public struct HpDirtyTag
    {
        // Tag Component는 데이터를 가지지 않음 (존재 여부만으로 의미 전달)
    }
}
