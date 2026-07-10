# Agent Instructions

## Project Overview

This is a Unity 6000.0.42f1 LTS 2D URP ARPG using a custom ECS architecture, not Unity DOTS.

- Unity Input System 1.13.1 (`InputSystem_Actions.inputactions`)
- Universal Render Pipeline 17.0.4
- Custom ECS with `SparseSet` component storage

## Project Structure

`Assets/Scripts/` is organized as follows:

- `Manager/`: core managers such as `SystemManager`, `ComponentManager`, and `DataManager`
- `Common/Component/`: pure ECS component data structs
- `Common/System/`: ECS systems and game logic
- `Common/Interface/`: interfaces including `ISystem`
- `Common/SparseSet.cs`: ECS component storage
- `AI/`, `Buff/`, `Skill/`, `Creature/`, `UI/`: feature-specific implementations

## C# and ECS Rules

- Prefix member variables with `_`; order methods `public` → `protected` → `private`.
- `Initialize()` is for one-time setup; `Reset()` must clean up and restore the initial state.
- Use `for`, never `foreach`, for runtime iteration. Use `SqrMagnitude` for distance comparisons.
- Use explicit boolean/null comparisons (for example, `== false` and `== null`) instead of `!`.
- Components contain data only; game logic belongs in systems.
- Use `ComponentManager.TryGetComponent` for optional components and `GetComponentPool<T>()` for iteration. Cache `ComponentManager` references and size pools appropriately.
- Systems have one responsibility, remain stateless, declare an explicit priority, use the appropriate update phase, and set `UpdateInterval` for work that need not run every frame.
- `ISystem` priorities run in ascending order. Use these ranges:

  | Priority | Use | Phase |
  | --- | --- | --- |
  | 0–50 | Input and AI perception | Update / FixedUpdate |
  | 50–69 | Village domain | FixedUpdate |
  | 100–500 | Gameplay: movement, skills, buffs | FixedUpdate / Update |
  | 500–1000 | Animation and rendering | Update |

  Village sub-ranges are 50–54 Resource, 55–59 Population, 60–64 Lifecycle, and 65–69 Construction.
- New systems must implement `ISystem`, be registered in `SystemManager.Initialize()` with a priority comment, and have relevant `ComponentManager` pool sizes updated.
- Name components `[Feature]Component`, systems `System_[Feature]`, and tags `[Feature]Tag`.

## Logic Placement

- Search for an existing home before adding a class to avoid duplicate or fragmented helpers.
- Mutable state or anything with a lifecycle, cleanup, save/load, or per-owner ownership belongs in a component, manager instance, or save data—not a static class.
- Per-tick gameplay logic belongs in `System_*`; behavior tightly bound to one type belongs on that type or an extension; reusable, stateless pure logic may use a cohesive static class.
- Read-only static configuration is allowed. Persistent mutable static state is not. A scratch buffer that is cleared before every use is an exception.
- Do not create grab-bag helpers such as `*Util`, `*Helper`, `*Common`, or generic `*Manager`; class names must describe a single responsibility.

## Tool Execution Safety

Run tools sequentially: issue one tool call, wait for its result, then continue. Do not run tool calls in parallel.

## Wiki Reference Rules

`Wiki/` is a knowledge layer that synthesizes the original documentation and code. Follow the operating rules in `Wiki/_SCHEMA.md`.

- Before work that requires design, synthesis, or understanding *why* (architecture decisions, changes spanning multiple systems, or confirming design intent), read the relevant `Wiki/<topic>.md` first.
- Use this entry path, going deeper only when needed:
  1. If the affected topics are unclear, read `Wiki/README.md` first.
  2. Read every relevant `Wiki/<topic>.md` page, including related topics affected by the work.
  3. Read only the original `Docs/` sections cited by those pages when canonical detail is needed.
- If no topic page exists, use `Wiki/README.md` and the original documents it lists.
- Straightforward single-file implementation or bug-fix work does not require consulting the wiki.
- `Docs/` and the codebase are the source of truth. If the wiki conflicts with either, stop and report the discrepancy; do not independently change the wiki.
- When an important design decision made during work is missing from the wiki, propose an update to the relevant page and apply it only with user confirmation.
- Keep wiki pages concise syntheses rather than copies of source documents. Every factual claim must include a source reference in the form `[src: path/to/file]`; mark unverified inferences as `[inference]`.
