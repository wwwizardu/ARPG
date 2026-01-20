using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// 기본 스탯과 최종 스탯을 관리하는 컴포넌트
    /// Base: 레벨업 등으로 결정되는 기본 스탯
    /// Final: 장비, 버프 등 모든 보정이 적용된 최종 스탯 (실제 사용값)
    /// </summary>
    public struct StatComponent
    {
        // === 기본 스탯 (Base Stats) ===
        // 레벨업이나 초기 설정으로 결정되는 값
        public int BaseStr;             // 기본 힘
        public int BaseDex;             // 기본 민첩
        public int BaseInt;             // 기본 지능
        public int BaseMaxHp;           // 기본 최대 체력
        public int BaseMaxMp;           // 기본 최대 마나
        public int BaseHpGeneration;    // 기본 체력 재생
        public int BaseMpGeneration;    // 기본 마나 재생
        public int BaseAttackMin;       // 기본 최소 공격력
        public int BaseAttackMax;       // 기본 최대 공격력
        public int BaseCriRate;         // 기본 치명타 확률
        public int BaseCriDamage;       // 기본 치명타 피해
        public int BaseMoveSpeed;       // 기본 이동 속도
        public int BaseAttackSpeed;     // 기본 공격 속도
        public int BaseCastSpeed;       // 기본 시전 속도
        public int BaseDefense;         // 기본 방어력
        public int BaseFireResist;      // 기본 화염 저항
        public int BaseIceResist;       // 기본 냉기 저항
        public int BaseLightningResist; // 기본 번개 저항
        public int BasePoisonResist;    // 기본 독 저항
        public int BaseLuck;            // 기본 행운
        public int BaseBloodingRate;    // 기본 출혈 확률
        public int BaseIgniteRate;      // 기본 점화 확률

        // === 최종 스탯 (Final Stats) ===
        // Base + 장비 + 버프 등 모든 보정이 적용된 최종값 (읽기 전용으로 사용)
        public int FinalStr;
        public int FinalDex;
        public int FinalInt;
        public int FinalMaxHp;
        public int FinalMaxMp;
        public int FinalHpGeneration;
        public int FinalMpGeneration;
        public int FinalAttackMin;
        public int FinalAttackMax;
        public int FinalCriRate;
        public int FinalCriDamage;
        public int FinalMoveSpeed;
        public int FinalAttackSpeed;
        public int FinalCastSpeed;
        public int FinalDefense;
        public int FinalFireResist;
        public int FinalIceResist;
        public int FinalLightningResist;
        public int FinalPoisonResist;
        public int FinalLuck;
        public int FinalBloodingRate;
        public int FinalIgniteRate;

        // === 현재 상태 값 ===
        // 전투 중 변동되는 값들
        private int _currentHp;       // 현재 체력 (SetCurrentHp로만 변경)
        public int CurrentMp;         // 현재 마나

        /// <summary>
        /// 현재 체력 (읽기 전용, 변경은 SetCurrentHp 사용)
        /// </summary>
        public readonly int CurrentHp => _currentHp;

        /// <summary>
        /// 현재 체력 설정 및 HpDirtyTag 추가
        /// </summary>
        /// <param name="entityId">엔티티 ID</param>
        /// <param name="value">설정할 체력 값</param>
        public void SetCurrentHp(int entityId, int value)
        {
            _currentHp = value;
            AR.s.Component.AddComponent(entityId, new HpDirtyTag());
        }

        /// <summary>
        /// 현재 체력 직접 설정 (초기화 전용, HpDirtyTag 추가 안함)
        /// </summary>
        /// <param name="value">설정할 체력 값</param>
        public void SetCurrentHpDirect(int value)
        {
            _currentHp = value;
        }

        /// <summary>
        /// StatTable로부터 기본 스탯을 초기화
        /// </summary>
        public void InitializeFromTable(ARPG.Tables.StatTable statTable)
        {
            BaseStr = statTable.Str;
            BaseDex = statTable.Dex;
            BaseInt = statTable.Int;
            BaseMaxHp = statTable.MaxHp;
            BaseMaxMp = statTable.MaxMp;
            BaseHpGeneration = statTable.HpGeneration;
            BaseMpGeneration = statTable.MpGeneration;
            BaseAttackMin = statTable.AttackMin;
            BaseAttackMax = statTable.AttackMax;
            BaseCriRate = statTable.CriRate;
            BaseCriDamage = statTable.CriDamage;
            BaseMoveSpeed = statTable.MoveSpeed;
            BaseAttackSpeed = statTable.AttackSpeed;
            BaseCastSpeed = statTable.CastSpeed;
            BaseDefense = statTable.Defense;
            BaseFireResist = statTable.FireResist;
            BaseIceResist = statTable.IceResist;
            BaseLightningResist = statTable.LightningResist;
            BasePoisonResist = statTable.PoisonResist;
            BaseLuck = statTable.Luck;
            BaseBloodingRate = statTable.BloodingRate;
            BaseIgniteRate = statTable.IgniteRate;

            // 초기에는 Final = Base로 설정
            CopyBaseToFinal();

            // 현재 체력/마나를 최대값으로 초기화
            SetCurrentHpDirect(FinalMaxHp);
            CurrentMp = FinalMaxMp;
        }

        /// <summary>
        /// Base 스탯을 Final로 복사 (재계산 전 초기화용)
        /// </summary>
        public void CopyBaseToFinal()
        {
            FinalStr = BaseStr;
            FinalDex = BaseDex;
            FinalInt = BaseInt;
            FinalMaxHp = BaseMaxHp;
            FinalMaxMp = BaseMaxMp;
            FinalHpGeneration = BaseHpGeneration;
            FinalMpGeneration = BaseMpGeneration;
            FinalAttackMin = BaseAttackMin;
            FinalAttackMax = BaseAttackMax;
            FinalCriRate = BaseCriRate;
            FinalCriDamage = BaseCriDamage;
            FinalMoveSpeed = BaseMoveSpeed;
            FinalAttackSpeed = BaseAttackSpeed;
            FinalCastSpeed = BaseCastSpeed;
            FinalDefense = BaseDefense;
            FinalFireResist = BaseFireResist;
            FinalIceResist = BaseIceResist;
            FinalLightningResist = BaseLightningResist;
            FinalPoisonResist = BasePoisonResist;
            FinalLuck = BaseLuck;
            FinalBloodingRate = BaseBloodingRate;
            FinalIgniteRate = BaseIgniteRate;
        }
    }
}