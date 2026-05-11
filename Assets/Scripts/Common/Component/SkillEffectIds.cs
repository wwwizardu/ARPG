using UnityEngine;

namespace ARPG.Component
{
    /// <summary>
    /// SkillComponent.EffectiveSkillEffectIds 저장용 인라인 정수 배열 struct.
    /// 최대 8개의 효과 ID를 heap 할당 없이 inline 보관한다 (List&lt;int&gt; 대체).
    /// 초과 시 LogError 후 추가 무시. 호출 측은 Count로 순회.
    /// </summary>
    public struct SkillEffectIds
    {
        public const int Capacity = 8;

        public int Count;
        private int _0, _1, _2, _3, _4, _5, _6, _7;

        public int Get(int index)
        {
            switch (index)
            {
                case 0: return _0;
                case 1: return _1;
                case 2: return _2;
                case 3: return _3;
                case 4: return _4;
                case 5: return _5;
                case 6: return _6;
                case 7: return _7;
                default:
                    Debug.LogError($"[SkillEffectIds] Get 인덱스 범위 초과: index={index}, Count={Count}, Capacity={Capacity}");
                    return 0;
            }
        }

        public void Add(int id)
        {
            if (Count >= Capacity)
            {
                Debug.LogError($"[SkillEffectIds] Capacity({Capacity}) 초과 — id={id} 추가 무시. 스킬 본체 + 페이지 효과 합산이 한도를 넘었습니다.");
                return;
            }
            switch (Count)
            {
                case 0: _0 = id; break;
                case 1: _1 = id; break;
                case 2: _2 = id; break;
                case 3: _3 = id; break;
                case 4: _4 = id; break;
                case 5: _5 = id; break;
                case 6: _6 = id; break;
                case 7: _7 = id; break;
            }
            Count++;
        }
    }
}
