# CLAUDE.md

This file provides guidance to Claude Code when working with this Unity ARPG project.

## Project Overview

Unity 6000.4.2f1 ARPG project with 2D URP setup and custom ECS architecture.

**Key Technologies:**
- Unity Input System 1.19.0 (configured in `InputSystem_Actions.inputactions`)
- Universal Render Pipeline (URP) 17.4.0
- Custom ECS (not Unity DOTS)

## Project Structure

```
Assets/Scripts/
├── Manager/          # Core managers (SystemManager, ComponentManager, DataManager, etc.)
├── Common/
│   ├── Component/    # ECS components (pure data structs)
│   ├── System/       # ECS systems (game logic)
│   ├── Interface/    # ISystem interfaces
│   └── SparseSet.cs  # ECS component storage
├── AI/               # AI behavior
├── Buff/             # Buff system
├── Skill/            # Skill system
├── Creature/         # Character implementations
└── UI/               # UI components
```

## Coding Conventions

- **Member variables**: Use underscore prefix (`_variableName`)
- **Methods order**: public → protected → private
- **Initialize()**: One-time setup
- **Reset()**: Clean up and return to initial state
- **Loop iteration**: Always use `for` over `foreach` (avoids GC)
- **Distance checks**: Use `SqrMagnitude` for comparisons (avoids sqrt)
- **Boolean conditions**: Use explicit comparisons (`== false`, `== null`) instead of `!` operator for better readability

## Custom ECS Architecture

**Core Concepts:**
- **Entity**: Integer ID
- **Component**: Struct with data only (no logic)
- **System**: Class with logic (processes components)
- **SparseSet**: O(1) component storage

### ComponentManager API

```csharp
// Add/Set component
componentManager.AddComponent<TransformComponent>(entityId, component);
componentManager.SetComponent<VelocityComponent>(entityId, component);

// Query (preferred)
if (componentManager.TryGetComponent<MovementComponent>(entityId, out var movement)) { }

// Get pool for iteration
SparseSet<TransformComponent> pool = componentManager.GetComponentPool<TransformComponent>();
```

### System Interfaces

```csharp
public interface ISystem
{
    int Priority { get; }           // Lower = earlier execution
    float UpdateInterval => 0f;     // 0 = every frame, >0 = custom interval
    void OnCreate();
    void OnReset();
}

public interface IUpdateSystem : ISystem          // 60 FPS - input, UI, rendering
{
    void OnUpdate(float inDeltaTime);
}

public interface IFixedUpdateSystem : ISystem     // 50 FPS - physics, gameplay
{
    void OnFixedUpdate(float inFixedDeltaTime);
}

public interface ILateUpdateSystem : ISystem      // Camera, final sync
{
    void OnLateUpdate(float inDeltaTime);
}
```

### System Priority Guidelines

| Priority | Purpose | Phase |
|----------|---------|-------|
| 0-50 | Input, AI perception | Update/FixedUpdate |
| 50-69 | **Village domain** (Resource/Population/Lifecycle/Construction) | FixedUpdate |
| 100-500 | Gameplay (movement, skills, buffs) | FixedUpdate/Update |
| 500-1000 | Animation, rendering | Update |

**Village domain sub-bands** (see [VILLAGE_RUNTIME_DESIGN.md](Docs/village/VILLAGE_RUNTIME_DESIGN.md)):
- 50-54: Resource (PassiveProduction)
- 55-59: Population (respawn, immigration, birth, household split)
- 60-64: Lifecycle (Tier promotion, reputation, threat level)
- 65-69: Construction (BuildQueue, Wall)

### Component Iteration Pattern

```csharp
// Single component
SparseSet<TransformComponent> pool = componentManager.GetComponentPool<TransformComponent>();
for (int i = 0; i < pool.Count; i++)
{
    int entityId = pool.GetEntityId(i);
    TransformComponent transform = pool.GetByIndex(i);
    // Process...
}

// Multi-component query
SparseSet<MovementComponent> movementPool = componentManager.GetComponentPool<MovementComponent>();
for (int i = 0; i < movementPool.Count; i++)
{
    int entityId = movementPool.GetEntityId(i);
    if (componentManager.TryGetComponent<VelocityComponent>(entityId, out var velocity))
    {
        MovementComponent movement = movementPool.GetByIndex(i);
        // Process entity with both components...
    }
}
```

## Component Design Rules

✅ **Good - Pure data:**
```csharp
public struct TransformComponent
{
    public Vector2 Position;
    public float Rotation;
    public Vector2 Scale;
}
```

❌ **Bad - Logic in component:**
```csharp
public struct BadComponent
{
    public Vector2 Position;
    public void UpdatePosition() { }  // Logic belongs in Systems
}
```

**Common Component Types:**
- Transform: `TransformComponent`, `VelocityComponent`
- Gameplay: `StateComponent`, `StatComponent`, `InputComponent`
- AI: `AIComponent`, `AIPerceptionComponent`
- Skills: `SkillComponent`, `SkillStateComponent`
- Tags: Empty structs (e.g., `AICanSeeTargetTag`)

## System Design Rules

1. **Single Responsibility**: One system = one feature
2. **Stateless**: All state in components, not in systems
3. **Priority-based execution**: Set explicit Priority value
4. **Choose correct update phase**: Update/FixedUpdate/LateUpdate
5. **Use UpdateInterval**: For non-per-frame systems

