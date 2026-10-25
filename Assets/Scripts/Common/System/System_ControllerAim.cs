#nullable enable
using ARPG.Component;
using ARPG.Skill;
using ARPG.Skill.Aim;
using ARPG.Tables;
using ARPG.Utility;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>Resolves each requested/running skill separately; never uses the right stick or a preview cursor.</summary>
    public sealed class System_ControllerAim : IFixedUpdateSystem
    {
        public int Priority => 185;
        public void OnCreate() { }
        public void OnReset() { }

        public void OnFixedUpdate(float deltaTime)
        {
            ComponentManager cm = AR.s.Component;
            var commands = cm.GetComponentPool<SkillCommandComponent>();
            for (int i = 0; i < commands.Count; i++)
            {
                int owner = commands.GetEntityId(i);
                if (cm.TryGetComponent<InputComponent>(owner, out var input) == false || input.IsGamepad == false) continue;
                var command = commands.GetByIndex(i);
                if (cm.TryGetComponent<SkillComponent>(command.SkillEntityId, out var skill) == false || skill.Table == null) continue;
                command.TargetPosition = Resolve(owner, skill.Table, input.AimDirection, input.ControllerTargetId, out int targetId);
                if (skill.Table.SkillTargetType == GlobalEnum.SkillTargetType.Position && skill.Table.SkillRangeMin > 0f
                    && cm.TryGetComponent<TransformComponent>(owner, out var origin) == true
                    && (command.TargetPosition - origin.Position).sqrMagnitude + 0.0001f < skill.Table.SkillRangeMin * skill.Table.SkillRangeMin)
                { cm.RemoveComponent<SkillCommandComponent>(owner); i--; continue; }
                command.TargetId = targetId;
                input.ControllerTargetId = targetId;
                cm.SetComponent(owner, input);
                cm.SetComponent(owner, command);
            }

            var skills = cm.GetComponentPool<SkillComponent>();
            for (int i = 0; i < skills.Count; i++)
            {
                int entityId = skills.GetEntityId(i);
                var skill = skills.GetByIndex(i);
                if (skill.Table == null || cm.TryGetComponent<InputComponent>(skill.OwnerEntityId, out var input) == false || input.IsGamepad == false
                    || cm.TryGetComponent<SkillStateComponent>(entityId, out var state) == false || state.IsRunning == false || state.State == SkillState.End
                    || cm.TryGetComponent<SkillTargetComponent>(entityId, out var target) == false) continue;
                var mode = SkillUsePolicyResolver.ResolveAimTrackingMode(skill, true);
                if (mode == GlobalEnum.SkillAimTrackingMode.SnapshotOnCommand
                    || (mode == GlobalEnum.SkillAimTrackingMode.TrackUntilCommit && skill.IsAimCommitted == true)) continue;
                target.ControllerAimPoint = Resolve(skill.OwnerEntityId, skill.Table, input.AimDirection, target.ControllerTargetId, out int targetId);
                target.ControllerTargetId = targetId;
                cm.SetComponent(entityId, target);
            }
        }

        public static Vector2 Resolve(int owner, SkillTable table, Vector2 facing, int previousTarget, out int selected)
        {
            ComponentManager cm = AR.s.Component;
            selected = -1;
            if (cm.TryGetComponent<TransformComponent>(owner, out var originTransform) == false) return Vector2.zero;
            Vector2 origin = originTransform.Position;
            facing = SkillAimResolver.NormalizeOrFallback(facing, Vector2.right);
            if (SkillUsePolicyResolver.RequiresDirectionalAim(table) == false) return origin;
            float range = table.SkillRangeMax > 0f ? table.SkillRangeMax : 12f;
            float bestScore = float.MaxValue;
            Vector2 result = origin + facing * Mathf.Clamp(range * 0.7f, Mathf.Max(0f, table.SkillRangeMin), range);
            var transforms = cm.GetComponentPool<TransformComponent>();
            for (int i = 0; i < transforms.Count; i++)
            {
                int candidate = transforms.GetEntityId(i);
                if (candidate == owner || cm.HasComponent<DestroyTag>(candidate) == true
                    || cm.TryGetComponent<StatComponent>(candidate, out var stats) == false || stats.CurrentHp <= 0
                    || FactionHelper.IsHostileTo(owner, candidate) == false) continue;
                Vector2 position = transforms.GetByIndex(i).Position;
                Vector2 offset = position - origin;
                float distanceSqr = offset.sqrMagnitude;
                float effectiveRange = table.SkillTargetType == GlobalEnum.SkillTargetType.Direction
                    ? range + SkillHelper.GetEntityHitRadius(owner) + SkillHelper.GetEntityHitRadius(candidate) : range;
                if (distanceSqr > effectiveRange * effectiveRange || distanceSqr < table.SkillRangeMin * table.SkillRangeMin) continue;
                float distance = Mathf.Sqrt(distanceSqr);
                float alignment = distance > 0.001f ? Vector2.Dot(facing, offset / distance) : 1f;
                if (alignment < 0.25f || AR.s.Fog.IsVisibleWorld(position) == false) continue;
                float score = (1f - alignment) * 4f + distance / Mathf.Max(1f, effectiveRange);
                if (candidate == previousTarget) score -= 0.3f;
                if (score >= bestScore || HasClearLine(origin, position) == false) continue;
                bestScore = score; selected = candidate; result = position;
            }
            result = SkillAimResolver.ResolveTargetPosition(origin, result, result - origin, table.SkillRangeMin, range);
            return ClampDestination(owner, table, origin, result);
        }

        public static Vector2 ClampDestination(int owner, SkillTable table, Vector2 origin, Vector2 result)
        {
            ComponentManager cm = AR.s.Component;
            bool movement = table.SkillType == GlobalEnum.SkillType.Jump || table.SkillType == GlobalEnum.SkillType.Dash;
            if (movement == true || table.SkillTargetType == GlobalEnum.SkillTargetType.Position)
            {
                float radius = 0.05f;
                if (movement == true && cm.TryGetComponent<ColliderComponent>(owner, out var collider) == true) radius = collider.MoveRadius;
                Vector2 delta = result - origin;
                int steps = Mathf.Clamp(Mathf.CeilToInt(delta.magnitude / 0.2f), 1, 256);
                Vector2 valid = origin;
                for (int i = 1; i <= steps; i++)
                {
                    Vector2 point = origin + delta * (i / (float)steps);
                    if (CollisionUtil.IsBlockedAt(point, radius) == true) break;
                    valid = point;
                }
                result = valid;
            }
            return result;
        }

        private static bool HasClearLine(Vector2 start, Vector2 end)
        {
            Vector2 delta = end - start;
            int steps = Mathf.Clamp(Mathf.CeilToInt(delta.magnitude / 0.4f), 1, 128);
            for (int i = 1; i <= steps; i++)
                if (CollisionUtil.IsBlockedAt(start + delta * (i / (float)steps), 0.02f) == true) return false;
            return true;
        }
    }
}
