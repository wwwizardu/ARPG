# ECS Performance Optimization Plan

> Target files: `SparseSet<T>`, `ComponentManager`, `EntityIdHelper`, hot ECS systems
>
> Goal: reduce per-frame component lookup cost without changing gameplay behavior.

---

## 1. Current State

### 1.1 Component Storage

Current component pools use `SparseSet<T>`.

```text
SparseSet<T>
  _sparse: Dictionary<int, int>   // EntityId -> dense index
  _dense:  int[]                  // dense index -> EntityId
  _data:   T[]                    // dense component data
```

The dense side is good. Systems already iterate component data with `GetComponentPool<T>()`, `GetEntityId(i)`, and `GetByIndex(i)`.

The weak point is `_sparse`. It is a `Dictionary<int, int>`, so every cross-component lookup still pays hash lookup cost.

Examples:

- `System_Projectile` iterates projectiles, then looks up `TransformComponent`, `VelocityComponent`, `SkillComponent`, `FactionComponent`, `StatComponent`, `JumpComponent`, `ColliderComponent`.
- `System_Render` iterates transforms, then looks up velocity/collider/jump.
- `System_Skill` iterates skills, then performs many owner/target component lookups.
- `FactionHelper` iterates factions and looks up transform/stat.

### 1.2 EntityId Shape

General runtime entities use small recycled IDs.

```text
CreateEntity()
  1, 2, 3, ...
  destroyed IDs are queued and reused
```

But some entities use deterministic large IDs.

```text
Buff:         targetId + (buffTableId + 1) * 100_000
Skill:        ownerId  + (slotIndex + 1)   * 1_000_000
Relationship: fromId   + (toId + 1)        * 10_000_000
```

Because of this, `sparse[entityId]` cannot blindly allocate up to the largest EntityId. A relationship ID can be huge.

---

## 2. Optimization Strategy

Use staged changes.

```text
Phase 1: SparseSet internal optimization
  low-risk, mostly one file, keeps public API

Phase 1.5: Projectile hot-loop optimization
  keep ECS shape, reduce collision candidates and manager lookup calls

Phase 2: deterministic ID cleanup
  medium/high-risk, changes EntityIdHelper contracts

Phase 3: hot component mapping table
  optional, only if profiling still shows lookup cost

Phase 4: Entity generation safety
  correctness/safety refactor, not primarily performance
```

Do not implement all phases at once.

---

## 3. Phase 0: Measurement Baseline

Before changing behavior, capture a baseline.

### 3.1 Profiler Markers

Add temporary or editor-only profiler markers around:

- `ComponentManager.TryGetComponent<T>`
- `ComponentManager.HasComponent<T>`
- `SparseSet<T>.TryGet`
- `SparseSet<T>.Contains`
- hot systems:
  - `System_Projectile.OnFixedUpdate`
  - `System_Projectile.CheckCollision`
  - `System_Skill.OnFixedUpdate`
  - `System_Render.OnUpdate`
  - `System_AI_Perception.OnFixedUpdate`

### 3.2 Capture Scenarios

Use at least three repeatable scenes/scenarios.

| Scenario | Purpose |
|----------|---------|
| Normal field | everyday entity/component count |
| Projectile-heavy combat | worst current lookup pattern |
| Village/NPC-heavy scene | many non-combat entity references |

### 3.3 Baseline Metrics

Record:

- total `TryGetComponent` count per frame
- total `HasComponent` count per frame
- main thread frame time
- GC allocations per frame
- system-specific time for projectile/skill/render/AI

Exit criteria for Phase 0:

- Have one profiler capture before changes.
- Know whether projectile/skill/render are the top lookup-heavy systems.

---

## 4. Phase 1: SparseSet Array + Fallback Dictionary

Status: implemented in `Assets/Scripts/Common/SparseSet.cs`.

Current implementation keeps the public `SparseSet<T>` API unchanged. Normal small EntityIds use the direct sparse array path. Large deterministic IDs, negative sentinel-like IDs if ever inserted, and any ID outside the direct cap use fallback Dictionary storage.

### 4.1 Goal

Replace `Dictionary<int, int> _sparse` with:

```text
small EntityId path:
  int[] _sparse

large EntityId path:
  Dictionary<int, int> _fallbackSparse
```

This keeps actual component data dense and unchanged.

