namespace ARPG.Component
{
    /// <summary>
    /// 스탯 재계산이 필요함을 표시하는 태그 컴포넌트
    ///
    /// 사용 시나리오:
    /// 1. 장비를 착용/해제할 때 이 태그를 추가
    /// 2. 버프가 적용/제거될 때 이 태그를 추가
    /// 3. StatCalculationSystem이 이 태그가 있는 엔티티만 처리
    /// 4. 재계산 완료 후 태그 제거
    ///
    /// 이 패턴을 통해 변경이 없는 엔티티는 스킵하고,
    /// 변경된 엔티티만 효율적으로 재계산할 수 있습니다.
    /// </summary>
    public struct StatDirtyTag
    {
        // Tag Component는 데이터를 가지지 않음 (존재 여부만으로 의미 전달)
    }
}
