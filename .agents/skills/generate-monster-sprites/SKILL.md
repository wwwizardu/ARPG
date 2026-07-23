---
name: generate-monster-sprites
description: 'Generate a complete Unity monster animation sprite set for this ARPG while matching the existing Bear and Wolf art style. Use when the user asks for a new monster sprite, a monster animation set, "몬스터 스프라이트", "버섯 몬스터 만들어줘", or explicitly invokes $generate-monster-sprites. By default create 13 separate transparent 128x128 PNG files: Idle 2, Move 4, Attack 4, and Die 3, saved under Assets/Art/Sprites/Monster/{MonsterName}/.'
---

# Generate Monster Sprites

Create a production-ready set of separate animation-frame PNGs, not a combined sprite sheet. Use the built-in image generation tool and preserve the current project naming, composition, and import conventions.

## Defaults

- Output root: `Assets/Art/Sprites/Monster`
- Canvas: `128x128` RGBA PNG
- Camera: locked three-quarter side view, facing left
- Ground line: bottom of visible subject at image row 125-126
- Background: fully transparent; no floor, cast shadow, contact shadow, text, or watermark
- Frames: `Idle1..2`, `Move1..4`, `Attack1..4`, `Die1..3`
- Naming: `<PascalCaseMonster>_<Animation><1-based frame>.png`
- Unity target: single sprite, 32 pixels per unit, mipmaps disabled, alpha-is-transparency enabled

Apply explicit user overrides to frame counts, facing direction, destination, or naming. Keep all other defaults.

## Runtime Portability

- Resolve every project path relative to the current repository. Never assume a drive letter, absolute checkout path, or a specific user home directory.
- Resolve the installed `imagegen` skill from the current skill catalog. Do not hard-code its installation path. If it is unavailable, stop before generation and report the missing dependency.
- Before running the bundled Python scripts, verify that the selected interpreter can import Pillow with `python -c "from PIL import Image"`.
- Prefer an existing project Python with Pillow. In the ChatGPT desktop app, use `load_workspace_dependencies` when available and select its bundled Python executable. Otherwise use an available Python environment with Pillow; if none exists, explain the missing dependency and ask before installing anything system-wide.
- Keep generated source and alpha intermediates under the current repository's `tmp/imagegen/` directory so the workflow behaves the same on Windows, macOS, and Linux checkouts.
- Do not require Unity to be running for generation, alpha conversion, normalization, or validation. If Unity is unavailable, finish the PNG set and report Unity import and `.meta` generation as pending.

## Workflow

1. Resolve the monster's English PascalCase asset name. Translate common Korean creature names directly, such as `버섯` to `Mushroom`. Ask only when the intended identity or asset name is genuinely ambiguous.
2. Read [references/sprite-spec.md](references/sprite-spec.md).
3. Inspect the existing reference PNGs with the image viewer before generating:
   - Use `Assets/Art/Sprites/Monster/Wolf/Wolf_Idle1.png` and `Wolf_Idle2.png` for idle framing.
   - Use matching Bear and Wolf frame numbers for Move, Attack, and Die pose/style references.
   - Treat existing monsters as style, camera, scale, and motion references. Do not copy their anatomy into the new monster.
4. Check the destination before writing. Do not overwrite any existing expected PNG unless the user explicitly requested replacement. If a collision exists, ask whether to replace it or create a versioned set.
5. Use the installed `imagegen` skill and its built-in-tool path. Never switch to the CLI/API fallback unless the user explicitly authorizes it.
6. Create `Idle1` first as the identity anchor. Reference the existing Wolf idle frame(s) for style and framing.
7. Create every remaining frame with one built-in image-generation call per asset. Reference:
   - the generated `<Monster>_Idle1` identity anchor, and
   - the matching Bear/Wolf action frame(s).
   Label the images' roles in every prompt. Preserve species, silhouette, palette, proportions, markings, camera, lighting, and apparent body scale. Change only the pose needed for that frame.
8. Generate every raw source on a perfectly flat removable chroma-key background. Default to `#00ff00`; use `#ff00ff` when the subject contains important green tones. Require no gradient, texture, reflection, floor plane, or shadow.
9. Copy each selected raw output from the built-in output location into `tmp/imagegen/<monster-kebab>/raw/` using its final expected filename.
10. Convert every raw file to an alpha PNG in `tmp/imagegen/<monster-kebab>/alpha/` with the installed imagegen helper:

   ```text
   python <imagegen-skill>/scripts/remove_chroma_key.py --input <raw.png> --out <alpha.png> --auto-key border --soft-matte --transparent-threshold 12 --opaque-threshold 220 --despill
   ```

   Validate the intermediate result. If a fringe remains, retry once with `--edge-contract 1`. Use `--edge-feather 0.25` only for visibly stair-stepped opaque edges. If local removal still fails or true native transparency is required, follow the imagegen skill and ask before using its CLI fallback.
11. Normalize the complete alpha set together so scale and grounding stay consistent:

   ```text
   python <this-skill>/scripts/normalize_sprite_set.py --input-dir tmp/imagegen/<monster-kebab>/alpha --output-dir Assets/Art/Sprites/Monster/<MonsterName> --base-name <MonsterName>
   ```

12. Validate all final files:

   ```text
   python <this-skill>/scripts/validate_sprite_set.py --input-dir Assets/Art/Sprites/Monster/<MonsterName> --base-name <MonsterName> --key-color <used-key-color>
   ```

13. Visually inspect the 13 final PNGs. Check character identity, action readability, chronological motion, facing direction, apparent scale, stable ground line, transparent background, and edge halos. Regenerate only the bad frame while reusing `Idle1` and adjacent action frames as references, then normalize and validate the full set again.
14. Let Unity create `.meta` files. When Unity tooling is available, refresh the destination and match the neighboring monster import settings: Sprite (2D and UI), Single, 32 PPU, mipmaps off, alpha transparency on.
15. Report the saved folder, generated frame list, chroma key used, validation result, and any deviations from defaults.

## Generation Rules

- Produce separate images. Do not generate a sprite sheet and crop it unless the user explicitly requests that workflow.
- Keep the full subject inside the canvas with generous source padding; do not crop ears, limbs, weapons, caps, particles, or the death pose.
- Use one coherent light direction and painterly semi-realistic rendering matching Bear/Wolf.
- Keep background removal practical: opaque creature body and crisp antialiased edges. Avoid smoke, glow, translucent effects, motion blur, and detached particles unless requested.
- Do not fabricate animation controllers, clips, prefabs, or gameplay code unless the user asks for them.