```text
_sparse[entityId] = denseIndex + 1
0 means "component not present"
```

The `+1` is needed because dense index `0` is valid.

### 4.2 Why This Fits Current Project

General entities are small and recycled, so they benefit from array lookup.

Deterministic Skill/Buff/Relationship IDs are large, so they stay in fallback Dictionary until Phase 2.

### 4.3 Proposed `SparseSet<T>` Fields

```csharp
private const int DefaultSparseCapacity = 1024;
private const int MaxDirectEntityId = 65536;

private int[] _sparse;
private readonly Dictionary<int, int> _fallbackSparse;
private int[] _dense;
private T[] _data;
private int _count;
```

Notes:

- `MaxDirectEntityId` should be configurable or at least easy to change.
- `65536` means each direct sparse array is at most about 256 KB per component pool.
- Use dynamic growth so small pools do not immediately allocate the full max size.

### 4.4 Direct vs Fallback Rule

```csharp
private static bool CanUseDirectSparse(int entityId)
{
    return entityId >= 0 && entityId < MaxDirectEntityId;
}
```

If `entityId` is direct but larger than current `_sparse.Length`, grow `_sparse` up to `MaxDirectEntityId`.

If growing would exceed `MaxDirectEntityId`, use `_fallbackSparse`.

### 4.5 Required Helper Methods

Add private helpers inside `SparseSet<T>`:

```csharp
private bool TryGetDenseIndex(int entityId, out int denseIndex);
private void SetSparseIndex(int entityId, int denseIndex);
private void RemoveSparseIndex(int entityId);
private void EnsureSparseCapacity(int entityId);
```

All public methods should use these helpers.

Affected public methods:

- `Add`
- `Set`
- `Get`
- `Contains`
- `TryGet`
- `Remove`

### 4.6 Add/Set Flow

```text
Add/Set(entityId, value)
  if TryGetDenseIndex(entityId)
    _data[denseIndex] = value
    return

  ensure dense capacity
  denseIndex = _count
  _dense[denseIndex] = entityId
  _data[denseIndex] = value
  SetSparseIndex(entityId, denseIndex)
  _count++
```

### 4.7 Remove Flow

`Remove` must update sparse mapping after swap-and-pop.

```text
Remove(entityId)
  if not found: return

  denseIndex = found index
  lastIndex = _count - 1

  if denseIndex != lastIndex
    lastEntityId = _dense[lastIndex]
    _dense[denseIndex] = lastEntityId
    _data[denseIndex] = _data[lastIndex]
    SetSparseIndex(lastEntityId, denseIndex)

  RemoveSparseIndex(entityId)
  _count--
```

This is critical. If `lastEntityId` is not remapped, later lookups can read stale data.

### 4.8 Compatibility

No public API changes in Phase 1.

Expected unchanged call sites:

- `ComponentManager.AddComponent`
- `ComponentManager.SetComponent`
- `ComponentManager.TryGetComponent`
- `ComponentManager.HasComponent`
- all systems using `GetComponentPool<T>()`

### 4.9 Phase 1 Validation

Manual/component-level checks:

- Add component to small ID, retrieve it.
- Set component to small ID, retrieve updated value.
- Remove component from small ID, confirm missing.
- Add several IDs, remove middle element, confirm swapped entity still retrieves correctly.
- Add component to large deterministic ID, retrieve it through fallback.
- Remove component from large deterministic ID.
- Mix small and large IDs in the same pool.

Runtime checks:

- Spawn player.
- Spawn monsters.
- Cast skills.
- Fire projectiles.
- Apply buffs.
- Create/destroy village/build tasks if available.

Profiler checks:

- `TryGetComponent` count may remain similar.
- Time per lookup should drop for small EntityIds.
- No new GC allocations in hot loops.

### 4.10 Phase 1 Risks

| Risk | Mitigation |
|------|------------|
| stale dense index after remove | centralize swap update in `Remove` |
| direct sparse array memory grows too much | cap with `MaxDirectEntityId` |
| deterministic ID accidentally creates huge array | route IDs above cap to fallback |
| negative IDs | treat as missing, never index array |
| behavior difference in `Get` default return | preserve current behavior |

## 5. Phase 1.5: Projectile Hot-Loop Optimization

### 5.1 Goal

After Phase 1, component lookup is cheaper, but projectile collision can still dominate the frame because it multiplies lookups by:

```text
projectile count * candidate entity count * component checks per candidate
```

Current shape:

```text
for each ProjectileComponent
  iterate every TransformComponent
    ComponentManager.HasComponent<ProjectileTag>
    ComponentManager.TryGetComponent<FactionComponent>
    ComponentManager.HasComponent<StatComponent>
    ComponentManager.TryGetComponent<JumpComponent>
    ComponentManager.TryGetComponent<ColliderComponent>
```

Target shape:

```text
for each ProjectileComponent
  use cached local pools
  iterate FactionComponent candidates for normal faction-owned projectiles
    target faction is read by GetByIndex
    Transform/Stat/Jump/Collider are read by pool.TryGet/Contains
```

This phase does not introduce a global hot mapping table. It is a local system-level optimization for `System_Projectile`.

### 5.2 Why Before Phase 2

Profiler after Phase 1 showed the remaining cost concentrated around:

- `System_Projectile.OnFixedUpdate`
- `System_Projectile.CheckCollision`
- repeated `ComponentManager.TryGetComponent`
- repeated `ComponentManager.HasComponent`

Phase 2 reduces large deterministic ID costs, but projectile collision mostly spends time scanning target candidates and checking multiple components. Therefore projectile-loop cleanup has better immediate risk/reward.

### 5.3 Planned Changes

At the start of `System_Projectile.OnFixedUpdate`, cache pools once:

```csharp
SparseSet<ProjectileComponent> projectilePool = cm.GetComponentPool<ProjectileComponent>();
SparseSet<TransformComponent> transformPool = cm.GetComponentPool<TransformComponent>();
SparseSet<VelocityComponent> velocityPool = cm.GetComponentPool<VelocityComponent>();
SparseSet<SkillComponent> skillPool = cm.GetComponentPool<SkillComponent>();
SparseSet<FactionComponent> factionPool = cm.GetComponentPool<FactionComponent>();
SparseSet<StatComponent> statPool = cm.GetComponentPool<StatComponent>();
SparseSet<JumpComponent> jumpPool = cm.GetComponentPool<JumpComponent>();
SparseSet<ColliderComponent> colliderPool = cm.GetComponentPool<ColliderComponent>();
SparseSet<ProjectileTag> projectileTagPool = cm.GetComponentPool<ProjectileTag>();
```

Movement lookup changes from manager path:

```csharp
cm.TryGetComponent<TransformComponent>(entityId, out var transform)
cm.TryGetComponent<VelocityComponent>(entityId, out var velocity)
```

to direct pool path:

```csharp
transformPool.TryGet(entityId, out var transform)
velocityPool.TryGet(entityId, out var velocity)
```

`CheckCollision` receives the hot pools as parameters so it does not repeatedly go through `ComponentManager`.

### 5.4 Candidate Loop Strategy

Fast path:

```text
if projectile owner has FactionComponent:
  iterate FactionComponent pool
```

Benefits:

- Skips world items, area effects, map loaders, skill entities, and other transform-only entities without faction.
- Reads target faction with `factionPool.GetByIndex(i)` instead of another lookup.
- Keeps `StatComponent` as an explicit filter for damageable targets.

Fallback path:

```text
if projectile owner has no FactionComponent:
  preserve existing behavior by iterating TransformComponent pool
```

This keeps migration-safe behavior for old or special projectiles.

### 5.5 Detailed Filtering Order

For each candidate:

1. skip projectile entity itself
2. skip projectile owner
3. skip entities with `ProjectileTag`
4. if owner has faction:
   - target must have faction
   - target faction must not be neutral
   - target faction must differ from owner faction
5. target must have `StatComponent`
6. if target has `JumpComponent` and height is invincible, skip
7. read target transform
8. read optional collider
9. run hitbox test
10. apply damage/effects

### 5.6 Expected Effect

Expected improvements:

- fewer total candidate iterations in projectile collision
- fewer `ComponentManager.TryGetComponent` calls
- fewer `ComponentManager.HasComponent` calls
- more direct pool lookups in the hottest projectile path

This will not eliminate all component lookup cost. It should reduce the biggest multiplier before considering a broader hot component mapping table.

### 5.7 Risks