## Logic Placement Rules

Before creating a new `static` helper class, ask **in order**:

1. **Does it hold mutable state / have a lifecycle?** (needs reset, per-owner cleanup, or save/load)
   → **No static.** Put the state in a Component / manager instance / save data.
   A static class may still own the *pure logic* and receive that state as a parameter.
2. **Is it gameplay logic that runs every tick?** → `System_*` (register in `SystemManager`).
3. **Is it behavior tightly bound to one data type?** → a method/extension on that type.
4. **Is it stateless pure logic used in more than one place?** → *now* a `static` class is fine —
   keep it cohesive (single responsibility).

**Guardrails (prevent helper-class sprawl):**

- **Search for an existing home before creating a new class** — avoid duplicate / fragmented helpers.
- Constant / read-only config as `static` fields is fine. **Mutable `static` state that persists across
  operations is not** — per-entity / per-village state, caches, and shuffle bags need an owner with a
  lifecycle (see the `_recent`-style anti-pattern: can't be reset, cleaned up, or saved).
  - *Exception:* a scratch buffer `Clear()`ed before every use keeps no state between operations — a legit
    GC-avoidance pattern (e.g. `Pathfinder` A* sets, `System_VillageJobAssignment._busyEntityIds`).
- Avoid grab-bag names: `*Util`, `*Helper`, `*Common`, generic `*Manager`. The name must state a responsibility.

✅ **Good - cohesive, stateless, named for its job:**
```csharp
// Pure functions in, results out. State is passed in, not owned.
public static class DialogueTopicSelector
{
    public static DialogueTopic Select(int npcId, List<DialogueTopic> topics,
                                       AffinityTier tier, HashSet<DialogueTopicType> recent) { }
}
```

❌ **Bad - mutable state hidden in a static:**
```csharp
public static class DialogueTopicSelector
{
    // Process-global, single-instance, no owner, never reset → belongs in a Component/manager/save.
    private static readonly Dictionary<int, HashSet<DialogueTopicType>> _recent = new();
}
```

## Adding a New System

1. Create system class implementing `ISystem` interface
2. Register in `SystemManager.Initialize()` with priority comment
3. Update `ComponentManager` pool sizes for new components

```csharp
// In System file
public class System_MyFeature : IFixedUpdateSystem
{
    public int Priority => 150;
    public float UpdateInterval => 0f;

    public void OnCreate() { }

    public void OnFixedUpdate(float inFixedDeltaTime)
    {
        ComponentManager cm = GlobalManager.Instance.ComponentManager;
        SparseSet<MyComponent> pool = cm.GetComponentPool<MyComponent>();
        for (int i = 0; i < pool.Count; i++)
        {
            // System logic...
        }
    }

    public void OnReset() { }
}

// In SystemManager.Initialize()
// Priority 150: My Feature System (FixedUpdate) - Description
RegisterSystems(new System_MyFeature());
```

## Naming Conventions

- Components: `[Feature]Component` (e.g., `TransformComponent`)
- Systems: `System_[Feature]` (e.g., `System_Move`)
- Tags: `[Feature]Tag` (e.g., `AICanSeeTargetTag`)

## Performance Best Practices

- Use `for` loops (not `foreach`)
- Use `SqrMagnitude` for distance comparisons
- Cache ComponentManager references
- Use `TryGetComponent` for optional components
- Set appropriate pool sizes in ComponentManager

## Tool Execution Safety

Call independent tools **in parallel** — batch them into a single message. Fall back to
sequential execution only when a call genuinely depends on an earlier one, or when two calls
could conflict on the same target.

**Must be sequential:**
- The arguments of a call come from a previous call's output (search for a file, *then* read it)
- Two writes to the same file, or a write followed by a read of that file
- Google Sheets mutations on the same spreadsheet (`sheets_write`, `sheets_format`,
  `sheets_add_sheet`) — concurrent edits are not ordered by the API
- Anything mutating shared state: `git` commit/checkout, table re-download, asset generation

**Safe in parallel:**
- Reads of different files
- Independent `Grep` / `Glob` searches
- Read-only inspection commands (`git status`, `git diff`, `ls`)

When it is unclear whether two calls can conflict, run them sequentially.

## Wiki Reference Rules

The `Wiki/` folder is a knowledge layer that synthesizes the original docs and code. See `Wiki/_SCHEMA.md` for operating rules.

- Before starting **work that requires design, synthesis, or "why"** (architecture decisions, changes spanning multiple systems, confirming design intent):
  **read** the relevant `Wiki/<topic>.md` **first**.
- **Entry path** (funnel — go deeper only as needed):
  1. Not sure which topics the work touches? Read `Wiki/README.md` (the map) first.
  2. Then read the relevant `Wiki/<topic>.md` page(s) — include every topic the work spans, not just the primary one.
  3. Then read **only** the original `Docs/` sections the topic page points to as canonical — do not read whole doc folders.
  - If no `Wiki/<topic>.md` exists yet for a topic, fall back to the map (`Wiki/README.md`) + the original docs it lists.
- Simple single-file work (straightforward implementation, bug fixes) does not require reading the wiki (saves context).
- If the wiki and the code appear to disagree, **stop and report.** Do not fix the wiki on your own (the source of truth wins).
- If you made an important design decision during work that is not in the wiki, **propose** adding it to the relevant page (record it after human confirmation).
