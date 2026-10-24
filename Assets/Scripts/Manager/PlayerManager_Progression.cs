#nullable enable
using ARPG.Component;
using ARPG.Data;
using ARPG.Message;
using ARPG.Skill.Combat;
using ARPG.Utility;
using UnityEngine;
using GE = GlobalEnum;

namespace ARPG.Manager
{
    /// <summary>
    /// 플레이어 레벨·경험치·능력치 포인트 (PLAYER_LEVEL_CHARM_DESIGN.md §3·§4).
    /// 상태는 PlayerData가 갖고, 이 파일은 경험치 지급·레벨업·분배 확정·되돌리기와 ECS 반영만 맡는다.
    /// </summary>
    public partial class PlayerManager
    {
        /// <summary>
        /// 플레이어 엔티티에 레벨 기본 정확도 A(L)와 넣은 능력치 포인트를 반영하고 스탯 재계산을 요청한다.
        /// 생성 직후·레벨업·분배 확정·되돌리기 때 부른다.
        /// </summary>
        public void ApplyProgressionToEntity(int entityId)
        {
            PlayerData? data = AR.s.Data.Player;
            if (data == null)
                return;

            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<StatComponent>(entityId, out StatComponent stat) == false)
                return;

            stat.BaseAccuracy = HitChanceRules.GetLevelAccuracy(data.Level);
            cm.SetComponent(entityId, stat);

            DataManager dm = AR.s.Data;
            dm.RemoveModifiersBySource(entityId, StatModifierSource.PlayerAttribute, 0);
            AddAttributeModifier(dm, entityId, GE.Stat.Str, data.AllocatedStr);
            AddAttributeModifier(dm, entityId, GE.Stat.Dex, data.AllocatedDex);
            AddAttributeModifier(dm, entityId, GE.Stat.Int, data.AllocatedInt);
            cm.AddComponent(entityId, new StatDirtyTag());
        }

        /// <summary>
        /// 몬스터 사망 경험치 (§3.2). 살아 있는 플레이어 근처(20칸)에서 죽었으면 누가 잡았든 준다.
        /// </summary>
        public void GrantMonsterKillExp(Vector2 deathPosition, int monsterLevel, float expMul)
        {
            if (_myPlayer == null || _myPlayer.IsCurrentEntity == false)
                return;

            int playerId = _myPlayer.EntityId;
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<StatComponent>(playerId, out StatComponent stat) == false || stat.CurrentHp <= 0)
                return;
            if (cm.TryGetComponent<TransformComponent>(playerId, out TransformComponent transform) == false)
                return;

            float radius = PlayerLevelRules.NEARBY_KILL_RADIUS;
            if ((transform.Position - deathPosition).sqrMagnitude > radius * radius)
                return;

            PlayerData? data = AR.s.Data.Player;
            if (data == null)
                return;

