#nullable enable
using ARPG.Component;
using ARPG.Input;
using ARPG.Utility;
using ARPG.Village;
using UnityEngine;

namespace ARPG.Systems
{
    public sealed class System_Input : IUpdateSystem
    {
        public int Priority => 0;
        public void OnCreate() { }
        public void OnReset() { }

        public void OnUpdate(float deltaTime)
        {
            ComponentManager cm = AR.s.Component;
            ArpgInput controls = AR.s.UI.Input;
            controls.Tick();
            if (AR.s.UI.UpdateInput() == false || AR.s.Dungeon.GameplayPaused == true)
            { ClearGameplayInput(); return; }
            int playerId = AR.s.Data.CurrentPlayerEntityId;
            if (cm.TryGetComponent<InputComponent>(playerId, out var input) == false) return;
            if (controls.Pressed("Inventory") == true) AR.s.UI.Show<UI.UICharacter>(AddressablePath.Character, UIManager.Layer.Main);
            else if (controls.Pressed("SkillBook") == true) AR.s.UI.Show<UI.UISkillBook>(AddressablePath.SkillBook, UIManager.Layer.Main);
            else if (controls.Pressed("CharmBoard") == true) AR.s.UI.Show<UI.UICharmBoard>(AddressablePath.CharmBoard, UIManager.Layer.Main);
            else if (controls.Pressed("QuickSlots") == true) AR.s.UI.Show<UI.UIQuickSlots>(AddressablePath.QuickSlots, UIManager.Layer.Main);
            if (AR.s.UI.CurrentUI != null) { ClearGameplayInput(); return; }
            input.IsGamepad = controls.IsGamepad;
            input.MoveDirection = controls.ReadMovement(AR.s.UI.Settings.Current.MoveDeadzone);
            if (input.AimDirection.sqrMagnitude < 0.001f) input.AimDirection = Vector2.right;
            if (input.MoveDirection.sqrMagnitude > 0.01f) input.AimDirection = input.MoveDirection.normalized;
            input.MousePosition = controls.Player.MouseMove.ReadValue<Vector2>();
            Camera camera = Camera.main;
            if (input.IsGamepad == false && camera != null) input.MouseWorldPosition = camera.ScreenToWorldPoint(input.MousePosition);
            input.IsInteracting = controls.Pressed("Interact") == true && AR.s.Npc.IsPlayerInDuel == false;
            input.IsSprinting = controls.Held("Sprint");
            bool anyHeld = false;
            for (int i = 0; i < GameInputBindings.SkillNames.Length; i++)
                if (controls.Held(GameInputBindings.SkillNames[i]) == true) anyHeld = true;
            for (int i = 0; i < 4; i++) if (controls.Held(GameInputBindings.ItemNames[i]) == true) anyHeld = true;
            if (input.RequireSkillRelease == true && anyHeld == false) input.RequireSkillRelease = false;
            input.SkillSlotHeldMask = 0; input.QuickItemPressedMask = 0; input.IsAttacking = false;
            if (input.RequireSkillRelease == false)
            {
                bool attack = controls.Held("Attack");
                if (controls.Pressed("Attack") == true && input.IsGamepad == false)
                    input.AttackSuppressedUntilRelease = AR.s.UI.IsPointerOverUIElement(input.MousePosition) == true || TryClickDroppedItem(input.MouseWorldPosition);
                if (attack == false) input.AttackSuppressedUntilRelease = false;
                for (int i = 0; i < GameInputBindings.SkillNames.Length; i++)
                {
                    if (controls.Held(GameInputBindings.SkillNames[i]) == false || (i == 0 && input.AttackSuppressedUntilRelease == true)) continue;
                    input.SkillSlotHeldMask |= 1 << i;
                }
                input.IsAttacking = (input.SkillSlotHeldMask & 1) != 0;
                for (int i = 0; i < 4; i++)
                    if (controls.Pressed(GameInputBindings.ItemNames[i]) == true) input.QuickItemPressedMask |= 1 << i;
            }
            cm.SetComponent(playerId, input);
            if (input.IsInteracting == true)
            {
                bool pickedUp = input.IsGamepad == true && TryPickupNearest(playerId) == true;
                if (pickedUp == false && cm.TryGetComponent<PlayerNearbyServicesComponent>(playerId, out var nearby) == true) ServiceUIRouter.Open(nearby);
                if (AR.s.UI.CurrentUI != null || AR.s.Dungeon.IsBusy == true) { ClearGameplayInput(); return; }
            }
            for (int i = 0; i < GameInputBindings.SkillNames.Length; i++)
            {
                if ((input.SkillSlotHeldMask & (1 << i)) == 0) continue;
                if (i == 1 && controls.Pressed(GameInputBindings.SkillNames[i]) == false) continue;
                if (AR.s.PlayerSkill.GetEquippedSkillId(i) == 0) continue;
                if (SkillHelper.GetSkillCommandComponent(i, playerId, input.MouseWorldPosition, out var command) == true) cm.SetComponent(playerId, command);
            }
        }

        private static void ClearGameplayInput()
        {
            ComponentManager cm = AR.s.Component;
            var pool = cm.GetComponentPool<InputComponent>();
            for (int i = 0; i < pool.Count; i++)
            {
                int id = pool.GetEntityId(i);
                var input = pool.GetByIndex(i);
                input.MoveDirection = Vector2.zero; input.SkillSlotHeldMask = 0; input.QuickItemPressedMask = 0;
                input.IsAttacking = false; input.IsInteracting = false; input.IsSprinting = false; input.RequireSkillRelease = true;
                cm.SetComponent(id, input);
                cm.RemoveComponent<SkillCommandComponent>(id);
            }
        }

        private static bool TryClickDroppedItem(Vector2 position)
        {
            RaycastHit2D hit = Physics2D.Raycast(position, Vector2.zero);
            if (hit.collider == null || hit.collider.gameObject.CompareTag("DropedItem") == false) return false;
            var item = hit.collider.GetComponentInParent<Item.ItemObject>();
            if (item == null) return false;
            AR.s.Item.PickupItem(item); return true;
        }

        private static bool TryPickupNearest(int playerId)
        {
            ComponentManager cm = AR.s.Component;
            if (cm.TryGetComponent<TransformComponent>(playerId, out var player) == false) return false;
            var pool = cm.GetComponentPool<WorldItemComponent>();
            int nearest = -1;
            float nearestSqr = 6.25f;
            for (int i = 0; i < pool.Count; i++)
            {
                int id = pool.GetEntityId(i);
                if (cm.TryGetComponent<TransformComponent>(id, out var position) == false) continue;
                float sqr = (position.Position - player.Position).sqrMagnitude;
                if (sqr < nearestSqr) { nearestSqr = sqr; nearest = id; }
            }
            if (nearest < 0) return false;
            var items = Object.FindObjectsByType<Item.ItemObject>();
            for (int i = 0; i < items.Length; i++)
                if (items[i].EntityId == nearest) { AR.s.Item.PickupItem(items[i]); return true; }
            return false;
        }
    }
}
