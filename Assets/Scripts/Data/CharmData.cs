#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Tables;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.Data
{
    /// <summary>
    /// 참 판 인스턴스 데이터 (PLAYER_LEVEL_CHARM_DESIGN.md §6.2·§7). 3×3칸, 칸 번호는 왼쪽 위부터 행 순서 0~8.
    /// 박힌 참은 판에 붙어 있어 판을 옮기거나 거래하면 함께 움직인다(§6.5). SkillBookData.SocketedPages와 같은 방식이다.
    /// </summary>
    [Serializable]
    public class CharmBoardData
    {
        public int BlockedMask;     // 막힌 칸 9비트 (bit i = 칸 i)

        [SerializeReference]
        public List<CharmPlacement> Charms = new();

        public void OnLoadCompleted()
        {
            Charms ??= new List<CharmPlacement>();
            for (int i = Charms.Count - 1; i >= 0; i--)
            {
                CharmPlacement? placement = Charms[i];
                if (placement == null || placement.Charm == null)
                {
                    Charms.RemoveAt(i);
                    continue;
                }
                placement.Charm.OnLoadCompleted();
            }
        }

        public int GetBlockedCount()
        {
            int count = 0;
            for (int i = 0; i < 9; i++)
            {
                if ((BlockedMask & (1 << i)) != 0)
                    count++;
            }
            return count;
        }
    }

    /// <summary>판에 박힌 참 1개. 위치는 모양을 회전한 뒤 묶음 왼쪽 위 칸(AnchorCell)과 회전 수(0~3, 90도씩)다.</summary>
    [Serializable]
    public class CharmPlacement
    {
        [SerializeReference]
        public ItemData Charm = null!;
        public int AnchorCell;
        public int Rotation;
    }

    /// <summary>
    /// 참 인스턴스 데이터 (§6.3). 옵션은 장비처럼 ModInstance(Slot = Prefix/Postfix)로 저장한다 — 장비 옵션과 같은 표시·적용 경로를 쓴다.
    /// 등급은 장비와 같이 저장하지 않고 접두·접미 수로 판정한다.
    /// </summary>
    [Serializable]
    public class CharmData
    {
        public GlobalEnum.CharmShape Shape;

        // 참 옵션 티어를 정한 아이템 레벨 (몬스터 레벨 또는 상점 지역 레벨). 판매가 레벨 배율에도 쓴다
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int ItemLevel;

        public List<ModInstance> Mods = new();

        /// <summary>
        /// 로드 보정 (§6.3):
        /// - 2026-10-25 전 참 옵션(Slot = Charm)은 Mod 표의 칸(접두·접미)으로 바꾼다.
        /// - 값이 지금 참 값 표(CharmModTierTable)의 그 티어·모양 칸 범위를 벗어나면 범위 안으로 맞춘다. 옵션·티어는 그대로다.
        ///   그래서 표를 고치면 이미 있는 참도 다음 로드 때 새 범위를 따른다. 그 칸이 없으면(예: 옛 1×1에 붙은 최대 HP) 값을 그대로 둔다.
        /// </summary>
        public void OnLoadCompleted()
        {
            Mods ??= new List<ModInstance>();
            for (int i = Mods.Count - 1; i >= 0; i--)
            {
                ModInstance? mod = Mods[i];
                if (mod == null)
                {
                    Mods.RemoveAt(i);
                    continue;
                }
                mod.OnLoadCompleted();
                if (mod.Slot != GlobalEnum.ModSlot.Prefix && mod.Slot != GlobalEnum.ModSlot.Postfix && mod.Table != null
                    && (mod.Table.Slot == GlobalEnum.ModSlot.Prefix || mod.Table.Slot == GlobalEnum.ModSlot.Postfix))
                    mod.Slot = mod.Table.Slot;

                CharmModTierTable? valueRow = AR.s.Data?.GetCharmModTier(mod.ModTableId, mod.Tier);
                if (valueRow == null || ARPG.Item.CharmRules.TryGetOptionValueRange(valueRow, Shape, out int min, out int max) == false)
                    continue;
                mod.Value1 = (ushort)Mathf.Clamp(mod.Value1, min, max);
            }
        }

        /// <summary>
        /// 2026-10-25 제거된 3×3 참이면 2×2로 바꾸고 true. 옵션·값은 그대로다.
        /// 3×3은 막힌 칸 없는 판의 왼쪽 위에서 시작하므로 같은 기준 칸의 2×2도 그 판에 그대로 들어간다.
        /// 아이템 Id는 부르는 쪽(ItemData)이 고친다.
        /// </summary>
        public bool MigrateRemovedShape()
        {
            if (Shape != GlobalEnum.CharmShape.Square3)
                return false;
            Shape = GlobalEnum.CharmShape.Square2;
            return true;
        }

        /// <summary>등급: 옵션 없으면 일반, 접두·접미 각 1개 이하면 마법, 그 밖은 희귀 (장비와 같은 판정).</summary>
        public GlobalEnum.EquipmentGrade GetGrade()
        {
            CountAffixes(out int prefixCount, out int postfixCount);
            return EquipmentData.ResolveGrade(prefixCount, postfixCount);
        }

        public void CountAffixes(out int prefixCount, out int postfixCount)
        {
            prefixCount = 0;
            postfixCount = 0;
            for (int i = 0; i < Mods.Count; i++)
            {
                if (Mods[i].Slot == GlobalEnum.ModSlot.Prefix)
                    prefixCount++;
                else if (Mods[i].Slot == GlobalEnum.ModSlot.Postfix)
                    postfixCount++;
            }
        }
    }
}
