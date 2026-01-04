#nullable enable
using ARPG.Tables;

namespace ARPG.Component
{
    /// <summary>
    /// 스킬 실행 타입
    /// </summary>
    public enum SkillExecutionType : byte
    {
        /// <summary>단발성 스킬 - 한 번만 효과 발동</summary>
        Single,

        /// <summary>다단히트 스킬 - 일정 간격으로 여러 번 타격</summary>
        MultiHit,

        /// <summary>채널링 스킬 - 입력을 누르고 있는 동안 지속적으로 효과 발동</summary>
        Channeling,

        /// <summary>토글 스킬 - ON/OFF 전환 (버프 등)</summary>
        Toggle,

        /// <summary>차징 스킬 - 누르는 시간에 따라 효과 강도 변화</summary>
        Charge
    }

    /// <summary>
    /// 스킬의 기본 데이터를 저장하는 컴포넌트
    /// 이 컴포넌트는 스킬 엔티티에 붙으며, 각 스킬은 독립적인 엔티티로 관리됨
    /// </summary>
    public struct SkillComponent
    {
        /// <summary>스킬 테이블 ID</summary>
        public int SkillId;

        /// <summary>이 스킬을 소유한 캐릭터 엔티티 ID</summary>
        public int OwnerEntityId;

        /// <summary>스킬 슬롯 인덱스 (0부터 시작)</summary>
        public int SlotIndex;

        /// <summary>스킬 테이블 데이터</summary>
        public SkillTable? Table;

        /// <summary>스킬이 초기화되었는지 여부</summary>
        public bool IsInitialized;

        /// <summary>스킬 실행 가능 여부</summary>
        public bool IsEnabled;

        /// <summary>스킬 실행 타입</summary>
        public SkillExecutionType ExecutionType;

        // === MultiHit 타입용 필드 ===
        /// <summary>다단히트 총 타격 횟수</summary>
        public int HitCount;

        /// <summary>다단히트 타격 간격 (초)</summary>
        public float HitInterval;

        /// <summary>현재 몇 번째 히트인지 (0부터 시작)</summary>
        public int CurrentHitIndex;

        // === Channeling 타입용 필드 ===
        /// <summary>채널링 효과 적용 간격 (초)</summary>
        public float ChannelingInterval;

        /// <summary>마지막 채널링 효과 적용 시간</summary>
        public float LastChannelingTime;

        // === Charge 타입용 필드 ===
        /// <summary>최대 차징 시간 (초)</summary>
        public float MaxChargeTime;

        /// <summary>현재 차징 시간</summary>
        public float CurrentChargeTime;

        /// <summary>최소 차징 비율 (0.0 ~ 1.0)</summary>
        public float MinChargeRatio;

        // === Toggle 타입용 필드 ===
        /// <summary>토글이 활성화 상태인지</summary>
        public bool IsToggleActive;

        /// <summary>
        /// 스킬 데이터를 초기화
        /// </summary>
        public void ResetRuntimeData()
        {
            CurrentHitIndex = 0;
            LastChannelingTime = 0f;
            CurrentChargeTime = 0f;
            IsToggleActive = false;
        }
    }
}
