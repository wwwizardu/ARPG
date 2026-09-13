---
name: manage-monster-tables
description: Create, update, balance, and verify ARPG monster records in the connected ARPG Tables Google Sheet across the Monster and Stat tabs. Use for requests about "몬스터 테이블", Monster/Stat values, new monster rows, monster AI, collision radii, archetype, or biome assignments. Do not use for sprite, SpriteLibrary, prefab, or unrelated table work.
---

# Manage Monster Tables

Maintain one coherent monster definition across the `Monster` and `Stat` tabs without duplicating rows or silently changing adjacent game data.

## Required context

- For any live Google Sheet read or write, first load and follow the available `google-drive:google-sheets` skill.
- Before interpreting or writing fields, read [references/table-contract.md](references/table-contract.md).
- If the user asks Codex to choose balance values, read `Wiki/current_status.md` and the relevant sections of `Docs/combat/BALANCE_DESIGN.md`, then compare against live rows for similar monsters. Explicit user constraints are authoritative.

## Workflow

1. Identify the monster by exact `Name` or `Id` and determine whether the request is inspect-only, edit-existing, or add-new.
   - If the user says the entries already exist, search both tabs and edit those exact rows. Never append replacements.
   - For a new monster, check both `Id` and `Name` for duplicates before choosing an unused ID.
2. Resolve the target spreadsheet from the project default in the contract and verify its title and tab metadata before reading cells.
3. Read the live header and the smallest relevant ranges with cell metadata. Preserve formulas, validation, notes, formatting, and fields outside the request.
4. Build the `Monster` and `Stat` changes together.
   - Keep `Monster.StatId` equal to an existing `Stat.Id` for that monster.
   - Validate `AnimationId`, `Ai`, `WeaponId`, and `DropId` against their live referenced tables before inventing or changing them.
   - Derive AI behavior from the live `AI` table. Do not assume an ID always means melee or ranged.
   - Treat `Any` as all-biome test/common spawning; use a specific biome only when the user wants an exclusive habitat.
   - Use relative balance constraints literally, such as "HP below Wolf and Bear". Read those comparison rows live rather than relying on remembered numbers.
5. Before writing, summarize any inferred gameplay choices that materially affect behavior. Exact user-provided values need no extra confirmation.
6. Apply related cell edits in one coherent batch with exact ranges and value-only field masks. Do not sort, resize, restyle, or rewrite entire rows unless required.
7. Re-read every changed cell and verify:
   - the target `Name`/`Id` still identifies the intended monster;
   - `Monster.StatId == Stat.Id`;
   - required numeric Stat cells are populated and parseable;
   - referenced IDs exist;
   - AI, biome, and user-stated relative balance constraints hold;
   - neighboring values and formatting are unchanged.
8. Report the spreadsheet link, tab/ranges changed, key gameplay effects, and verification result.

## Optional Unity synchronization

Editing Google Sheets does not update the project's downloaded binary tables.

- Download or refresh Unity table assets only when the user explicitly asks to sync, import, download, or test the sheet changes in Unity.
- With a connected Unity Editor, first inspect the exact menu items, then run only `ARPG/Download Table/MonsterTable` and `ARPG/Download Table/StatTable` as needed.
- After download, inspect the Unity Console for parser/reference errors. Do not modify sprites, prefabs, animations, or code as part of this skill.

## Boundaries

- Do not create missing sprites, SpriteLibraryAssets, animation rows, AI rows, skills, projectiles, drops, or prefabs unless the user separately requests that work.
- Do not migrate the Stat schema or insert Max Resistance columns during ordinary monster edits.
- Do not convert a balance preference into a permanent project rule; keep decisions scoped to the requested monster.
