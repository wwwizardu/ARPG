#nullable enable
using ARPG.Data;
using ARPG.Tables;

namespace ARPG.Utility
{
    /// <summary>
    /// 몬스터 전리품 식별 — "이 아이템이 몬스터 전리품인가 / 어느 몬스터에서 나왔나".
    /// 트랙 A(식별)와 트랙 B(관찰 반응)의 경계 인터페이스. 무상태 순수 조회(상태·수명 없음).
    ///
    /// 백킹 데이터 = ItemTable.SourceMonsterId (0 = 전리품 아님). 위협 등급 등 파생 정보는
    /// 소스 MonsterTable(Level/Archetype/Biome)에서 얻는다. 트랙 B는 이 클래스만 호출하고
    /// ItemTable 필드명이나 MonsterTable 구조를 직접 알지 않는다.
    ///
    /// 설계: Docs/npc/PLAYER_RECOGNITION_DESIGN.md (트랙 A)
    /// </summary>
    public static class MonsterTrophyIdentifier
    {
        /// <summary>이 아이템이 몬스터 전리품인지 여부. SourceMonsterId > 0.</summary>
        public static bool IsMonsterTrophy(ItemData? item)
        {
            return GetSourceMonsterId(item) > 0;
        }

        /// <summary>전리품의 소스 몬스터 TableId. 전리품이 아니면 0.</summary>
        public static int GetSourceMonsterId(ItemData? item)
        {
            if (item == null || item.Table == null)
                return 0;

            return item.Table.SourceMonsterId;
        }

        /// <summary>
        /// 전리품의 소스 몬스터 테이블. 전리품이 아니거나 테이블이 없으면 null.
        /// 위협 등급(Level/Archetype)·서식 Biome 파생은 여기서 얻은 테이블을 사용한다.
        /// </summary>
        public static MonsterTable? GetSourceMonster(ItemData? item)
        {
            int monsterId = GetSourceMonsterId(item);
            if (monsterId <= 0)
                return null;

            return AR.s.Data?.GetMonster(monsterId);
        }
    }
}