| Risk | Mitigation |
|------|------------|
| target without faction should still be hittable in special cases | use Transform fallback only when owner has no faction |
| behavior changes for neutral/factionless targets | preserve current owner-has-faction filtering exactly |
| too many method parameters to `CheckCollision` | introduce a small private context struct if readability suffers |
| optimization hides future broadphase need | profile again after this phase |

### 5.8 Acceptance Criteria

- Projectile movement still works.
- Projectiles still ignore owner and other projectiles.
- Same-faction and neutral filtering remains unchanged.
- Damageable targets with `StatComponent` can still be hit.
- Jump invincibility still skips hits.
- Piercing projectiles still continue after hits.
- Non-piercing projectiles still destroy on first hit.
- Profiler shows lower `System_Projectile.CheckCollision` time and fewer manager lookup calls.

---

## 6. Phase 1.6: Skill Loop Manager-Bypass Optimization

Status: partially implemented in `Assets/Scripts/Common/System/System_Skill.cs`.

### 6.1 Goal

Keep the projectile bottleneck visible for comparison, while reducing a separate non-projectile source of repeated ECS lookup overhead.

This phase intentionally does not change projectile collision behavior. It targets the primary `System_Skill.OnFixedUpdate` loop only.

### 6.2 Implemented Scope

At the start of `System_Skill.OnFixedUpdate`, cache the component manager and the pools used by the per-skill loop:

```csharp
ComponentManager cm = AR.s.Component;
SparseSet<SkillComponent> skillPool = cm.GetComponentPool<SkillComponent>();
SparseSet<SkillStateComponent> skillStatePool = cm.GetComponentPool<SkillStateComponent>();
SparseSet<SkillTimingComponent> skillTimingPool = cm.GetComponentPool<SkillTimingComponent>();
SparseSet<SkillCommandComponent> skillCommandPool = cm.GetComponentPool<SkillCommandComponent>();
SparseSet<StateComponent> statePool = cm.GetComponentPool<StateComponent>();
```

Then replace repeated manager-path reads in the main loop:

```text
ComponentManager.TryGetComponent<SkillStateComponent>
ComponentManager.TryGetComponent<SkillTimingComponent>
ComponentManager.TryGetComponent<SkillCommandComponent>
ComponentManager.TryGetComponent<StateComponent> inside ShouldCancelSkill
```

with direct pool reads:

```text
skillStatePool.TryGet
skillTimingPool.TryGet
skillCommandPool.TryGet
statePool.TryGet
```

Writes still go through `ComponentManager.SetComponent`/`RemoveComponent` so tracking behavior stays unchanged.

### 6.3 Deferred Scope

Do not yet rewrite:

- `ProcessSkillCommands`
- state enter/exit handlers
- skill hit processing
- target search/range checks

Those paths are more behavior-heavy and should be profiled separately.

### 6.4 Acceptance Criteria

- Skill cooldown ticking still works.
- Running skills still update timing/state.
- Stun cancellation still works.
- Skill commands still start the correct skill.
- Profiler shows fewer `ComponentManager.TryGetComponent` calls from `System_Skill.OnFixedUpdate`.

---

## 7. Phase 1.7: Render Loop Manager-Bypass Optimization

Status: implemented in `Assets/Scripts/Common/System/System_Render.cs`.

### 7.1 Goal

Reduce non-projectile ECS lookup overhead in `System_Render.OnUpdate` while preserving the existing render/update flow.

This phase intentionally keeps projectile collision unchanged so projectile remains available as a separate profiling stress case.

### 7.2 Implemented Scope

At the start of `System_Render.OnUpdate`, cache the hot pools once:

```csharp
SparseSet<TransformComponent> transformPool = _componentManager.GetComponentPool<TransformComponent>();
SparseSet<VelocityComponent> velocityPool = _componentManager.GetComponentPool<VelocityComponent>();
SparseSet<ColliderComponent> colliderPool = _componentManager.GetComponentPool<ColliderComponent>();
SparseSet<JumpComponent> jumpPool = _componentManager.GetComponentPool<JumpComponent>();
```

Then replace manager-path reads in the per-transform loop:

```text
ComponentManager.TryGetComponent<VelocityComponent>
ComponentManager.TryGetComponent<ColliderComponent>
ComponentManager.TryGetComponent<JumpComponent>
```

with direct pool reads:

```text
velocityPool.TryGet
colliderPool.TryGet
jumpPool.TryGet
```

