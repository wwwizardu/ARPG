"""
ComfyUI 일반 Save Format → API Format 변환기.

일반 Save Format: {"nodes": [...], "links": [...]} 형태 (UI 메타데이터 포함)
API Format: {"node_id": {"class_type": ..., "inputs": {...}}} 형태 (서버 제출용)

사용법:
  python Tools/convert_workflow_to_api.py Tools/GameItem.json Tools/GameItem_api.json

ComfyUI 서버가 떠있어야 함 — /object_info 로 각 노드 클래스의 input 스펙을 조회해서
widgets_values 와 inputs 리스트를 정확히 매핑.
"""

import json
import sys
import urllib.request
import urllib.error
from pathlib import Path

COMFYUI_URL = "http://127.0.0.1:8188"

# ComfyUI에서 link/connection 으로만 사용되는 타입 (widget이 아님)
LINK_ONLY_TYPES = {
    "MODEL", "CLIP", "VAE", "CONDITIONING", "LATENT", "IMAGE", "MASK",
    "CONTROL_NET", "STYLE_MODEL", "CLIP_VISION", "CLIP_VISION_OUTPUT",
    "GLIGEN", "UPSCALE_MODEL", "SAMPLER", "SIGMAS", "GUIDER", "NOISE",
}

def fetch_object_info() -> dict:
    """/object_info 호출해서 모든 노드 클래스 스펙 반환."""
    url = COMFYUI_URL + "/object_info"
    with urllib.request.urlopen(url, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))

def is_widget_input(input_spec) -> bool:
    """input 스펙이 widget(슬라이더/입력박스/콤보)인지 link인지 판정."""
    if not isinstance(input_spec, list) or not input_spec:
        return False
    type_def = input_spec[0]
    # 콤보 (선택지 리스트) → widget
    if isinstance(type_def, list):
        return True
    # 알려진 link 전용 타입은 widget 아님
    if type_def in LINK_ONLY_TYPES:
        return False
    # 그 외 (INT, FLOAT, STRING, BOOLEAN 등) → widget
    return True

def convert(regular: dict, object_info: dict) -> dict:
    """일반 포맷 dict → API 포맷 dict."""
    api = {}
    nodes = regular.get("nodes", [])
    links = regular.get("links", [])

    # link_id → (from_node_id, from_slot) 매핑 구축
    link_map = {}
    for link in links:
        # link 구조: [link_id, from_node_id, from_slot, to_node_id, to_slot, type]
        if len(link) >= 5:
            link_id, from_node, from_slot = link[0], link[1], link[2]
            link_map[link_id] = (from_node, from_slot)

    for node in nodes:
        node_id = str(node["id"])
        class_type = node["type"]

        # bypass/mute 모드는 스킵 (mode 2 = mute, 4 = bypass)
        if node.get("mode", 0) in (2, 4):
            continue

        # Reroute / Note 같은 시각 전용 노드는 스킵
        if class_type in ("Reroute", "Note", "PrimitiveNode"):
            continue

        # 그룹 nodes (sub-graph) 등 비표준 클래스도 일단 시도
        spec = object_info.get(class_type)
        if spec is None:
            print(f"  WARN: 노드 ID {node_id} class '{class_type}' object_info 없음 — 스킵")
            continue

        api_inputs = {}

        # 클래스의 input 순서 (required → optional 차례)
        input_order_dict = spec.get("input_order", {})
        input_specs = spec.get("input", {})

        ordered_names = []
        for section in ("required", "optional"):
            ordered_names.extend(input_order_dict.get(section, []))

        # 노드의 connected inputs (이름 → link_id)
        connected = {}
        for inp in node.get("inputs", []):
            link_id = inp.get("link")
            if link_id is not None:
                connected[inp["name"]] = link_id

        # widgets_values를 순서대로 소비 (widget input만 매핑)
        widget_values = node.get("widgets_values", [])
        widget_idx = 0

        for input_name in ordered_names:
            input_spec_def = input_specs.get("required", {}).get(input_name) \
                          or input_specs.get("optional", {}).get(input_name)
            if input_spec_def is None:
                continue

            if input_name in connected:
                # link 형태: [from_node_id_str, from_slot]
                link_id = connected[input_name]
                if link_id in link_map:
                    from_node, from_slot = link_map[link_id]
                    api_inputs[input_name] = [str(from_node), from_slot]
            elif is_widget_input(input_spec_def):
                # widget 값 소비
                if widget_idx < len(widget_values):
                    api_inputs[input_name] = widget_values[widget_idx]
                    widget_idx += 1
                    # 특수 케이스: KSampler의 'seed' 다음에 'control_after_generate'가 widget으로 따라옴
                    # API에는 seed만 필요하므로 control_after_generate가 있으면 추가로 1개 더 소비 (skip)
                    if input_name == "seed" and widget_idx < len(widget_values):
                        # 다음 값이 문자열 ("randomize"/"fixed"/"increment"/"decrement")이면 skip
                        next_val = widget_values[widget_idx]
                        if isinstance(next_val, str) and next_val in ("randomize", "fixed", "increment", "decrement"):
                            widget_idx += 1
                    elif input_name == "noise_seed" and widget_idx < len(widget_values):
                        next_val = widget_values[widget_idx]
                        if isinstance(next_val, str) and next_val in ("randomize", "fixed", "increment", "decrement"):
                            widget_idx += 1

        api[node_id] = {
            "class_type": class_type,
            "inputs": api_inputs,
        }
        # 메타정보 (선택)
        if "_meta" not in api[node_id]:
            api[node_id]["_meta"] = {"title": node.get("title", class_type)}

    return api

def main():
    if len(sys.argv) < 3:
        print("사용법: python Tools/convert_workflow_to_api.py <input.json> <output.json>")
        sys.exit(1)

    in_path = Path(sys.argv[1])
    out_path = Path(sys.argv[2])

    if not in_path.exists():
        print(f"[ERROR] 입력 파일 없음: {in_path}")
        sys.exit(1)

    with open(in_path, encoding="utf-8") as f:
        regular = json.load(f)

    if "nodes" not in regular:
        # 이미 API 포맷일 수도
        sample_key = next(iter(regular), None)
        if sample_key and isinstance(regular[sample_key], dict) and "class_type" in regular[sample_key]:
            print(f"[INFO] 이미 API 포맷 — 그대로 복사")
            out_path.write_text(json.dumps(regular, indent=2, ensure_ascii=False), encoding="utf-8")
            return
        print(f"[ERROR] 'nodes' 키 없음 — 알 수 없는 포맷")
        sys.exit(1)

    print(f"[INFO] ComfyUI /object_info 조회 중...")
    try:
        object_info = fetch_object_info()
    except urllib.error.URLError as e:
        print(f"[ERROR] ComfyUI 연결 실패 ({COMFYUI_URL}): {e}")
        sys.exit(1)
    print(f"[INFO] {len(object_info)} 개 노드 클래스 스펙 로드")

    print(f"[INFO] 변환 중... ({len(regular['nodes'])} nodes)")
    api = convert(regular, object_info)
    print(f"[INFO] 변환 완료: {len(api)} 노드")

    out_path.write_text(json.dumps(api, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"[DONE] 저장: {out_path}")

if __name__ == "__main__":
    main()
