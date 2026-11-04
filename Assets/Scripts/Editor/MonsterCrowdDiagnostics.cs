using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ARPG.Component;
using ARPG.Creature;
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
        private static readonly MonsterTerrainSweep _eastWallTerrain = EastWallTerrain;
        private static readonly MonsterTerrainSweep _countingOpenTerrain = CountingOpenTerrain;
        private static readonly MonsterTerrainSweep _tileCorridorTerrain = TileCorridorTerrain;
        // 지형 검사 호출 횟수. 측정·검사 직전에 0으로 비우고 쓴다.
        private static int _terrainCalls;
        // 검사용 틱 길이. 기존 계약의 시간 기준(frame / 60)을 유지한다.
        private const float StepTime = 1f / 60f;
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
                ValidatePlayerBlocking(cm, report);
                ValidateNpcBlocking(cm, report);
                ValidatePassCycles(cm, report);
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
            grid.Reset();
            grid.ApplySettings(64, 1f);
            grid.Begin(Vector2.zero);
            a = grid.Add(5, Vector2.zero, 0.3f, true);
            grid.FindNeighbors(a, Vector2.zero, 0f);
            Require(grid.Count == 1 && grid.NeighborCount == 0 && grid.OverflowCount == 0,
                "reset restores an empty initial grid");
            report.AppendLine("PASS: grid registration, negative coordinates, unlink, overflow, boundaries, generation, large radii, reset.");
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

            grid.Begin(Vector2.zero);
            grid.Add(1, Vector2.zero, 0.3f, false);
            int middle = grid.Add(2, new Vector2(0.55f, 0f), 0.3f, true);
            grid.Add(3, new Vector2(0.75f, 0f), 0.3f, false);
            for (int iteration = 0; iteration < 120; iteration++)
                grid.SetPosition(middle, grid.GetBody(middle).Position + MonsterCrowdMotion.RecoverOverlap(grid, middle, 0.02f, _openTerrain));
            float middleX = grid.GetBody(middle).Position.x;
            Require(Mathf.Abs((0.6f - middleX) - (middleX - 0.15f)) < 0.02f,
                "pinned three-body overlap balances instead of freezing");

            grid.Begin(Vector2.zero);
            grid.Add(1, Vector2.zero, 0.3f, false);
            int touching = grid.Add(2, new Vector2(0.6f, 0f), 0.3f, true);
            grid.Add(3, new Vector2(0.9f, 0.3f), 0.3f, false);
            for (int iteration = 0; iteration < 120; iteration++)
                grid.SetPosition(touching, grid.GetBody(touching).Position + MonsterCrowdMotion.RecoverOverlap(grid, touching, 0.02f, _openTerrain));
            Vector2 deflected = grid.GetBody(touching).Position;
            Require(deflected.sqrMagnitude >= 0.3599f && (deflected - new Vector2(0.9f, 0.3f)).sqrMagnitude > 0.35f,
                "touching neighbor deflects overlap recovery instead of freezing it");
            report.AppendLine("PASS: swept contact, no tunneling, escape, tangent, identical-position recovery, pinned three-body balance, touching deflection.");
        }

        private static void ValidateSimulation(ComponentManager cm, StringBuilder report)
        {
            var grid = NewGrid();
            var settings = new MonsterCrowdSettings();
            AddMonster(cm, 1, Vector2.zero, Vector2.right * 4f);
            AddMonster(cm, 2, Vector2.right * 0.625f, Vector2.zero);
            cm.AddComponent(1, new StateComponent { MoveState = MovementStates.Walking, MovementStatePrev = MovementStates.Walking });
            Step(cm, settings, grid, 1, _corridorTerrain);
            Require(cm.TryGetComponent<MonsterCrowdComponent>(1, out var state) == true && state.IsWaiting == true,
                "narrow corridor waits behind a stationary attacker");
            Require(state.WaitBlockerId == 2, "crowd wait records the blocking monster");
            Require(cm.TryGetComponent<VelocityComponent>(1, out var velocity) == true && velocity.Speed == 4f,
                "waiting preserves requested speed");
            Require(Read<StateComponent>(cm, 1).MoveState == MovementStates.Walking,
                "crowd movement leaves MoveState to System_Move");
            for (int frame = 2; frame <= 150; frame++)
                Step(cm, settings, grid, frame, _corridorTerrain);
            Require(Read<MonsterCrowdComponent>(cm, 1).IsWaiting == true && Read<TransformComponent>(cm, 1).Position.x < 0.01f,
                "queue behind an attacker it does not block keeps waiting past the pass delay");
            cm.SetComponent(2, new TransformComponent { Position = Vector2.right * 3f });
            Step(cm, settings, grid, 170, _corridorTerrain);
            Require(cm.TryGetComponent<TransformComponent>(1, out var position) == true && position.Position.x > 0.01f,
                "waiting resumes after a gap opens");
            settings.Enabled = false;
            Vector2 previousPosition = position.Position;
            Step(cm, settings, grid, 21, _openTerrain);
            Require(Read<ResolvedVelocityComponent>(cm, 1).Active == false
                && Read<TransformComponent>(cm, 1).Position == previousPosition && grid.Count == 0,
                "disabled module hands motion back to base movement");
            settings.Enabled = true;
            cm.AddComponent(1, new MonsterCrowdExcludedTag());
            Step(cm, settings, grid, 22, _openTerrain);
            Require(grid.Count == 1 && Read<ResolvedVelocityComponent>(cm, 1).Active == false,
                "excluded actor has neither movement nor occupancy");
            cm.RemoveComponent<MonsterCrowdExcludedTag>(1);
            Step(cm, settings, grid, 23, _openTerrain);
            Require(grid.Count == 2 && Read<ResolvedVelocityComponent>(cm, 1).Active == true,
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
            System_MonsterCrowdMovement.Simulate(cm, settings, grid, -1, Vector2.zero, 1f / 60f, 1f, 28, _openTerrain, _inactive);
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

            cm.Reset();
            AddMonster(cm, 1, Vector2.zero, Vector2.right * 4f);
            AddMonster(cm, 2, Vector2.right * 0.625f, Vector2.left * 4f);
            for (int frame = 200; frame < 230; frame++)
                Step(cm, settings, grid, frame, _corridorTerrain);
            Require(Read<MonsterCrowdComponent>(cm, 1).IsWaiting == true && Read<MonsterCrowdComponent>(cm, 2).IsWaiting == true
                && Read<TransformComponent>(cm, 1).Position.x < Read<TransformComponent>(cm, 2).Position.x,
                "head-on actors wait before the pass delay");
            for (int frame = 230; frame < 380; frame++)
                Step(cm, settings, grid, frame, _corridorTerrain);
            Require(Read<TransformComponent>(cm, 1).Position.x > Read<TransformComponent>(cm, 2).Position.x + 0.6f,
                "head-on actors blocking each other pass after the pass delay");
            Require(Read<MonsterCrowdComponent>(cm, 1).PassUntil < 379f / 60f && Read<MonsterCrowdComponent>(cm, 2).PassUntil < 379f / 60f,
                "pass state ends after the pair separates");

            cm.Reset();
            AddMonster(cm, 1, Vector2.zero, Vector2.right * 4f);
            AddMonster(cm, 2, Vector2.right * 0.625f, Vector2.zero);
            cm.AddComponent(2, new AIBehaviorTypeComponent { BehaviorType = AIBehaviorType.Stationary });
            for (int frame = 400; frame < 580; frame++)
                Step(cm, settings, grid, frame, _corridorTerrain);
            Require(Read<TransformComponent>(cm, 1).Position.x > 1.3f
                && Mathf.Abs(Read<TransformComponent>(cm, 2).Position.x - 0.625f) < 0.0001f,
                "long wait behind an immovable actor passes through it");

            // 경유점 (5,0)의 중심 (5.5,0.5)이 정확히 동쪽이 되도록 타일 중심 높이에 둔다.
            Vector2 flankedStart = new Vector2(0.5f, 0.5f);
            cm.Reset();
            AddMonster(cm, 1, flankedStart, Vector2.right * 4f);
            AddMonster(cm, 2, flankedStart + Vector2.up * 0.625f, Vector2.zero);
            AddMonster(cm, 3, flankedStart + Vector2.down * 0.625f, Vector2.zero);
            cm.AddComponent(1, new PathfindingComponent { Status = PathfindingStatus.Following,
                Waypoints = new List<Vector2Int> { new Vector2Int(5, 0) } });
            for (int frame = 600; frame < 630; frame++)
                Step(cm, settings, grid, frame, _eastWallTerrain);
            var flanked = Read<MonsterCrowdComponent>(cm, 1);
            Require(flanked.IsWaiting == true && flanked.WaitBlockerId == 0,
                "wait with terrain ahead and monsters beside records no blocker");
            Require(Read<PathfindingComponent>(cm, 1).LastProgressTime == 0f,
                "terrain-ahead wait leaves A* stuck recovery active");

            // 경유점 (5,0)의 중심 높이(0.5)에 놓인 통로라 진행 방향이 통로와 정확히 나란하다.
            cm.Reset();
            AddMonster(cm, 1, new Vector2(0f, 0.5f), Vector2.right * 4f);
            AddMonster(cm, 2, new Vector2(0.625f, 0.5f), Vector2.zero);
            cm.AddComponent(1, new PathfindingComponent { Status = PathfindingStatus.Following,
                Waypoints = new List<Vector2Int> { new Vector2Int(5, 0) } });
            for (int frame = 650; frame < 680; frame++)
                Step(cm, settings, grid, frame, _tileCorridorTerrain);
            var blockedWait = Read<MonsterCrowdComponent>(cm, 1);
            var blockedPath = Read<PathfindingComponent>(cm, 1);
            Require(blockedWait.IsWaiting == true && blockedWait.WaitBlockerId == 2 && blockedPath.LastProgressTime == 679 / 60f,
                $"monster-blocked wait defers A* stuck recovery (waiting={blockedWait.IsWaiting}, blocker={blockedWait.WaitBlockerId}, "
                + $"lastProgress={blockedPath.LastProgressTime}, x={Read<TransformComponent>(cm, 1).Position.x})");

            // 진행 방향이 통로와 약 5도 어긋나면 막힌 옆 후보가 벽에 미끄러져 뒤쪽 성분만 남는다. 그 후보를 고르지 않고 기다려야 한다.
            cm.Reset();
            AddMonster(cm, 1, Vector2.zero, new Vector2(0.9959f, -0.0905f) * 4f);
            AddMonster(cm, 2, Vector2.right * 0.625f, Vector2.zero);
            bool steppedBack = false;
            for (int frame = 690; frame < 720; frame++)
            {
                Step(cm, settings, grid, frame, _corridorTerrain);
                steppedBack |= Read<TransformComponent>(cm, 1).Position.x < -0.0001f;
            }
            var offAxis = Read<MonsterCrowdComponent>(cm, 1);
            Require(steppedBack == false && offAxis.IsWaiting == true && offAxis.WaitBlockerId == 2,
                $"off-axis corridor wait does not step backward (steppedBack={steppedBack}, waiting={offAxis.IsWaiting}, blocker={offAxis.WaitBlockerId})");

            ValidateFollowing(cm, settings, grid, 700, _corridorTerrain, false,
                "follower behind a same-speed leader keeps pace without waiting");
            ValidateFollowing(cm, settings, grid, 800, _openTerrain, true,
                "follower in open ground does not sidestep a same-speed leader");

            cm.Reset();
            AddMonster(cm, 1, Vector2.zero, Vector2.right * 4f);
            AddMonster(cm, 2, Vector2.right * 0.625f, Vector2.right * 4f);
            AddMonster(cm, 3, Vector2.right * 1.25f, Vector2.zero);
            SetSteadyMotion(cm, 1, Vector2.right * 4f, 900);
            SetSteadyMotion(cm, 2, Vector2.right * 4f, 900);
            for (int frame = 900; frame < 930; frame++)
                Step(cm, settings, grid, frame, _corridorTerrain);
            Require((Read<TransformComponent>(cm, 2).Position - Read<TransformComponent>(cm, 1).Position).sqrMagnitude >= 0.3599f,
                "leader prediction does not keep a follower overlapped when the leader stops");

            // 혼자 막힘 없이 가는 개체는 이동 검사가 프레임마다 지형 검사 1번이다. 나머지는 경로 앞보기 몫이다.
            int lookAheadFrames = settings.PathLookAheadTicks;
            int frames = lookAheadFrames * 2;
            cm.Reset();
            AddMonster(cm, 1, new Vector2(10f, 10f), Vector2.right * 4f);
            var distantWaypoints = new List<Vector2Int>();
            for (int i = 0; i < 6; i++)
                distantWaypoints.Add(new Vector2Int(i, 0));
            cm.AddComponent(1, new PathfindingComponent { Status = PathfindingStatus.Following, Waypoints = distantWaypoints });
            _terrainCalls = 0;
            for (int frame = 1000; frame < 1000 + frames; frame++)
                Step(cm, settings, grid, frame, _countingOpenTerrain);
            Require(_terrainCalls == frames, "off-path actor does not sweep toward its distant current waypoint");

            cm.Reset();
            AddMonster(cm, 1, new Vector2(0.5f, 0.5f), Vector2.right * 4f);
            var nearWaypoints = new List<Vector2Int>();
            for (int i = 0; i < 12; i++)
                nearWaypoints.Add(new Vector2Int(1, 0));
            cm.AddComponent(1, new PathfindingComponent { Status = PathfindingStatus.Following, Waypoints = nearWaypoints });
            _terrainCalls = 0;
            for (int frame = 1100; frame < 1100 + frames; frame++)
                Step(cm, settings, grid, frame, _countingOpenTerrain);
            Require(_terrainCalls == frames + frames / lookAheadFrames,
                "path lookahead sweeps only on its staggered frames");

            // 위치는 Render 몫이다. 고정 틱 계산은 확정 속도만 내고 TransformComponent를 바꾸지 않는다.
            cm.Reset();
            AddMonster(cm, 1, Vector2.zero, Vector2.right * 4f);
            Simulate(cm, settings, grid, 1300, _openTerrain);
            var handoff = Read<ResolvedVelocityComponent>(cm, 1);
            Require(Read<TransformComponent>(cm, 1).Position == Vector2.zero && handoff.Active == true
                && (handoff.Velocity - Vector2.right * 4f).sqrMagnitude < 0.0001f,
                "crowd outputs a resolved velocity and leaves position to Render");
            report.AppendLine("PASS: waiting/resume, blocker record, MoveState ownership, queue past pass delay, requested velocity, feature toggle, exclusion/reapply, special motion, death, inactive, detour, terrain, static stuck, type exclusion, stationary actor, bounded path lookahead, head-on pass, pass end, immovable pass, terrain-ahead wait, terrain wait keeps A* stuck, monster wait defers A* stuck, off-axis corridor wait, corridor following, open following, stopped leader, no current-waypoint sweep, staggered lookahead, velocity handoff.");
        }

        private static void ValidatePlayerBlocking(ComponentManager cm, StringBuilder report)
        {
            var grid = NewGrid();
            var settings = new MonsterCrowdSettings();
            const int player = 100;
            Vector2 monster = Vector2.right * 0.625f;

            cm.Reset();
            AddMonster(cm, 1, monster, Vector2.zero);
            AddPlayer(cm, player, Vector2.zero, Vector2.right * 4f);
            for (int frame = 1; frame <= 30; frame++)
                Step(cm, settings, grid, frame, _openTerrain, player);
            Require(Read<TransformComponent>(cm, player).Position.x < 0.001f && Read<TransformComponent>(cm, 1).Position == monster,
                "player walking into a monster stops and the monster does not move");

            cm.Reset();
            AddMonster(cm, 1, monster, Vector2.zero);
            AddPlayer(cm, player, Vector2.zero, new Vector2(1f, 1f).normalized * 4f);
            for (int frame = 1; frame <= 30; frame++)
                Step(cm, settings, grid, frame, _openTerrain, player);
            Vector2 slid = Read<TransformComponent>(cm, player).Position;
            Require(slid.y > 0.5f && (slid - monster).sqrMagnitude >= 0.3599f && Read<TransformComponent>(cm, 1).Position == monster,
                "player slides around a monster it brushes against");

            cm.Reset();
            AddMonster(cm, 1, monster, Vector2.zero);
            AddPlayer(cm, player, Vector2.right * 0.3f, Vector2.right * 4f);
            for (int frame = 1; frame <= 10; frame++)
                Step(cm, settings, grid, frame, _openTerrain, player);
            Require(Read<TransformComponent>(cm, player).Position.x <= 0.3001f, "overlapped player cannot push deeper into a monster");
            cm.SetComponent(player, new VelocityComponent { Direction = Vector2.left, Speed = 4f });
            for (int frame = 11; frame <= 20; frame++)
                Step(cm, settings, grid, frame, _openTerrain, player);
            Require(Read<TransformComponent>(cm, player).Position.x < 0f, "overlapped player can walk out of a monster");

            // 몬스터 계산은 플레이어를 모른다. 공격 사거리에서 멈추는 것은 AI 몫이다.
            cm.Reset();
            AddMonster(cm, 1, Vector2.right * 2f, Vector2.left * 4f);
            AddPlayer(cm, player, Vector2.zero, Vector2.zero);
            for (int frame = 1; frame <= 30; frame++)
                Step(cm, settings, grid, frame, _openTerrain, player);
            Require(Read<TransformComponent>(cm, 1).Position.x < 0.3f, "monster movement ignores the player body");

            settings.PlayerBlockedByMonsters = false;
            Step(cm, settings, grid, 31, _openTerrain, player);
            Require(Read<ResolvedVelocityComponent>(cm, player).Active == false,
                "turning player blocking off hands player motion back to base movement");
            settings.PlayerBlockedByMonsters = true;
            Step(cm, settings, grid, 32, _openTerrain, player);
            settings.Enabled = false;
            Step(cm, settings, grid, 33, _openTerrain, player);
            Require(Read<ResolvedVelocityComponent>(cm, player).Active == false,
                "disabled crowd hands player motion back to base movement");
            report.AppendLine("PASS: player stops at a monster, slides around it, cannot push deeper, walks out, monsters ignore the player, toggles hand motion back.");
        }

        private static void ValidateNpcBlocking(ComponentManager cm, StringBuilder report)
        {
            var grid = NewGrid();
            var settings = new MonsterCrowdSettings();
            const int npc = 200;
            Vector2 monster = Vector2.right * 0.625f;

            cm.Reset();
            AddMonster(cm, 1, monster, Vector2.zero);
            AddNpc(cm, npc, Vector2.zero, Vector2.right * 4f);
            cm.AddComponent(npc, new PathfindingComponent { Status = PathfindingStatus.Following,
                Waypoints = new List<Vector2Int> { new Vector2Int(5, 0) } });
            for (int frame = 1; frame <= 30; frame++)
                Step(cm, settings, grid, frame, _openTerrain);
            Require(Read<TransformComponent>(cm, npc).Position.x < 0.001f && Read<TransformComponent>(cm, 1).Position == monster,
                "NPC walking into a monster stops and the monster does not move");
            Require(Read<PathfindingComponent>(cm, npc).LastProgressTime == 30 / 60f,
                "NPC blocked by a monster defers A* stuck recovery");

            cm.Reset();
            AddMonster(cm, 1, monster, Vector2.zero);
            AddNpc(cm, npc, Vector2.zero, new Vector2(1f, 1f).normalized * 4f);
            for (int frame = 1; frame <= 30; frame++)
                Step(cm, settings, grid, frame, _openTerrain);
            Vector2 slid = Read<TransformComponent>(cm, npc).Position;
            Require(slid.y > 0.5f && (slid - monster).sqrMagnitude >= 0.3599f,
                "NPC slides around a monster it brushes against");

            cm.Reset();
            AddMonster(cm, 1, Vector2.right * 2f, Vector2.left * 4f);
            AddNpc(cm, npc, Vector2.zero, Vector2.zero);
            for (int frame = 1; frame <= 30; frame++)
                Step(cm, settings, grid, frame, _openTerrain);
            Require(Read<TransformComponent>(cm, 1).Position.x < 0.3f, "monster movement ignores NPC bodies");

            System_MonsterCrowdMovement.Simulate(cm, settings, grid, -1, Vector2.zero, StepTime, 31 / 60f, 31,
                _openTerrain, _inactive);
            Require(Read<ResolvedVelocityComponent>(cm, npc).Active == false,
                "inactive NPC hands motion back to base movement");
            Step(cm, settings, grid, 32, _openTerrain);
            settings.NpcBlockedByMonsters = false;
            Step(cm, settings, grid, 33, _openTerrain);
            Require(Read<ResolvedVelocityComponent>(cm, npc).Active == false,
                "turning NPC blocking off hands NPC motion back to base movement");
            report.AppendLine("PASS: NPC stops at a monster, defers A* stuck while blocked, slides around it, monsters ignore NPCs, inactive and toggle hand motion back.");
        }

        private static void ValidatePassCycles(ComponentManager cm, StringBuilder report)
        {
            // 격자 단위: 세 마리 고리 1→2→3→1, 고리에 붙은 꼬리 4→1, 기다리지 않는 앞 개체로 끊기는 줄 5→6
            var grid = NewGrid();
            int a = grid.Add(1, new Vector2(0f, 0f), 0.3f, true, 1f, 2);
            int b = grid.Add(2, new Vector2(2f, 0f), 0.3f, true, 1f, 3);
            int c = grid.Add(3, new Vector2(4f, 0f), 0.3f, true, 1f, 1);
            int d = grid.Add(4, new Vector2(6f, 0f), 0.3f, true, 1f, 1);
            int e = grid.Add(5, new Vector2(8f, 0f), 0.3f, true, 1f, 6);
            int f = grid.Add(6, new Vector2(10f, 0f), 0.3f, true);
            grid.ResolvePassCycles();
            Require(grid.GetBody(a).CanPassThrough(grid.GetBody(b)) && grid.GetBody(b).CanPassThrough(grid.GetBody(c))
                && grid.GetBody(c).CanPassThrough(grid.GetBody(a)),
                "three-body pass cycle lets each pass its partner");
            Require(grid.GetBody(d).CanPassThrough(grid.GetBody(a)) == false
                && grid.GetBody(e).CanPassThrough(grid.GetBody(f)) == false,
                "cycle tail and a queue ending at a non-waiting body are not a pass cycle");
            grid.Begin(Vector2.zero);
            int g = grid.Add(7, Vector2.zero, 0.3f, true, 1f, 8);
            int h = grid.Add(8, Vector2.right * 2f, 0.3f, true, 1f, 7);
            grid.ResolvePassCycles();
            Require(grid.GetBody(g).CanPassThrough(grid.GetBody(h)) && grid.GetBody(h).CanPassThrough(grid.GetBody(g)),
                "two bodies naming each other stay a pass cycle");

            // 실제 흐름: 비켜 가기를 끄고 네 마리가 네모로 서로 막는 교착(1→2→3→4→1)
            var settings = new MonsterCrowdSettings { AvoidanceEnabled = false };
            grid = NewGrid();
            cm.Reset();
            Vector2[] starts = { Vector2.zero, Vector2.right * 0.625f, new Vector2(0.625f, 0.625f), Vector2.up * 0.625f };
            Vector2[] headings = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            for (int i = 0; i < 4; i++)
                AddMonster(cm, i + 1, starts[i], headings[i] * 4f);
            for (int frame = 1300; frame < 1330; frame++)
                Step(cm, settings, grid, frame, _openTerrain);
            bool allWaiting = true;
            for (int i = 0; i < 4; i++)
                allWaiting &= Read<MonsterCrowdComponent>(cm, i + 1).IsWaiting
                    && (Read<TransformComponent>(cm, i + 1).Position - starts[i]).sqrMagnitude < 0.0001f;
            Require(allWaiting, "four-body gridlock waits before the pass delay");
            for (int frame = 1330; frame < 1480; frame++)
                Step(cm, settings, grid, frame, _openTerrain);
            bool allMoved = true;
            for (int i = 0; i < 4; i++)
                allMoved &= Vector2.Dot(Read<TransformComponent>(cm, i + 1).Position - starts[i], headings[i]) > 0.5f;
            Require(allMoved, "four-body gridlock passes after the pass delay");
            report.AppendLine("PASS: three-body cycle, tail and queue excluded, two-body cycle, four-body gridlock resolves.");
        }

        private static void ValidateFollowing(ComponentManager cm, MonsterCrowdSettings settings, MonsterCrowdGrid grid,
            int startFrame, MonsterTerrainSweep terrain, bool requireStraight, string message)
        {
            cm.Reset();
            AddMonster(cm, 1, Vector2.zero, Vector2.right * 4f);
            AddMonster(cm, 2, Vector2.right * 0.625f, Vector2.right * 4f);
            SetSteadyMotion(cm, 1, Vector2.right * 4f, startFrame);
            SetSteadyMotion(cm, 2, Vector2.right * 4f, startFrame);
            bool waited = false;
            for (int frame = startFrame; frame < startFrame + 60; frame++)
            {
                Step(cm, settings, grid, frame, terrain);
                waited |= Read<MonsterCrowdComponent>(cm, 1).IsWaiting;
            }
            Vector2 follower = Read<TransformComponent>(cm, 1).Position;
            Vector2 leader = Read<TransformComponent>(cm, 2).Position;
            Require(waited == false && follower.x > 3.8f && (leader - follower).sqrMagnitude >= 0.3599f
                && (requireStraight == false || Mathf.Abs(follower.y) < 0.001f), message);
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
            long queries = 0;
            long terrainCalls = 0;
            for (int sample = -60; sample < 600; sample++)
            {
                for (int i = 0; i < count; i++)
                {
                    cm.SetComponent(i + 1, new TransformComponent { Position = starts[i] });
                    cm.SetComponent(i + 1, new MonsterCrowdComponent());
                }
                _terrainCalls = 0;
                long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                // 위치는 매 스텝 되돌리므로 Render 몫의 위치 적분은 하지 않고 고정 틱 계산만 잰다.
                Simulate(cm, settings, grid, sample + 100, _countingOpenTerrain);
                long elapsed = Stopwatch.GetTimestamp() - start;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
                if (sample < 0)
                    continue;
                ticks += elapsed;
                maximumTicks = Math.Max(maximumTicks, elapsed);
                allocations += allocated;
                checks += grid.CandidateChecks;
                queries += grid.NeighborQueries;
                terrainCalls += _terrainCalls;
            }
            report.AppendLine($"{count} {(dense == true ? "dense" : "spread")}: avg={ticks * 1000.0 / Stopwatch.Frequency / 600:F3} ms, "
                + $"max={maximumTicks * 1000.0 / Stopwatch.Frequency:F3} ms, GC={allocations / 600.0:F1} B/step, sweepChecks={checks / 600}, "
                + $"neighborQueries={queries / 600}, terrainCalls={terrainCalls / 600}.");
            Require(allocations == 0, "steady state simulation allocates no managed memory");
        }

        private static void AddMonster(ComponentManager cm, int entityId, Vector2 position, Vector2 velocity)
        {
            var stat = new StatComponent { FinalMoveSpeed = 4f, FinalMoveSpeedMul = 100 };
            stat.SetCurrentHpDirect(100);
            cm.AddComponent(entityId, stat);
            cm.AddComponent(entityId, new MonsterTag());
            cm.AddComponent(entityId, new MonsterCrowdComponent());
            cm.AddComponent(entityId, new ResolvedVelocityComponent());
            cm.AddComponent(entityId, new ColliderComponent { MoveRadius = 0.3f });
            cm.AddComponent(entityId, new TransformComponent { Position = position });
            cm.AddComponent(entityId, new VelocityComponent { Direction = velocity.normalized, Speed = velocity.magnitude });
        }

        /// <summary>직전 틱에 군집 이동이 velocity로 움직이던 상태로 만든다 (예측 이동 검사용).</summary>
        private static void SetSteadyMotion(ComponentManager cm, int entityId, Vector2 velocity, int frame)
        {
            cm.SetComponent(entityId, new MonsterCrowdComponent { ActualVelocity = velocity });
            cm.SetComponent(entityId, new ResolvedVelocityComponent { Velocity = velocity, Active = true });
        }

        /// <summary>고정 틱 계산 한 번 + Render 몫의 위치 적분(확정 속도 × 시간). 지형은 계산 때 이미 반영됐다.</summary>
        private static void Step(ComponentManager cm, MonsterCrowdSettings settings, MonsterCrowdGrid grid,
            int frame, MonsterTerrainSweep terrain, int playerId = -1)
        {
            Simulate(cm, settings, grid, frame, terrain, playerId);
            SparseSet<ResolvedVelocityComponent> resolvedPool = cm.GetComponentPool<ResolvedVelocityComponent>();
            for (int i = 0; i < resolvedPool.Count; i++)
            {
                ResolvedVelocityComponent resolved = resolvedPool.GetByIndex(i);
                int entityId = resolvedPool.GetEntityId(i);
                if (resolved.Active == false || cm.TryGetComponent<TransformComponent>(entityId, out var transform) == false)
                    continue;
                transform.Position += resolved.Velocity * StepTime;
                cm.SetComponent(entityId, transform);
            }
        }

        private static void Simulate(ComponentManager cm, MonsterCrowdSettings settings, MonsterCrowdGrid grid,
            int frame, MonsterTerrainSweep terrain, int playerId = -1)
        {
            System_MonsterCrowdMovement.Simulate(cm, settings, grid, playerId, Vector2.zero, StepTime,
                frame / 60f, frame, terrain, _active);
        }

        /// <summary>실체화된 NPC처럼 NpcTag와 이동·충돌·확정 속도 칸을 가진 개체.</summary>
        private static void AddNpc(ComponentManager cm, int entityId, Vector2 position, Vector2 velocity)
        {
            cm.AddComponent(entityId, new NpcTag());
            AddPlayer(cm, entityId, position, velocity);
        }

        /// <summary>플레이어처럼 MonsterTag·군집 컴포넌트 없이 이동·충돌·확정 속도 칸만 가진 개체.</summary>
        private static void AddPlayer(ComponentManager cm, int entityId, Vector2 position, Vector2 velocity)
        {
            cm.AddComponent(entityId, new ColliderComponent { MoveRadius = 0.3f });
            cm.AddComponent(entityId, new TransformComponent { Position = position });
            cm.AddComponent(entityId, new VelocityComponent { Direction = velocity.normalized, Speed = velocity.magnitude });
            cm.AddComponent(entityId, new ResolvedVelocityComponent());
        }

        private static MonsterCrowdGrid NewGrid()
        {
            var grid = new MonsterCrowdGrid();
            grid.Initialize();
            grid.ApplySettings(64, 1f);
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
        private static Vector2 TileCorridorTerrain(Vector2 position, Vector2 delta, float radius) => new Vector2(position.x + delta.x, 0.5f);
        private static Vector2 WallTerrain(Vector2 position, Vector2 delta, float radius)
            => new Vector2(Mathf.Min(2f - radius, position.x + delta.x), position.y + delta.y);
        private static Vector2 EastWallTerrain(Vector2 position, Vector2 delta, float radius)
            => new Vector2(position.x + Mathf.Min(0f, delta.x), position.y + delta.y);
        private static Vector2 CountingOpenTerrain(Vector2 position, Vector2 delta, float radius)
        {
            _terrainCalls++;
            return position + delta;
        }
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