When render-side movement changes a `TransformComponent`, store it back through the already-iterated pool:

```csharp
transformPool.SetByIndex(i, transformComponent);
```

This avoids another manager lookup and matches the intent of updating the same dense transform element currently being processed.

### 7.3 Deferred Scope

Do not yet change:

- entity-to-GameObject Dictionary mapping
- Unity `Transform` property writes
- jump/shadow visual behavior
- movement ownership between `System_Render` and fixed update systems

Those are separate architecture questions.

### 7.4 Acceptance Criteria

- Entities with velocity still move visually.
- Collider sliding still applies when `ColliderComponent` exists.
- Jump sprite height and shadow scale still update.
- Non-jumping sprites still reset local Y to zero.
- Profiler shows fewer `ComponentManager.TryGetComponent` calls from `System_Render.OnUpdate`.

---

## 8. Phase 1.8: AI Behavior Loop Manager-Bypass Optimization

Status: implemented in `Assets/Scripts/Common/System/System_AI_Behavior.cs`.

### 8.1 Goal

Reduce repeated manager-path reads in the main AI behavior dispatch loop without changing AI state handler behavior.

This keeps the work narrowly scoped: only the system loop that chooses which handler to call is changed.

### 8.2 Implemented Scope

Cache the state pool once:

```csharp
SparseSet<AIBehaviorTypeComponent> behaviorPool = componentManager.GetComponentPool<AIBehaviorTypeComponent>();
SparseSet<AIStateComponent> statePool = componentManager.GetComponentPool<AIStateComponent>();
```

Then replace:

```csharp
componentManager.TryGetComponent<AIStateComponent>(entityId, out var stateComponent)
```

with:

```csharp
statePool.TryGet(entityId, out var stateComponent)
```

`AIBehaviorTypeComponent` was already read from the dense behavior pool with `GetByIndex`.

### 8.3 Deferred Scope

Do not yet optimize inside individual AI state handlers.

Handlers such as `ChaseStateHandler`, `MeleeAttackStateHandler`, `RangedAttackStateHandler`, `PatrolStateHandler`, and `BuildStateHandler` still use normal `ComponentManager` access. That is intentional because those handlers contain more gameplay-specific branching and should be profiled separately.

### 8.4 Acceptance Criteria

- AI entities still dispatch to the same state handlers.
- Entities without `AIStateComponent` are still skipped.
- No AI state transition behavior changes.
- Profiler shows fewer `ComponentManager.TryGetComponent<AIStateComponent>` calls from `System_AI_Behavior.OnFixedUpdate`.

---

## 9. Phase 2: Small EntityId for Skill/Buff/Relationship

### 9.1 Goal

Stop encoding deterministic meaning into EntityId numbers.

Current:

```text
deterministic meaning = EntityId formula
```

Target:

```text
EntityId = small recycled runtime ID
deterministic meaning = key -> EntityId mapping
```

Important intent:

The key map is not meant to move the same hot-path Dictionary lookup from `SparseSet<T>` to `EntityIdHelper`.

It separates two different kinds of lookup.

```text
Meaning lookup, rare:
  (ownerEntityId, slotIndex) -> skillEntityId
  (targetEntityId, buffTableId) -> buffEntityId
  (fromEntityId, toEntityId) -> relationshipEntityId

Component lookup, frequent:
  skillEntityId -> SkillComponent
  skillEntityId -> SkillStateComponent
  skillEntityId -> SkillTimingComponent
  targetEntityId -> StatComponent
```

With deterministic large IDs, meaning lookup is cheap because the ID is computed, but all component lookups for that large ID go through the `SparseSet<T>` fallback Dictionary.

With key maps, the key Dictionary should be used only when code needs to resolve a semantic key into an entity. Once a small `entityId` is known, normal ECS component access should use the direct sparse array path.

Expected usage:

```text
Creation/deletion/equipment change/UI/command construction:
  use key map if needed

Per-frame combat/system loops:
  use already stored small entityId
  or iterate the component pool densely
```

Bad usage:

```text
Every frame, for every character slot:
  Dictionary<SkillKey, int> lookup
  then component lookup
```

That would reintroduce hash cost into the hot path.

### 9.2 New Key Maps

Skill:

