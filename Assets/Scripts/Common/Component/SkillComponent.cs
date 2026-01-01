#nullable enable
using ARPG.Tables;

namespace ARPG.Component
{
    /// <summary>
    /// 스킬의 기본 데이터를 저장하는 컴포넌트
    /// </summary>
    public struct SkillComponent
    {
        /// <summary>스킬 ID</summary>
        public int Id;

        /// <summary>스킬 테이블 데이터</summary>
        public SkillTable? Table;

        /// <summary>스킬이 초기화되었는지 여부</summary>
        public bool IsInitialized;

        /// <summary>스킬 실행 가능 여부</summary>
        public bool IsEnabled;
    }
}
