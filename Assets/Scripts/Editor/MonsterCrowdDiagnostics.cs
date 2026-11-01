using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ARPG.Component;
using ARPG.Monster;
using ARPG.Systems;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace ARPG.EditorTools
{
    /// <summary>
    /// 군집 이동의 유지 진단. EditMode의 임시 ComponentManager로 실제 Simulate 경로를 실행한다.
    /// 게임/세이브/기존 엔티티를 건드리지 않는다. 부하 수치는 평지·군집 계산이며 전체 게임 프레임이 아니다.
    /// </summary>
    public static class MonsterCrowdDiagnostics
    {
        private static readonly MonsterTerrainSweep _openTerrain = OpenTerrain;
        private static readonly MonsterTerrainSweep _corridorTerrain = CorridorTerrain;
        private static readonly MonsterTerrainSweep _wallTerrain = WallTerrain;
        private static readonly MonsterTerrainSweep _sealedTerrain = SealedTerrain;
        private static readonly Func<int, bool> _active = IsActive;
        private static readonly Func<int, bool> _inactive = IsInactive;

        [MenuItem("ARPG/Diagnostics/Monster Crowd/Run Contracts")]
        public static void RunContracts()
        {
            RequireEditMode();
            var report = new StringBuilder("[MonsterCrowd] Contracts\n");
            GameObject temporary = new GameObject("MonsterCrowdDiagnostic") { hideFlags = HideFlags.HideAndDontSave };
            ComponentManager cm = temporary.AddComponent<ComponentManager>();
            cm.Reset();
            try
            {
                ValidateGrid(report);
                ValidateSweeps(report);
                ValidateSimulation(cm, report);
                Debug.Log(report.AppendLine("PASS: all monster crowd contracts.").ToString());
            }
            finally
            {
                cm.Reset();
                Object.DestroyImmediate(temporary);
            }
        }

        [MenuItem("ARPG/Diagnostics/Monster Crowd/Measure 50 100 200")]
        public static void MeasureLoad()
        {
            RequireEditMode();
            var report = new StringBuilder("[MonsterCrowd] Flat terrain simulation, 600 measured steps after 60 warmup steps.\n");
            GameObject temporary = new GameObject("MonsterCrowdBenchmark") { hideFlags = HideFlags.HideAndDontSave };
            ComponentManager cm = temporary.AddComponent<ComponentManager>();
            cm.Reset();
            try
            {
                int[] sizes = { 50, 100, 200 };
                for (int i = 0; i < sizes.Length; i++)
                {
                    Measure(cm, sizes[i], false, report);
                    Measure(cm, sizes[i], true, report);
                }
                Debug.Log(report.AppendLine("Each step resets pursuit so the benchmark includes avoidance rather than only settled waiting.").ToString());
            }
            finally
            {
                cm.Reset();
                Object.DestroyImmediate(temporary);
            }
        }

        private static void ValidateGrid(StringBuilder report)
        {
            var grid = NewGrid();
            int a = grid.Add(1, new Vector2(-0.1f, -0.1f), 0.3f, true);
            grid.Add(2, new Vector2(0.2f, 0.2f), 0.3f, true);
            grid.FindNeighbors(a, Vector2.zero, 0f);
            Require(grid.NeighborCount == 1, "negative coordinates and neighboring cells");
            grid.SetPosition(1, new Vector2(200f, 0f));
            grid.FindNeighbors(a, Vector2.zero, 0f);
            Require(grid.NeighborCount == 0 && grid.OverflowCount == 1, "cell unlink and overflow insertion");
            grid.SetPosition(a, new Vector2(199.5f, 0f));
            grid.FindNeighbors(a, Vector2.zero, 0f);
            Require(grid.NeighborCount == 1, "outside-array pairs are not dropped");
            grid.SetPosition(a, new Vector2(31.8f, 0f));
            grid.SetPosition(1, new Vector2(32.2f, 0f));
            grid.FindNeighbors(a, Vector2.zero, 0f);
            Require(grid.NeighborCount == 1, "inside/outside array boundary pair");
            grid.Begin(new Vector2(100f, -100f));
            a = grid.Add(3, new Vector2(100f, -100f), 0.3f, true);
            grid.FindNeighbors(a, Vector2.zero, 0f);
            Require(grid.Count == 1 && grid.NeighborCount == 0 && grid.OverflowCount == 0,
                "recentered generation contains no old occupants");
            grid.Add(4, new Vector2(103f, -100f), 3f, false);
            grid.FindNeighbors(a, Vector2.zero, 0f);
            Require(grid.NeighborCount == 1, "large radii search farther than adjacent cells");
            report.AppendLine("PASS: grid registration, negative coordinates, unlink, overflow, boundaries, generation, large radii.");
        }

        private static void ValidateSweeps(StringBuilder report)
        {
            var grid = NewGrid();
            int a = grid.Add(1, Vector2.zero, 0.3f, true);
            grid.Add(2, Vector2.right, 0.3f, false);
            Vector2 travel = grid.ClipMove(a, Vector2.right * 10f, 0f);
            Require(travel.x > 0.39f && travel.x < 0.401f, "high speed sweep stops at circle contact");
            grid.SetPosition(a, Vector2.right * 0.4f);
            Require(grid.ClipMove(a, Vector2.right, 0f).sqrMagnitude < 0.00001f,
                "touching circles cannot move into one another");
            Require(grid.ClipMove(a, Vector2.left, 0f).x < -0.99f, "contact permits moving away");
            Require(grid.ClipMove(a, Vector2.up, 0f).y > 0.99f, "contact permits tangent motion");

            grid.Begin(Vector2.zero);
            a = grid.Add(1, Vector2.zero, 0.3f, true);
            grid.Add(2, Vector2.zero, 0.3f, true);
            for (int iteration = 0; iteration < 120; iteration++)
            {
                for (int i = 0; i < grid.Count; i++)
                    grid.SetPosition(i, grid.GetBody(i).Position + MonsterCrowdMotion.RecoverOverlap(grid, i, 0.02f, _openTerrain));
            }
            Require((grid.GetBody(0).Position - grid.GetBody(1).Position).sqrMagnitude >= 0.359f,
                "identical positions recover deterministically");
            Require((grid.GetBody(0).Position + grid.GetBody(1).Position).sqrMagnitude < 0.1f,
                "overlap recovery does not drift both actors in one direction");
            report.AppendLine("PASS: swept contact, no tunneling, escape, tangent, identical-position recovery.");
        }

        private static void ValidateSimulation(ComponentManager cm, StringBuilder report)
        {
            var grid = NewGrid();
            var settings = new MonsterCrowdSettings();
            AddMonster(cm, 1, Vector2.zero, Vector2.right * 4f);
            AddMonster(cm, 2, Vector2.right * 0.625f, Vector2.zero);
            Step(cm, settings, grid, 1, _corridorTerrain);
            Require(cm.TryGetComponent<MonsterCrowdComponent>(1, out var state) == true && state.IsWaiting == true,
                "narrow corridor waits behind a stationary attacker");
            Require(cm.TryGetComponent<VelocityComponent>(1, out var velocity) == true && velocity.Speed == 4f,
                "waiting preserves requested speed");
            cm.SetComponent(2, new TransformComponent { Position = Vector2.right * 3f });
            Step(cm, settings, grid, 20, _corridorTerrain);
            Require(cm.TryGetComponent<TransformComponent>(1, out var position) == true && position.Position.x > 0.01f,
                "waiting resumes after a gap opens");
            settings.Enabled = false;
            int previousFrame = Read<MonsterCrowdComponent>(cm, 1).IntegratedFrame;
            Vector2 previousPosition = position.Position;
            Step(cm, settings, grid, 21, _openTerrain);
            Require(Read<MonsterCrowdComponent>(cm, 1).IntegratedFrame == previousFrame
                && Read<TransformComponent>(cm, 1).Position == previousPosition && grid.Count == 0,
                "disabled module leaves base movement ownership available");
            settings.Enabled = true;
            cm.AddComponent(1, new MonsterCrowdExcludedTag());
            Step(cm, settings, grid, 22, _openTerrain);
            Require(grid.Count == 1 && Read<MonsterCrowdComponent>(cm, 1).IntegratedFrame == previousFrame,
                "excluded actor has neither movement nor occupancy");
            cm.RemoveComponent<MonsterCrowdExcludedTag>(1);
            Step(cm, settings, grid, 23, _openTerrain);
            Require(grid.Count == 2 && Read<MonsterCrowdComponent>(cm, 1).IntegratedFrame == 23,
                "removing exclusion restores participation");

            cm.AddComponent(1, new DashComponent());
            Step(cm, settings, grid, 24, _openTerrain);
            Require(grid.Count == 1, "dash retains position ownership");
            cm.RemoveComponent<DashComponent>(1);
            cm.AddComponent(1, new JumpComponent());
            Step(cm, settings, grid, 25, _openTerrain);
            Require(grid.Count == 1, "jump retains position ownership");
            cm.RemoveComponent<JumpComponent>(1);
            cm.AddComponent(1, new ForcedMoveComponent());
            Step(cm, settings, grid, 26, _openTerrain);
            Require(grid.Count == 1, "forced movement retains position ownership");
            cm.RemoveComponent<ForcedMoveComponent>(1);
            var dead = Read<StatComponent>(cm, 2);
            dead.SetCurrentHpDirect(0);
            cm.SetComponent(2, dead);
            Step(cm, settings, grid, 27, _openTerrain);
            Require(grid.Count == 1, "dead actors release occupancy");
            System_MonsterCrowdMovement.Simulate(cm, settings, grid, Vector2.zero, 1f / 60f, 1f, 28, _openTerrain, _inactive);
            Require(grid.Count == 0, "inactive map/view actors release occupancy");

            cm.Reset();
            AddMonster(cm, 1, Vector2.zero, Vector2.right * 4f);
            AddMonster(cm, 2, Vector2.right * 0.625f, Vector2.zero);
            Step(cm, settings, grid, 30, _openTerrain);
            Require(Mathf.Abs(Read<TransformComponent>(cm, 1).Position.y) > 0.001f,
                "open lateral space produces a detour");
            settings.AvoidanceEnabled = false;
            cm.SetComponent(1, new TransformComponent { Position = new Vector2(1.65f, 0f) });
            cm.SetComponent(1, new MonsterCrowdComponent());
            cm.SetComponent(2, new TransformComponent { Position = new Vector2(-10f, 0f) });
            Step(cm, settings, grid, 31, _wallTerrain);
            Require(Read<TransformComponent>(cm, 1).Position.x <= 1.70001f,
                "resolved movement respects terrain boundaries");
            cm.Reset();
            AddMonster(cm, 1, Vector2.zero, Vector2.right * 4f);
            settings.AvoidanceEnabled = true;
            Step(cm, settings, grid, 32, _sealedTerrain);
            Require(Read<MonsterCrowdComponent>(cm, 1).IsWaiting == false,
                "terrain-only blockage must not suppress A* stuck recovery");
            var typed = Read<MonsterCrowdComponent>(cm, 1);
            typed.MonsterTableId = 7;
            cm.SetComponent(1, typed);
            settings.ExcludedMonsterTableIds = new[] { 7 };
            Step(cm, settings, grid, 33, _openTerrain);
            Require(grid.Count == 0, "monster type exclusion releases occupancy");
            settings.ExcludedMonsterTableIds = Array.Empty<int>();
            Step(cm, settings, grid, 34, _openTerrain);
            Require(grid.Count == 1, "monster type can rejoin");
            cm.SetComponent(1, new AIBehaviorTypeComponent { BehaviorType = AIBehaviorType.Stationary });
            Vector2 stationaryPosition = Read<TransformComponent>(cm, 1).Position;
            Step(cm, settings, grid, 35, _openTerrain);
            Require(Read<TransformComponent>(cm, 1).Position == stationaryPosition,
                "stationary actor is an obstacle even with stale requested velocity");
            cm.Reset();
            AddMonster(cm, 1, Vector2.zero, Vector2.right * 4f);
            var waypoints = new List<Vector2Int>();
            for (int i = 0; i < 64; i++)
                waypoints.Add(new Vector2Int(i + 1, 0));
            cm.AddComponent(1, new PathfindingComponent { Status = PathfindingStatus.Following, Waypoints = waypoints });
            for (int frame = 60; frame < 180; frame++)
                Step(cm, settings, grid, frame, _openTerrain);
            Require(Read<PathfindingComponent>(cm, 1).CurrentWaypointIndex < 12,
                "path lookahead advances with physical progress, not frame count");
            report.AppendLine("PASS: waiting/resume, requested velocity, feature toggle, exclusion/reapply, special motion, death, inactive, detour, terrain, static stuck, type exclusion, stationary actor, bounded path lookahead.");
        }

        private static void Measure(ComponentManager cm, int count, bool dense, StringBuilder report)
        {
            cm.Reset();
            var grid = NewGrid();
            var settings = new MonsterCrowdSettings();
            Vector2[] starts = new Vector2[count];
            int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            float spacing = dense == true ? 0.61f : 1.2f;
            for (int i = 0; i < count; i++)
            {
                starts[i] = new Vector2((i % columns - columns * 0.5f) * spacing,
                    3f + (i / columns) * spacing);
                AddMonster(cm, i + 1, starts[i], -starts[i].normalized * 4f);
            }
            long ticks = 0;
            long maximumTicks = 0;
            long allocations = 0;
            long checks = 0;
            for (int sample = -60; sample < 600; sample++)
            {
                for (int i = 0; i < count; i++)
                {
                    cm.SetComponent(i + 1, new TransformComponent { Position = starts[i] });
                    cm.SetComponent(i + 1, new MonsterCrowdComponent());
                }
                long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                Step(cm, settings, grid, sample + 100, _openTerrain);
                long elapsed = Stopwatch.GetTimestamp() - start;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
                if (sample < 0)
                    continue;
                ticks += elapsed;
                maximumTicks = Math.Max(maximumTicks, elapsed);
                allocations += allocated;
                checks += grid.CandidateChecks;
            }
            report.AppendLine($"{count} {(dense == true ? "dense" : "spread")}: avg={ticks * 1000.0 / Stopwatch.Frequency / 600:F3} ms, "
                + $"max={maximumTicks * 1000.0 / Stopwatch.Frequency:F3} ms, GC={allocations / 600.0:F1} B/step, sweepChecks={checks / 600}.");
            Require(allocations == 0, "steady state simulation allocates no managed memory");
        }

        private static void AddMonster(ComponentManager cm, int entityId, Vector2 position, Vector2 velocity)
        {
            var stat = new StatComponent { FinalMoveSpeed = 4f, FinalMoveSpeedMul = 100 };
            stat.SetCurrentHpDirect(100);
            cm.AddComponent(entityId, stat);
            cm.AddComponent(entityId, new MonsterTag());
            cm.AddComponent(entityId, new MonsterCrowdComponent());
            cm.AddComponent(entityId, new ColliderComponent { MoveRadius = 0.3f });
            cm.AddComponent(entityId, new TransformComponent { Position = position });
            cm.AddComponent(entityId, new VelocityComponent { Direction = velocity.normalized, Speed = velocity.magnitude });
        }

        private static void Step(ComponentManager cm, MonsterCrowdSettings settings, MonsterCrowdGrid grid,
            int frame, MonsterTerrainSweep terrain)
        {
            System_MonsterCrowdMovement.Simulate(cm, settings, grid, Vector2.zero, 1f / 60f,
                frame / 60f, frame, terrain, _active);
        }

        private static MonsterCrowdGrid NewGrid()
        {
            var grid = new MonsterCrowdGrid();
            grid.Initialize(64, 1f);
            grid.Begin(Vector2.zero);
            return grid;
        }

        private static T Read<T>(ComponentManager cm, int entityId) where T : struct
        {
            Require(cm.TryGetComponent<T>(entityId, out var value) == true, typeof(T).Name + " exists");
            return value;
        }

        private static Vector2 OpenTerrain(Vector2 position, Vector2 delta, float radius) => position + delta;
        private static Vector2 SealedTerrain(Vector2 position, Vector2 delta, float radius) => position;
        private static Vector2 CorridorTerrain(Vector2 position, Vector2 delta, float radius) => new Vector2(position.x + delta.x, 0f);
        private static Vector2 WallTerrain(Vector2 position, Vector2 delta, float radius)
            => new Vector2(Mathf.Min(2f - radius, position.x + delta.x), position.y + delta.y);
        private static bool IsActive(int entityId) => true;
        private static bool IsInactive(int entityId) => false;

        private static void RequireEditMode()
        {
            Require(EditorApplication.isPlaying == false, "Run in EditMode; the diagnostic owns an isolated ComponentManager.");
        }

        private static void Require(bool condition, string message)
        {
            if (condition == false)
                throw new InvalidOperationException("[MonsterCrowd] FAIL: " + message);
        }
    }
}
