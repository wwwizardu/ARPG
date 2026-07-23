#!/usr/bin/env python3
"""Validate names, dimensions, alpha, grounding, and chroma fringe for a sprite set."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

try:
    from PIL import Image
except ModuleNotFoundError as exception:
    print("Pillow is required. Select a Python runtime with Pillow installed.", file=sys.stderr)
    raise SystemExit(2) from exception


ANIMATION_COUNTS = (("Idle", 2), ("Move", 4), ("Attack", 4), ("Die", 3))
HEX_COLOR = re.compile(r"^#?([0-9a-fA-F]{6})$")


def expected_names(base_name: str) -> list[str]:
    return [
        f"{base_name}_{animation}{frame}.png"
        for animation, count in ANIMATION_COUNTS
        for frame in range(1, count + 1)
    ]


def parse_color(value: str) -> tuple[int, int, int]:
    match = HEX_COLOR.match(value)
    if match is None:
        raise argparse.ArgumentTypeError("color must be a 6-digit RGB hex value")
    raw = match.group(1)
    return tuple(int(raw[index : index + 2], 16) for index in (0, 2, 4))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--base-name", required=True)
    parser.add_argument("--canvas-size", type=int, default=128)
    parser.add_argument("--ground-y", type=int, default=126)
    parser.add_argument("--ground-tolerance", type=int, default=2)
    parser.add_argument("--alpha-threshold", type=int, default=8)
    parser.add_argument("--key-color", type=parse_color)
    parser.add_argument("--fringe-distance", type=int, default=42)
    parser.add_argument("--max-fringe-ratio", type=float, default=0.005)
    return parser.parse_args()


def validate_file(path: Path, args: argparse.Namespace) -> list[str]:
    errors: list[str] = []
    try:
        with Image.open(path) as opened:
            opened.load()
            if opened.format != "PNG":
                errors.append(f"{path.name}: format is {opened.format}, expected PNG")
            if opened.mode != "RGBA":
                errors.append(f"{path.name}: mode is {opened.mode}, expected RGBA")
            if opened.size != (args.canvas_size, args.canvas_size):
                errors.append(
                    f"{path.name}: size is {opened.width}x{opened.height}, "
                    f"expected {args.canvas_size}x{args.canvas_size}"
                )
            image = opened.convert("RGBA")
    except Exception as exception:
        return [f"{path.name}: cannot read image ({exception})"]

    alpha = image.getchannel("A")
    minimum_alpha, maximum_alpha = alpha.getextrema()
    if minimum_alpha != 0:
        errors.append(f"{path.name}: has no fully transparent pixels")
    if maximum_alpha < 250:
        errors.append(f"{path.name}: has no substantially opaque pixels")

    corners = (
        alpha.getpixel((0, 0)),
        alpha.getpixel((args.canvas_size - 1, 0)),
        alpha.getpixel((0, args.canvas_size - 1)),
        alpha.getpixel((args.canvas_size - 1, args.canvas_size - 1)),
    )
    if any(value > args.alpha_threshold for value in corners):
        errors.append(f"{path.name}: one or more corners are not transparent")

    mask = alpha.point(lambda value: 255 if value > args.alpha_threshold else 0)
    bbox = mask.getbbox()
    if bbox is None:
        errors.append(f"{path.name}: contains no visible subject")
        return errors

    visible_pixels = sum(1 for value in alpha.tobytes() if value > args.alpha_threshold)
    coverage = visible_pixels / (args.canvas_size * args.canvas_size)
    if not 0.01 <= coverage <= 0.80:
        errors.append(f"{path.name}: implausible visible coverage {coverage:.1%}")

    if abs(bbox[3] - args.ground_y) > args.ground_tolerance:
        errors.append(
            f"{path.name}: visible bottom is row {bbox[3]}, "
            f"expected {args.ground_y}±{args.ground_tolerance}"
        )

    if args.key_color is not None:
        key_r, key_g, key_b = args.key_color
        visible_count = 0
        fringe_count = 0
        rgba = image.tobytes()
        for offset in range(0, len(rgba), 4):
            red, green, blue, pixel_alpha = rgba[offset : offset + 4]
            if pixel_alpha <= args.alpha_threshold:
                continue
            visible_count += 1
            distance = abs(red - key_r) + abs(green - key_g) + abs(blue - key_b)
            if distance <= args.fringe_distance and pixel_alpha < 250:
                fringe_count += 1
        fringe_ratio = fringe_count / max(1, visible_count)
        if fringe_ratio > args.max_fringe_ratio:
            errors.append(f"{path.name}: possible chroma fringe ratio {fringe_ratio:.2%}")

    return errors


def main() -> int:
    args = parse_args()
    names = expected_names(args.base_name)
    errors: list[str] = []

    for name in names:
        path = args.input_dir / name
        if path.is_file() is False:
            errors.append(f"Missing expected file: {path}")
            continue
        errors.extend(validate_file(path, args))

    if errors:
        print(f"Sprite-set validation failed with {len(errors)} issue(s):", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print(
        f"Validated {len(names)} sprites in {args.input_dir}: "
        f"names, PNG/RGBA, {args.canvas_size}x{args.canvas_size}, alpha, grounding, and fringe checks passed."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
