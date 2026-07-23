# Monster Sprite Specification

## Required files

| Animation | Frames | Required names for `<Name>` |
| --- | ---: | --- |
| Idle | 2 | `<Name>_Idle1.png`, `<Name>_Idle2.png` |
| Move | 4 | `<Name>_Move1.png` through `<Name>_Move4.png` |
| Attack | 4 | `<Name>_Attack1.png` through `<Name>_Attack4.png` |
| Die | 3 | `<Name>_Die1.png` through `<Name>_Die3.png` |

## Pose progression

| Frame | Motion purpose |
| --- | --- |
| Idle1 | Neutral identity anchor with a readable silhouette. |
| Idle2 | Subtle breathing or weight shift; feet and camera remain stable. |
| Move1 | First contact pose with the leading side advancing. |
| Move2 | Weight transfer and passing pose. |
| Move3 | Opposite contact pose. |
| Move4 | Recovery/passing pose that loops cleanly into Move1. |
| Attack1 | Anticipation or wind-up. |
| Attack2 | Main lunge, bite, strike, or species-appropriate attack. |
| Attack3 | Impact/follow-through at maximum extension. |
| Attack4 | Recovery toward the neutral stance. |
| Die1 | Hit reaction or loss of balance. |
| Die2 | Collapse in progress. |
| Die3 | Fully grounded final pose with no remaining active tension. |

## Shared prompt scaffold

Use only the fields that help the current frame.

```text
Use case: stylized-concept
Asset type: Unity 2D ARPG monster animation frame
Primary request: Create <Name> <Animation><Frame> of <Total>.
Input images: Image 1 is the generated identity anchor; Images 2-3 are pose and project-style references only.
Subject: <brief monster description>; preserve the anchor's exact species, silhouette, proportions, palette, surface details, and markings.
Style/medium: painterly semi-realistic fantasy creature sprite matching the existing Bear and Wolf assets.
Composition/framing: locked three-quarter side view facing left; full body visible; stable camera, scale, and ground contact.
Lighting/mood: soft neutral game-sprite lighting matching the references.
Scene/backdrop: perfectly flat solid <key-color> chroma-key background.
Constraints: change only the pose required for this frame; coherent chronological animation; crisp antialiased outline; generous padding; no cropped anatomy.
Avoid: floor plane, cast/contact shadow, gradient, texture, reflections, scenery, text, watermark, motion blur, detached particles, glow, transparency in the subject, and <key-color> in the subject.
```

For `Idle1`, omit the generated identity anchor and describe the intended monster clearly. For subsequent frames, repeat identity invariants rather than relying on conversation memory.

## Reference routing

- Idle: use Wolf Idle frames for camera, placement, scale, and subtle motion.
- Move: use matching `Bear_MoveN` and `Wolf_MoveN` frames.
- Attack: use matching `Bear_AttackN` and `Wolf_AttackN` frames.
- Die: use matching `Bear_DieN` and `Wolf_DieN` frames.
- When an existing reference is missing, use the nearest frame in the same action and state the intended motion phase explicitly.

## Final visual checks

- All frames show the same individual monster.
- The monster faces left unless overridden.
- Idle and Move loop without a camera jump.
- Attack reads as anticipation, action, follow-through, and recovery.
- Die reads as hit, collapse, and final resting pose.
- Feet/body share a stable ground line; Die3 may extend horizontally but stays fully inside the canvas.
- Corners and background are transparent.
- No green/magenta key fringe remains on partially transparent edge pixels.

