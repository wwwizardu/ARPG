namespace ARPG.Component
{
    /// <summary>
    /// AI가 현재 타겟을 시야에서 감지하고 있음을 표시하는 태그 컴포넌트
    ///
    /// 사용 시나리오:
    /// 1. System_AI_Perception이 타겟 감지 시 이 태그를 추가
    /// 2. 타겟을 잃으면 이 태그를 제거
    /// 3. 의사결정 시스템이나 행동 시스템에서 이 태그 존재 여부로 빠르게 체크
    ///
    /// 장점:
    /// - AIComponent를 읽지 않고도 태그 존재만으로 "타겟 감지 중" 상태 확인 가능
    /// - 성능상 이점 (HasComponent는 O(1) 연산)
    /// </summary>
    public struct AICanSeeTargetTag
    {
        // Tag Component는 데이터를 가지지 않음 (존재 여부만으로 의미 전달)
    }
}