            GainExp(PlayerLevelRules.GetKillExp(data.Level, monsterLevel, expMul));
        }

        /// <summary>경험치를 더하고 필요량에 닿을 때마다 레벨을 올린다. 레벨업마다 능력치 포인트 5 (§3.1).</summary>
        public void GainExp(int amount)
        {
            PlayerData? data = AR.s.Data.Player;
            if (data == null || amount <= 0 || data.Level >= PlayerLevelRules.MAX_LEVEL)
                return;

            long exp = (long)data.Exp + amount;
            int levelsGained = 0;
            bool slotOpened = false;
            while (data.Level < PlayerLevelRules.MAX_LEVEL)
            {
                int required = PlayerLevelRules.GetRequiredExp(data.Level);
                if (required <= 0 || exp < required)
                    break;

                exp -= required;
                data.Level++;
                data.UnspentAttributePoints += PlayerLevelRules.ATTRIBUTE_POINTS_PER_LEVEL;
                levelsGained++;
                if (PlayerLevelRules.OpensCharmBoardSlotAt(data.Level) == true)
                    slotOpened = true;
            }

            if (data.Level >= PlayerLevelRules.MAX_LEVEL)
                exp = 0;
            data.Exp = (int)System.Math.Min(exp, int.MaxValue);
            AR.s.Data.MarkPlayerSaveDirty(data, PlayerSaveGroup.Progress);

            if (levelsGained > 0 && _myPlayer != null)
                ApplyProgressionToEntity(_myPlayer.EntityId);

            AR.s.Message.Broadcast(new PlayerProgressChangedMessage
            {
                Level = data.Level,
                LeveledUp = levelsGained > 0,
                CharmBoardSlotOpened = slotOpened,
            });
        }

        /// <summary>
        /// 캐릭터 창에서 예정으로 둔 포인트를 확정한다 (§8.2 [적용]). 남은 포인트보다 많으면 거부한다.
        /// </summary>
        public bool CommitAttributeAllocation(int addStr, int addDex, int addInt)
        {
            PlayerData? data = AR.s.Data.Player;
            if (data == null || addStr < 0 || addDex < 0 || addInt < 0)
                return false;

            int total = addStr + addDex + addInt;
            if (total == 0 || total > data.UnspentAttributePoints)
                return false;

            data.AllocatedStr += addStr;
            data.AllocatedDex += addDex;
            data.AllocatedInt += addInt;
            data.UnspentAttributePoints -= total;
            OnAttributePointsChanged(data);
            return true;
        }

        /// <summary>넣은 포인트 중 해당 능력치 포인트 수 (기본 능력치 제외).</summary>
        public int GetAllocatedPoints(GE.Stat attribute)
        {
            PlayerData? data = AR.s.Data.Player;
            if (data == null)
                return 0;

            switch (attribute)
            {
                case GE.Stat.Str: return data.AllocatedStr;
                case GE.Stat.Dex: return data.AllocatedDex;
                case GE.Stat.Int: return data.AllocatedInt;
                default: return 0;
            }
        }

        /// <summary>포인트 N개 되돌리기 골드 (§4.2). 19레벨까지 0.</summary>
        public int GetRefundCost(int points)
        {
            PlayerData? data = AR.s.Data.Player;
            if (data == null || points <= 0)
                return 0;

            long cost = (long)PlayerLevelRules.GetRespecCostPerPoint(data.Level) * points;
            return cost > int.MaxValue ? int.MaxValue : (int)cost;
        }

        /// <summary>
        /// 넣은 포인트를 골드를 내고 되돌린다 (§4.2). 기본 능력치 아래로는 내려가지 않는다.
        /// 골드가 모자라거나 넣은 포인트보다 많이 되돌리려 하면 아무것도 바꾸지 않는다.
        /// </summary>
        public bool TryRefundAttributePoints(GE.Stat attribute, int points)
        {
            PlayerData? data = AR.s.Data.Player;
            if (data == null || points <= 0 || GetAllocatedPoints(attribute) < points)
                return false;

            int cost = GetRefundCost(points);
            if (data.Gold < cost)
                return false;

            data.Gold -= cost;
            switch (attribute)
            {
                case GE.Stat.Str: data.AllocatedStr -= points; break;
                case GE.Stat.Dex: data.AllocatedDex -= points; break;
                case GE.Stat.Int: data.AllocatedInt -= points; break;
                default: return false;
            }
            data.UnspentAttributePoints += points;
            OnAttributePointsChanged(data);
            return true;
        }

        /// <summary>넣은 포인트를 모두 되돌린다. 비용 = 1포인트 비용 × 포인트 수 (§4.2).</summary>
        public bool TryRefundAllAttributePoints()
        {
            PlayerData? data = AR.s.Data.Player;
            if (data == null)
                return false;

            int points = data.AllocatedStr + data.AllocatedDex + data.AllocatedInt;
            if (points <= 0)
                return false;

            int cost = GetRefundCost(points);
            if (data.Gold < cost)
                return false;

            data.Gold -= cost;
            data.AllocatedStr = 0;
            data.AllocatedDex = 0;
            data.AllocatedInt = 0;
            data.UnspentAttributePoints += points;
            OnAttributePointsChanged(data);
            return true;
        }

        private void OnAttributePointsChanged(PlayerData data)
        {
            AR.s.Data.MarkPlayerSaveDirty(data, PlayerSaveGroup.Progress);
            if (_myPlayer != null)
                ApplyProgressionToEntity(_myPlayer.EntityId);

            AR.s.Message.Broadcast(new PlayerProgressChangedMessage { Level = data.Level });
        }

        private static void AddAttributeModifier(DataManager dm, int entityId, GE.Stat attribute, int points)
        {
            if (points <= 0)
                return;

            dm.AddStatModifier(entityId, StatModifierSource.PlayerAttribute, 0, attribute, StatModifierType.Add, points);
        }
    }
}
