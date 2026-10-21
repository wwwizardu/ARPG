# Agent Instructions

Shared instructions for every coding agent in this repository. Codex reads this file directly; Claude Code loads it through `CLAUDE.md`. Edit rules here only.

## Project Overview

This is a Unity 6000.4.2f1 2D URP ARPG using a custom ECS architecture, not Unity DOTS.

- Unity Input System 1.19.0 (`InputSystem_Actions.inputactions`)
- Universal Render Pipeline 17.4.0
- Custom ECS: an entity is an integer ID, a component is a data-only struct, a system is a class holding the logic, and `SparseSet` provides O(1) component storage

## Project Structure

`Assets/Scripts/` is organized as follows:

- `Manager/`: core managers such as `SystemManager`, `ComponentManager`, and `DataManager`
- `Common/Component/`: pure ECS component data structs
- `Common/System/`: ECS systems and game logic
- `Common/Interface/`: interfaces including `ISystem`
- `Common/SparseSet.cs`: ECS component storage
- `AI/`, `Buff/`, `Skill/`, `Creature/`, `UI/`: feature-specific implementations

## C# Conventions

- Prefix member variables with `_`; order methods `public` → `protected` → `private`.
- `Initialize()` is for one-time setup; `Reset()` must clean up and restore the initial state.
- Use `for` for performance-sensitive runtime iteration, such as per-frame/tick hot loops or large collections. Otherwise, choose `for` or `foreach` based on clarity. Use `SqrMagnitude` for distance comparisons.
- Do not create delegates on hot paths (per-frame/tick work, per-entity loops, frequently called APIs). Passing an instance method group or a capturing lambda to a `Func`/`Action` parameter allocates a new delegate (and closure) on every call. Create the delegate once at the owner's real initialization entry point, store it in a field, and reuse it. If the lambda must capture a variable, build it in a separate method so the cached lookup path does not allocate the closure.
- Use explicit boolean/null comparisons (for example, `== false` and `== null`) instead of `!`.

## ECS Rules

- Components contain data only; game logic belongs in systems. Tags are empty structs.
- Objects that have a world position and that the player interacts with or that need world-space presentation (for example dungeon chests and entrances) are entities: an `EntityBase` prefab plus components, decisions in systems, and visuals as `IEntityMessageHandler` children (see `HpBarView`). Keep the saved source of truth in domain data and treat the entity as its runtime representation. Do not add manager-owned marker GameObjects with their own distance checks or child lookups. Exception: map tile structures whose source of truth is tile data, such as village walls and gates in `VillageData.WallSegments`, stay tiles.
- Access components through `AR.s.Component` and cache it in a local at the top of the method (`ComponentManager cm = AR.s.Component;`).
- Use `TryGetComponent` for optional components. Iterate with `GetComponentPool<T>()` and `pool.Count` / `pool.GetEntityId(i)` / `pool.GetByIndex(i)`; for multi-component queries, iterate one pool and `TryGetComponent` the others. Size pools appropriately.
- Systems have one responsibility, remain stateless (all state lives in components or managers), declare an explicit priority, implement the matching phase interface (`IUpdateSystem`, `IFixedUpdateSystem`, or `ILateUpdateSystem`), and set `UpdateInterval` for work that need not run every frame.
- `ISystem` priorities run in ascending order. Use these ranges:

  | Priority | Use | Phase |
  | --- | --- | --- |
  | 0–50 | Input and AI perception | Update / FixedUpdate |
  | 50–69 | Village domain | FixedUpdate |
  | 100–500 | Gameplay: movement, skills, buffs | FixedUpdate / Update |
  | 500–1000 | Animation and rendering | Update |

  Village sub-ranges (see [VILLAGE_RUNTIME_DESIGN.md](Docs/village/VILLAGE_RUNTIME_DESIGN.md)): 50–54 Resource (passive production), 55–59 Population (respawn, immigration, birth, household split), 60–64 Lifecycle (tier promotion, reputation, threat level), and 65–69 Construction (build queue, walls).
- New systems implement an interface from `Common/Interface/ISystem.cs`, are registered in `SystemManager.Initialize()` with a priority comment, and add pool sizes for new components to `ComponentManager._poolSizes`. Use existing systems in `Common/System/` as templates.
- Name components `[Feature]Component`, systems `System_[Feature]`, and tags `[Feature]Tag`.

## Logic Placement

Before creating a new `static` class, ask in order:

1. **Does it hold mutable state or have a lifecycle** (reset, per-owner cleanup, save/load)? → Not static. Put the state in a component, manager instance, or save data. A static class may still own the pure logic and receive that state as a parameter.
2. **Is it gameplay logic that runs every tick?** → A `System_*` registered in `SystemManager`.
3. **Is it behavior tightly bound to one data type?** → A method or extension on that type.
4. **Is it stateless pure logic used in more than one place?** → A static class is fine; keep it cohesive with a single responsibility.

Guardrails:

- Search for an existing home before adding a class to avoid duplicate or fragmented helpers.
- Read-only static configuration is allowed. Persistent mutable static state is not: per-entity/per-village state, caches, and shuffle bags need an owner with a lifecycle. Anti-pattern: a static selector holding `private static readonly Dictionary<int, HashSet<DialogueTopicType>> _recent` — process-global, ownerless, and impossible to reset, clean up, or save.
- Exception: a scratch buffer cleared before every use keeps no state between operations (for example, the `Pathfinder` A* sets and `System_StatCalculation._increasedByStatScratch`).
- Do not create grab-bag helpers such as `*Util`, `*Helper`, `*Common`, or generic `*Manager`; class names must describe a single responsibility.

## Tool Execution Safety

Call independent tools in parallel by batching them into a single message. Run calls sequentially only when one genuinely depends on an earlier result or when two calls could conflict on the same target.

Must be sequential:

- A call whose arguments come from a previous call's output (search for a file, *then* read it)
- Two writes to the same file, or a write followed by a read of that file
- Google Sheets mutations on the same spreadsheet (`sheets_write`, `sheets_format`, `sheets_add_sheet`), because the API does not order concurrent edits
- Anything mutating shared state: `git` commit/checkout, table re-download, asset generation

Safe in parallel: reads of different files, independent searches, and read-only inspection commands (`git status`, `git diff`, `ls`). When it is unclear whether two calls can conflict, run them sequentially.

## Unity Projects and Temporary Files

Do not create, copy, or junction-link a secondary Unity project for testing. A separate project forces Unity to build a fresh `Library` and reimport project assets, so its time and disk cost grows with the resource set. Use the currently open project/editor, existing project-local test or diagnostic paths, or non-Unity static validation instead. If a required validation cannot run through those paths, stop and report the limitation rather than creating another Unity project.

Delete temporary files and directories created for validation or testing before finishing the task, after the processes using them have stopped. This includes temporary test saves, logs, scratch reports, backups, and generated build configuration. Summarize the necessary verification results in the final response or a requested deliverable before deleting the temporary evidence.

Limit cleanup to temporary artifacts created by the current task. Preserve pre-existing files, user changes, established diagnostic tools, and requested final deliverables. Do not delete shared Unity caches or an entire `Temp` directory to clean up task-specific files.

## Validation and Diagnostic Placement

- If a task will need diagnostic runs (Editor diagnostic menus, NPC/village simulation runs, play-mode verification), ask the user which diagnostics to run **before starting the implementation**, and proceed only after the user answers. List each candidate with what it checks and its expected duration, and recommend the smallest set that covers the change. Ask even when the task prompt already lists diagnostics. Long runs freeze the Unity Editor: the NPC ambition simulation takes about 10 minutes for 30 game days and about 21 minutes for 60.
- Distinguish checks needed only to validate the current change from invariants that need ongoing verification. Do not keep task-specific validation files merely because they could be reused later.
- Compilation is a check of the current change: run the appropriate existing Runtime/Editor build commands. Do not add a permanent verification script or test project just to wrap those commands.
- Put expensive, repeatable diagnostics in Unity Editor menu commands so they can be run explicitly when needed. Extend the relevant existing diagnostic menu and code before adding a new entry. File I/O, large data scans, stress tests, fault injection, and save/load round trips belong here; use isolated temporary data and clean it up after execution.
- Put lightweight ongoing checks at the relevant runtime state transitions or lifecycle boundaries, enabled only in the Editor with `#if UNITY_EDITOR` or an equivalent compile-time guard. Keep these checks cheap, avoid unnecessary per-frame checks and log spam, and exclude them from player builds.
- Reuse the feature's existing diagnostic code. Do not accumulate a new `verify.*` script, standalone host project, test directory, or scratch report for each task. Keep one-off validation scaffolding temporary and remove it when the task is finished.
- When changing a contract covered by a maintained Editor diagnostic or runtime check, update that check in the same change so it continues to verify the intended behavior.

## Wiki Reference Rules

`Wiki/` is a knowledge layer that synthesizes the original documentation and code. Follow the operating rules in `Wiki/_SCHEMA.md`.

- Before work that requires design, synthesis, or understanding *why* (architecture decisions, changes spanning multiple systems, or confirming design intent), read the relevant `Wiki/<topic>.md` first.
- Use this entry path, going deeper only when needed:
  1. If the affected topics are unclear, read `Wiki/README.md` (the map) first.
  2. Read every relevant `Wiki/<topic>.md` page, including related topics affected by the work, not just the primary one.
  3. Read only the original `Docs/` sections cited by those pages when canonical detail is needed; do not read whole doc folders.
- If no topic page exists, use `Wiki/README.md` and the original documents it lists.
- Straightforward single-file implementation or bug-fix work does not require consulting the wiki.
- `Docs/` and the codebase are the source of truth. If the wiki conflicts with either, stop and report the discrepancy; do not independently change the wiki.
- When an important design decision made during work is missing from the wiki, propose an update to the relevant page and apply it only with user confirmation.
- Keep wiki pages concise syntheses rather than copies of source documents. Every factual claim must include a source reference in the form `[src: path/to/file]`; mark unverified inferences as `[inference]`.