```csharp
private readonly struct SkillKey
{
    public readonly int OwnerEntityId;
    public readonly int SlotIndex;
}

private static readonly Dictionary<SkillKey, int> _skillEntityByOwnerSlot;
private static readonly Dictionary<int, SkillKey> _skillKeyByEntityId;
```

Buff:

```csharp
private readonly struct BuffKey
{
    public readonly int TargetEntityId;
    public readonly int BuffTableId;
}

private static readonly Dictionary<BuffKey, int> _buffEntityByTargetAndType;
private static readonly Dictionary<int, BuffKey> _buffKeyByEntityId;
```

Relationship:

```csharp
private readonly struct RelationshipKey
{
    public readonly int FromEntityId;
    public readonly int ToEntityId;
}

private static readonly Dictionary<RelationshipKey, int> _relationshipEntityByPair;
private static readonly Dictionary<int, RelationshipKey> _relationshipKeyByEntityId;
```

### 9.3 New Behavior

```text
CreateSkillEntity(owner, slot)
  if key exists: return existing ID
  id = CreateEntity()
  map key -> id
  map id -> key
  return id
```

Same idea for buff and relationship.

### 9.4 Replace Formula-Based APIs

Current API patterns to phase out:

- `GetDeterministicId(ownerEntityId, EntityIdCategory.Skill, slotIndex)`
- `GetOwnerEntityId(skillEntityId, EntityIdCategory.Skill)`
- `GetIndex(skillEntityId, EntityIdCategory.Skill)`
- `IsValidDeterministicId(...)`

Target API patterns:

- `TryGetSkillEntityId(ownerEntityId, slotIndex, out int skillEntityId)`
- `TryGetSkillOwnerAndSlot(skillEntityId, out int ownerEntityId, out int slotIndex)`
- `TryGetBuffEntityId(targetEntityId, buffTableId, out int buffEntityId)`
- `TryGetBuffTargetAndType(buffEntityId, out int targetEntityId, out int buffTableId)`
- `TryGetRelationshipEntityId(fromEntityId, toEntityId, out int relationshipEntityId)`
- `TryGetRelationshipPair(relationshipEntityId, out int fromEntityId, out int toEntityId)`

### 9.5 Migration Plan

Do this in slices.

1. Add new key maps while keeping old deterministic APIs.
2. Change `CreateSkillEntity`, `CreateBuffEntity`, `CreateRelationshipEntity` to optionally use small IDs behind a feature flag.
3. Replace call sites that compute IDs with lookup methods.
4. Remove formula dependency after all call sites are migrated.
5. Delete fallback compatibility only after save/load is confirmed.

### 9.6 Save/Load Considerations

Saved data may currently store deterministic IDs.

Need decisions:

- Are saved skill/buff/relationship entity IDs persisted?
- Can old saves be migrated?
- Should derived skill/buff/relationship entities be rebuilt on load instead of preserving exact IDs?

Recommended:

- Persistent world objects can keep saved EntityId.
- Runtime-derived entities like skill/buff/relationship should prefer rebuilding from owner/slot or target/type keys.

### 9.7 Phase 2 Risks

| Risk | Mitigation |
|------|------------|
| old code still computes formula IDs | keep compatibility wrappers temporarily |
| save files reference old IDs | add migration/rebuild path |
| duplicate skill/buff entities | key map owns uniqueness |
| destroying owner leaves derived maps dirty | centralize cleanup in `DestroyEntity` |

---

## 10. Phase 3: Hot Component Mapping Table

### 10.1 Goal

If Phase 1 and Phase 2 are not enough, add a per-entity hot index mapping.

```text
entityId -> HotComponentMap
```

Example:

```csharp
public struct HotComponentMap
{
    public int TransformIndex;
    public int VelocityIndex;
    public int StatIndex;
    public int FactionIndex;
    public int ColliderIndex;
    public int JumpIndex;
}
```

Use `-1` for missing component.

### 10.2 Scope

Start only with hot components:

- `TransformComponent`
- `VelocityComponent`
- `StatComponent`
- `FactionComponent`
- `ColliderComponent`
- `JumpComponent`
- maybe `StateComponent`

Do not add every component.

### 10.3 Integration Points

When adding a hot component:

```text
SparseSet<T>.Add/Set
  dense index assigned
  ComponentManager updates HotComponentMap for entity
```

When removing:

```text
SparseSet<T>.Remove
  swap-and-pop may move another entity
  ComponentManager must update both removed entity and swapped entity mapping
```

