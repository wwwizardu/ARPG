#nullable enable

namespace ARPG.Component
{
    /// <summary>
    /// 장비 인스턴스 컴포넌트
    /// 장비 아이템의 고유 데이터 (품질, 접두사/접미사 스탯)를 저장
    /// 인벤토리 슬롯과 별도 엔티티로 관리
    /// </summary>
    public struct EquipmentInstanceComponent
    {
        /// <summary>
        /// 소속 슬롯 엔티티 ID
        /// </summary>
        public int SlotEntityId;

        /// <summary>
        /// 장비 품질 (1-100)
        /// </summary>
        public int Quality;

        /// <summary>
        /// 강화 레벨
        /// </summary>
        public int EnhanceLevel;

        // 접두사 스탯 (최대 3개)
        public GlobalEnum.Stat PrefixStat1Type;
        public int PrefixStat1Value;
        public GlobalEnum.Stat PrefixStat2Type;
        public int PrefixStat2Value;
        public GlobalEnum.Stat PrefixStat3Type;
        public int PrefixStat3Value;

        // 접미사 스탯 (최대 3개)
        public GlobalEnum.Stat SuffixStat1Type;
        public int SuffixStat1Value;
        public GlobalEnum.Stat SuffixStat2Type;
        public int SuffixStat2Value;
        public GlobalEnum.Stat SuffixStat3Type;
        public int SuffixStat3Value;

        /// <summary>
        /// 접두사 스탯 개수 반환
        /// </summary>
        public readonly int GetPrefixStatCount()
        {
            int count = 0;
            if (PrefixStat1Value != 0) count++;
            if (PrefixStat2Value != 0) count++;
            if (PrefixStat3Value != 0) count++;
            return count;
        }

        /// <summary>
        /// 접미사 스탯 개수 반환
        /// </summary>
        public readonly int GetSuffixStatCount()
        {
            int count = 0;
            if (SuffixStat1Value != 0) count++;
            if (SuffixStat2Value != 0) count++;
            if (SuffixStat3Value != 0) count++;
            return count;
        }

        /// <summary>
        /// 장비 데이터 초기화
        /// </summary>
        public void Clear()
        {
            SlotEntityId = 0;
            Quality = 0;
            EnhanceLevel = 0;
            PrefixStat1Type = default;
            PrefixStat1Value = 0;
            PrefixStat2Type = default;
            PrefixStat2Value = 0;
            PrefixStat3Type = default;
            PrefixStat3Value = 0;
            SuffixStat1Type = default;
            SuffixStat1Value = 0;
            SuffixStat2Type = default;
            SuffixStat2Value = 0;
            SuffixStat3Type = default;
            SuffixStat3Value = 0;
        }
    }
}
