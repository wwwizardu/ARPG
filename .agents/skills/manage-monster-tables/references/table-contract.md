# ARPG Monster table contract

Read this before inspecting, adding, or editing monster rows.

## Default spreadsheet

- Title: `ARPG Tables`
- Spreadsheet ID: `13j0_AI_6nSHHEkAHK2w9oRd-98xYYiUoP5spAv0U4TA`
- Required tabs: `Monster`, `Stat`

Verify the live title, tabs, sheet IDs, headers, and target rows before every write. If the default ID is inaccessible or no longer matches the title, search Drive for the exact title and stop if more than one credible candidate remains.

## Source-of-truth code

- `Assets/Scripts/Common/Tables.cs`: runtime table fields and late references.
- `Assets/Scripts/Editor/DownloadTables.cs`: live sheet column parsing contract.
- `Assets/Scripts/Common/GlobalEnum.cs`: enum names accepted by the parser.
- `Docs/combat/BALANCE_DESIGN.md`: level curves and balance intent, not per-monster final numbers.

If the live sheet conflicts with parser code, do not guess or migrate it during a monster edit. Report the discrepancy.

## Monster tab: A:Q

| Column | Header | Contract |
| --- | --- | --- |
| A | Id | Unique integer monster ID. |
| B | Name | Runtime monster name/key. |
| C | Description | Sheet-facing description; parser does not load it. |
| D | StatId | Integer foreign key to `Stat.Id`. |
| E | MoveRadius | Float navigation/overlap footprint. |
| F | HitRadius | Float combat hit circle. |
| G | HitOffset | Float vertical hit-center offset; parsed as `HitOffsetY`. |
| H | PrefabName | Runtime prefab key. Preserve the established value unless prefab work is requested. |
| I | AnimationId | Integer reference to `Animation.Id`. |
| J | WeaponId | Integer weapon reference used by runtime creation. |
| K | Ai | Integer reference to `AI.Id`; behavior must be read from the live AI row. |
| L | DropId | Integer drop reference. |
| M | DropRateBonus | Integer bonus. |
| N | DropRarityBonus | Integer bonus. |
| O | Level | Integer base/override level. |
| P | Archetype | `GlobalEnum.MonsterArchetype` name or a parser-supported value. Validate against code/live data. |
| Q | Biome | `GlobalEnum.BiomeType` name. Empty falls back to `Any`. |

Current biome names in code are `Plains`, `Forest`, `Desert`, `Swamp`, `Steppe`, `Tundra`, `Snow`, `Mountain`, `Wasteland`, and `Any`. `Any` places the monster in every biome pool; a specific value makes it biome-exclusive under the current mapping.

### Collision interpretation

- `MoveRadius` is the entity's ground footprint used for pathing and separation.
- `HitRadius` and `HitOffset` define the target body used by melee, projectiles, and skill range calculations.
- When choosing new collision values, compare visually similar live monsters and, when available, the imported sprite bounds. A mismatched pivot or pixels-per-unit is an import problem and should be reported rather than hidden with extreme table values.

## Stat tab: current legacy A:AF layout

The current sheet uses the accepted 32-column legacy format:

`Id, Type, Class, Str, Dex, Int, MaxHp, MaxMp, HpGeneration, MpGeneration, AttackMin, AttackMax, CriRate, CriDamage, MoveSpeed, AttackSpeed, CastSpeed, Defense, FireResist, IceResist, LightningResist, PoisonResist, Luck, BloodingRate, IgniteRate, Evasion, BlockChance, BlockReduction, SkillDamage, CooldownReduction, LifeSteal, Thorns`

Rules:

- `Id` is the key referenced by `Monster.StatId`.
- `Type` and `Class` are sheet-facing classification fields; preserve their established spelling.
- Every field from `Str` through `Thorns` is parsed as an integer and must be populated. Blank cells cause download errors.
- The parser accepts exactly 32 legacy columns or at least 36 columns with four Max Resistance fields inserted after `PoisonResist`.
- Do not add those four columns during routine monster work. In the 32-column layout, runtime assigns the base maximum resistance defaults automatically.

## Balance decisions

- Compare against live monsters occupying the same role, size, and threat tier.
- Maintain `AttackMin <= AttackMax` and nonnegative HP, MP, speeds, defenses, rates, and resistances unless code explicitly supports another range.
- Ranged/melee identity is primarily an AI/skill behavior decision; HP and movement should then express the intended tradeoff.
- Specialized resistance or status identity should be deliberate and modest unless the user requests an elite/boss profile.
- Level scaling and archetype multipliers are handled elsewhere; do not bake high-level scaling into a level-1 base row.

## Write and validation checklist

1. Exact spreadsheet and tab IDs resolved from metadata.
2. Target row found by both name and ID.
3. No duplicate Monster or Stat keys.
4. Target cells read with values, formatting, formulas, and validation before writing.
5. Related changes batched with precise field masks.
6. Changed cells re-read after writing.
7. Foreign keys and user-stated behavior/balance constraints verified.
8. Unity download left untouched unless explicitly requested.