This phase may require `SparseSet<T>` to report swap details to `ComponentManager`.

Possible design:

```csharp
public readonly struct SparseRemoveResult
{
    public bool Removed;
    public int RemovedEntityId;
    public int MovedEntityId;
    public int MovedNewDenseIndex;
}
```

### 10.4 Usage Pattern

Hot systems can avoid repeated sparse lookup:

```text
map = hotMaps[entityId]
if map.TransformIndex >= 0
  transform = transformPool.GetByIndex(map.TransformIndex)
```

Only use this in the hottest systems after profiling.

Candidate systems:

- `System_Projectile`
- `System_Render`
- `System_Skill`
- `FactionHelper`
- `System_AI_Perception`

### 10.5 Risks

| Risk | Mitigation |
|------|------------|
| map desync after component remove | add invariant checks in development builds |
| too much ComponentManager coupling | only support selected hot components |
| harder debugging | keep normal `TryGetComponent` path as fallback |

---

## 11. Phase 4: Entity Generation Safety

### 11.1 Goal

Prevent stale entity references from pointing to a new entity after ID reuse.

Current risk:

```text
missile.TargetEntityId = 42
entity 42 dies
ID 42 reused by unrelated NPC
missile now targets unrelated NPC
```

### 11.2 Target Representation

Option A: packed 64-bit entity reference.

```csharp
public readonly struct EntityRef
{
    public readonly int Index;
    public readonly int Generation;
}
```

Option B: keep `int EntityId`, add validation table in `EntityIdHelper`.

Option A is cleaner but requires changing many component fields.

### 11.3 Current Fields Affected

Examples:

- `AIComponent.TargetEntityId`
- `ProjectileComponent.OwnerEntityId`
- `ProjectileComponent.SkillEntityId`
- `AreaEffectComponent.OwnerEntityId`
- `BuffInstance.TargetEntityId`
- `SkillComponent.OwnerEntityId`
- `SkillTargetComponent.TargetId`
- `NpcAssignmentComponent.AssignedObjectEntityId`
- `NpcBuildAssignmentComponent.TaskEntityId`
- `ObjectPlacementTaskComponent.AssignedNpcEntityId`
- `ObjectPlacementTaskComponent.BuildingEntityId`
- `PlayerNearbyServicesComponent.Nearest*EntityId`
- `RelationshipComponent.FromEntityId`
- `RelationshipComponent.ToEntityId`

### 11.4 Recommended Timing

Do not combine generation with Phase 1.

Generation is primarily correctness work. It touches component data, save/load, factories, systems, and helper APIs.

---

## 12. Implementation Order

Recommended order:

1. Profile current lookup cost.
2. Implement `SparseSet<T>` array + fallback Dictionary.
3. Validate gameplay and no GC allocations.
4. Profile again.
5. If large deterministic IDs still matter, migrate Skill/Buff/Relationship to small IDs with key maps.
6. Profile again.
7. If still necessary, add hot component mapping table to projectile/skill/render paths.
8. Schedule generation as a separate safety refactor.

---

## 13. Acceptance Criteria

Phase 1 is complete when:

- `SparseSet<T>` no longer uses Dictionary for normal small EntityIds.
- Large deterministic IDs still work through fallback.
- Existing systems compile without call site changes.
- Add/Set/TryGet/Contains/Remove behavior is unchanged.
- Removing from the middle of a pool keeps swapped entity lookup correct.
- Projectile combat, skill casting, buff application, entity destroy, and render sync still work.
- Profiler shows lower component lookup time in hot scenarios.

Phase 2 is complete when:

- Skill/Buff/Relationship entities can be created with small recycled IDs.
- Deterministic uniqueness is provided by key maps.
- Formula-based call sites are removed or fully wrapped.
- Save/load behavior is documented and tested.

Phase 3 is complete when:

- Only measured hot systems use hot maps.
- Hot maps remain consistent after add/remove/swap.
- Normal `TryGetComponent` remains available as a safe fallback.

---

## 14. Initial Recommendation

Start with Phase 1 only.

It has the best risk/reward ratio because it changes the internal sparse lookup mechanism while preserving the rest of the ECS surface.

Do not start by rewriting `EntityIdHelper`. That work is valuable, but it is larger and should happen after the first profiling-confirmed improvement.
