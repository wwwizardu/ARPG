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
    /// 참 인스턴스 데이터 (§6.3). 옵션은 ModInstance(Slot = Charm)로 저장한다 — 장비 옵션과 같은 표시·적용 경로를 쓴다.
    /// </summary>
    [Serializable]
    public class CharmData
    {
        public GlobalEnum.CharmShape Shape;

        // 참 옵션 티어를 정한 아이템 레벨 (몬스터 레벨 또는 상점 지역 레벨). 판매가 레벨 배율에도 쓴다
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int ItemLevel;

        public List<ModInstance> Mods = new();

        public void OnLoadCompleted()
        {
            Mods ??= new List<ModInstance>();
            for (int i = 0; i < Mods.Count; i++)
                Mods[i]?.OnLoadCompleted();
        }
    }
}
