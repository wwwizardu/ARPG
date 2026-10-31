#nullable enable
using ARPG.Component;
using ARPG.Data;
using ARPG.Message;
using ARPG.OreVein;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// 플레이어 채광 진행 (WORLD_ORE_VEIN_PLAN §7.1). E로 붙은 MiningComponent를 1.2초 진행시키고,
    /// 이동(밀림 포함)·피격(HP 감소)·공격/스킬·대상 광맥 사라짐·거리 이탈이면 잔량을 바꾸지 않고 취소한다.
    /// 완료하면 광석 1개를 지급(채광 기능 소유)하고 같은 프레임에 광맥 잔량을 1 소비한 뒤 Player 저장을 요청한다 —
    /// 두 저장 변경 표시가 같은 프레임에 있고 Player 저장이 ore_veins 파트도 실어 반쪽만 저장되지 않는다 (§8.1).
    /// 가방에 자리가 없으면 지급도 소비도 하지 않는다. 상태는 MiningComponent와 OreVeinManager의 저장 기록에 있다.
    /// </summary>
    public class System_PlayerMining : IFixedUpdateSystem
    {
        public int Priority => 258;

        // 시작 위치에서 이만큼(칸) 넘게 벗어나면 이동으로 본다
        private const float MoveTolerance = 0.05f;
        private const float MoveInputThreshold = 0.01f;

        public void OnCreate() { }

        public void OnReset() { }

        public void OnFixedUpdate(float inFixedDeltaTime)
        {
            ComponentManager cm = AR.s.Component;
            SparseSet<MiningComponent> pool = cm.GetComponentPool<MiningComponent>();
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                int entityId = pool.GetEntityId(i);
                MiningComponent mining = pool.GetByIndex(i);
                if (CanContinue(cm, entityId, ref mining) == false)
                {
                    End(cm, entityId);
                    continue;
                }
                mining.Elapsed += inFixedDeltaTime;
                if (mining.Elapsed < OreVeinRules.MiningSeconds)
                {
                    cm.SetComponent(entityId, mining);
                    AR.s.Message.SendToEntity(new MiningProgressMessage
                        { TargetEntityId = entityId, Progress = mining.Elapsed / OreVeinRules.MiningSeconds, Active = true });
                    continue;
                }
                Complete(mining);
                End(cm, entityId);
            }
        }

        private static bool CanContinue(ComponentManager cm, int entityId, ref MiningComponent mining)
        {
            // 채광은 현재 플레이어만, 월드에서만 한다
            if (entityId != AR.s.Data.CurrentPlayerEntityId || AR.s.Dungeon.IsInside == true || AR.s.Dungeon.GameplayPaused == true)
                return false;
            if (cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false ||
                cm.TryGetComponent<StatComponent>(entityId, out var stat) == false ||
                cm.TryGetComponent<InputComponent>(entityId, out var input) == false)
                return false;
            if (input.MoveDirection.sqrMagnitude > MoveInputThreshold ||
                (transform.Position - mining.StartPosition).sqrMagnitude > MoveTolerance * MoveTolerance)
                return false;
            if (stat.CurrentHp < mining.LastHp)
                return false;
            mining.LastHp = stat.CurrentHp;
            if (input.IsAttacking == true || input.SkillSlotHeldMask != 0 || cm.HasComponent<SkillCommandComponent>(entityId) == true)
                return false;
            var chunk = new Vector2Int(mining.ChunkX, mining.ChunkY);
            OreVeinRecord? record = AR.s.OreVein.GetRecord(chunk);
            if (record == null || AR.s.OreVein.IsMineable(chunk, mining.RollDay) == false)
                return false;
            return Vector2.SqrMagnitude(transform.Position - new Vector2(record.TileX + 0.5f, record.TileY + 0.5f)) <=
                   OreVeinRules.MiningCancelRange * OreVeinRules.MiningCancelRange;
        }

        private static void Complete(in MiningComponent mining)
        {
            var chunk = new Vector2Int(mining.ChunkX, mining.ChunkY);
            OreVeinRecord? record = AR.s.OreVein.GetRecord(chunk);
            if (record == null || AR.s.OreVein.IsMineable(chunk, mining.RollDay) == false)
                return;
            int itemId = OreVeinRules.GetItemId(record.Type);
            ItemData? item = itemId > 0 ? AR.s.Item.CreateInventoryItemData(itemId, OreVeinRules.MiningYield) : null;
            if (item == null)
            {
                Debug.LogError($"[OreVein] Mining - ore item {itemId} for {record.Type} could not be created.");
                return;
            }
            Item.Inventory inventory = AR.s.Player.Inventory;
            if (inventory.CanAddItem(item) == false)
            {
                AR.s.UI.SetNotify("가방이 가득 차 광석을 담을 수 없습니다");
                return;
            }
            // 광석 지급과 잔량 소비를 같은 프레임에 한다. 실패하면 둘 다 하지 않은 상태로 되돌린다
            int slot = inventory.AddItem(item);
            if (slot < 0)
                return;
            if (AR.s.OreVein.TryConsume(chunk, mining.RollDay, AR.s.Time.CurrentGameTime) == false)
            {
                inventory.RemoveItem(slot, OreVeinRules.MiningYield, out _);
                return;
            }
            AR.s.UI.SetNotify($"{OreVeinManager.GetOreName(record.Type)} {OreVeinRules.MiningYield}개를 캤습니다");
            _ = AR.s.Data.SaveAsync(SaveScope.Player | SaveScope.World | SaveScope.Map);
        }

        private static void End(ComponentManager cm, int entityId)
        {
            cm.RemoveComponent<MiningComponent>(entityId);
            AR.s.Message.SendToEntity(new MiningProgressMessage { TargetEntityId = entityId, Progress = 0f, Active = false });
        }
    }
}
