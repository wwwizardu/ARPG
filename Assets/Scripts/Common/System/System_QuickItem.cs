#nullable enable
using ARPG.Component;
using ARPG.Creature;
using ARPG.Data;
using UnityEngine;

namespace ARPG.Systems
{
    public sealed class System_QuickItem : IUpdateSystem
    {
        public int Priority => 120;
        public void OnCreate() { }
        public void OnReset() { }

        public void OnUpdate(float deltaTime)
        {
            ComponentManager cm = AR.s.Component;
            var pool = cm.GetComponentPool<InputComponent>();
            for (int i = 0; i < pool.Count; i++)
            {
                InputComponent input = pool.GetByIndex(i);
                int mask = input.QuickItemPressedMask;
                if (mask == 0) continue;
                int entityId = pool.GetEntityId(i);
                input.QuickItemPressedMask = 0;
                cm.SetComponent(entityId, input);
                for (int slot = 0; slot < 4; slot++) if ((mask & (1 << slot)) != 0) TryUse(entityId, slot);
            }
        }

        public static float Remaining(int entityId, int itemId)
        {
            ComponentManager cm = AR.s.Component;
            var table = AR.s.Data.GetItem(itemId);
            if (table == null || cm.TryGetComponent<QuickItemCooldownComponent>(entityId, out var cooldown) == false) return 0f;
            return Mathf.Max(0f, (table.RestoreHpPercent > 0 ? cooldown.HpReadyAt : cooldown.MpReadyAt) - Time.time);
        }

        public static bool TryUse(int entityId, int slot)
        {
            ComponentManager cm = AR.s.Component;
            if (slot < 0 || slot >= 4 || AR.s.Data.CurrentPlayerEntityId != entityId
                || AR.s.Dungeon.GameplayPaused == true || AR.s.UI.CurrentUI != null || AR.s.UI.IsLock == true
                || cm.HasComponent<DestroyTag>(entityId) == true
                || cm.TryGetComponent<StatComponent>(entityId, out var stat) == false || stat.CurrentHp <= 0
                || (cm.TryGetComponent<StateComponent>(entityId, out var state) == true && state.Condition == CharacterConditions.Dead)) return false;
            int itemId = AR.s.Data.Player._quickItemIds[slot];
            var table = AR.s.Data.GetItem(itemId);
            if (table == null || table.ItemType != GlobalEnum.ItemType.Consumable || Remaining(entityId, itemId) > 0f) return false;
            int hp = Mathf.Min(stat.FinalMaxHp - stat.CurrentHp, Mathf.CeilToInt(stat.FinalMaxHp * Mathf.Clamp(table.RestoreHpPercent, 0, 100) / 100f));
            int mp = Mathf.Min(stat.FinalMaxMp - stat.CurrentMp, Mathf.CeilToInt(stat.FinalMaxMp * Mathf.Clamp(table.RestoreMpPercent, 0, 100) / 100f));
            if (hp <= 0 && mp <= 0) return false;
            if (AR.s.Player.Inventory.TryConsumeItem(itemId, 1) == false) return false;
            if (hp > 0) stat.SetCurrentHp(entityId, stat.CurrentHp + hp);
            if (mp > 0) stat.CurrentMp += mp;
            cm.SetComponent(entityId, stat);
            if (mp > 0) AR.s.Message.SendToEntity(new ARPG.Message.ManaChangedMessage
                { TargetEntityId = entityId, CurrentMp = stat.CurrentMp, MaxMp = stat.FinalMaxMp });
            cm.TryGetComponent<QuickItemCooldownComponent>(entityId, out var cooldown);
            float ready = Time.time + Mathf.Max(0.1f, table.UseCooldownSeconds);
            if (table.RestoreHpPercent > 0) cooldown.HpReadyAt = ready;
            if (table.RestoreMpPercent > 0) cooldown.MpReadyAt = ready;
            cm.SetComponent(entityId, cooldown);
            AR.s.Data.MarkPlayerSaveDirty(AR.s.Data.Player, PlayerSaveGroup.Vitality);
            AR.s.UI.Input.Vibrate(0.2f, 0.08f);
            return true;
        }
    }
}
