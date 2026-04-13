#nullable enable
using System;
using GE = GlobalEnum;

namespace ARPG.Component
{
    /// <summary>
    /// 캐릭터 엔티티에 장착된 장비의 OnCalculate/OnEvent Mod를 보관하는 컴포넌트
    /// Passive Mod는 StatModifier로 변환되므로 여기에 포함되지 않음
    /// </summary>
    public struct ModPoolComponent
    {
        public const int MAX_ACTIVE_MODS = 32;

        public int Count;
        public ActiveMod Mod0, Mod1, Mod2, Mod3, Mod4, Mod5, Mod6, Mod7;
        public ActiveMod Mod8, Mod9, Mod10, Mod11, Mod12, Mod13, Mod14, Mod15;
        public ActiveMod Mod16, Mod17, Mod18, Mod19, Mod20, Mod21, Mod22, Mod23;
        public ActiveMod Mod24, Mod25, Mod26, Mod27, Mod28, Mod29, Mod30, Mod31;

        public bool Add(ActiveMod mod)
        {
            if (Count >= MAX_ACTIVE_MODS)
                return false;

            SetByIndex(Count, mod);
            Count++;
            return true;
        }

        /// <summary>
        /// 특정 아이템의 모든 Mod 제거
        /// </summary>
        public int RemoveBySource(int itemInstanceId)
        {
            int removed = 0;
            for (int i = Count - 1; i >= 0; i--)
            {
                if (GetByIndex(i).SourceItemInstanceId == itemInstanceId)
                {
                    // 마지막 요소와 교체 후 Count 감소
                    if (i < Count - 1)
                    {
                        SetByIndex(i, GetByIndex(Count - 1));
                    }
                    Count--;
                    removed++;
                }
            }
            return removed;
        }

        public ActiveMod GetByIndex(int index)
        {
            switch (index)
            {
                case 0: return Mod0;   case 1: return Mod1;   case 2: return Mod2;   case 3: return Mod3;
                case 4: return Mod4;   case 5: return Mod5;   case 6: return Mod6;   case 7: return Mod7;
                case 8: return Mod8;   case 9: return Mod9;   case 10: return Mod10; case 11: return Mod11;
                case 12: return Mod12; case 13: return Mod13; case 14: return Mod14; case 15: return Mod15;
                case 16: return Mod16; case 17: return Mod17; case 18: return Mod18; case 19: return Mod19;
                case 20: return Mod20; case 21: return Mod21; case 22: return Mod22; case 23: return Mod23;
                case 24: return Mod24; case 25: return Mod25; case 26: return Mod26; case 27: return Mod27;
                case 28: return Mod28; case 29: return Mod29; case 30: return Mod30; case 31: return Mod31;
                default: return default;
            }
        }

        private void SetByIndex(int index, ActiveMod value)
        {
            switch (index)
            {
                case 0: Mod0 = value; break;   case 1: Mod1 = value; break;   case 2: Mod2 = value; break;   case 3: Mod3 = value; break;
                case 4: Mod4 = value; break;   case 5: Mod5 = value; break;   case 6: Mod6 = value; break;   case 7: Mod7 = value; break;
                case 8: Mod8 = value; break;   case 9: Mod9 = value; break;   case 10: Mod10 = value; break; case 11: Mod11 = value; break;
                case 12: Mod12 = value; break; case 13: Mod13 = value; break; case 14: Mod14 = value; break; case 15: Mod15 = value; break;
                case 16: Mod16 = value; break; case 17: Mod17 = value; break; case 18: Mod18 = value; break; case 19: Mod19 = value; break;
                case 20: Mod20 = value; break; case 21: Mod21 = value; break; case 22: Mod22 = value; break; case 23: Mod23 = value; break;
                case 24: Mod24 = value; break; case 25: Mod25 = value; break; case 26: Mod26 = value; break; case 27: Mod27 = value; break;
                case 28: Mod28 = value; break; case 29: Mod29 = value; break; case 30: Mod30 = value; break; case 31: Mod31 = value; break;
            }
        }
    }

    /// <summary>
    /// ModPool에 저장되는 개별 Mod 데이터
    /// </summary>
    public struct ActiveMod
    {
        public int SourceItemInstanceId;       // 출처 아이템 (해제 시 제거용)
        public int ModTableId;                 // ModTable 참조
        public GE.ModEffectType EffectType;    // 효과 종류 (조회 시 빠른 필터링)
        public GE.ModApplyType ApplyType;      // Passive/OnCalculate/OnEvent
        public GE.DamageType Element;          // 속성
        public GE.SkillTag Tags;               // 적용 조건
        public ushort Value1;
        public ushort Value2;
    }
}
