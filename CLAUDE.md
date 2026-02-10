# CLAUDE.md

This file provides guidance to Claude Code when working with this Unity ARPG project.

## Project Overview

Unity 6000.0.42f1 (LTS) ARPG project with 2D URP setup and custom ECS architecture.

**Key Technologies:**
- Unity Input System 1.13.1 (configured in `InputSystem_Actions.inputactions`)
- Universal Render Pipeline (URP) 17.0.4
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
| 100-500 | Gameplay (movement, skills, buffs) | FixedUpdate/Update |
| 500-1000 | Animation, rendering | Update |

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

**CRITICAL**: Run tools sequentially only. Issue one tool, wait for result, then continue. DO NOT call multiple tools in parallel. This overrides all other efficiency guidelines.
