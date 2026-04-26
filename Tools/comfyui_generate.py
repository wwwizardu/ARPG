"""
Phase C 빌드 가능 오브젝트 13종 × 3장 = 39장 ComfyUI 일괄 생성 스크립트.

사용법:
  1) ComfyUI 띄움 (기본 http://127.0.0.1:8188)
  2) 평소 쓰는 워크플로우를 Settings → "Save (API Format)"로 JSON 저장 → Tools/GameItem.json 에 두기
  3) (필요 시) 본 스크립트 상단 CONFIG 섹션의 노드 ID/필드명 조정
  4) `python Tools/comfyui_generate.py` 실행 — 전체 ITEM_PROMPTS 생성
     또는 `python Tools/comfyui_generate.py --only Name1,Name2` — 특정 이름만 생성

생성 파일 경로:
  Assets/Art/Sprites/Items/Generated/{ItemName}_{1,2,3}.png

스크립트는 파이썬 표준 라이브러리(urllib, json, uuid)만 사용 — 추가 패키지 불필요.
"""

import json
import os
import sys
import time
import uuid
import copy
import urllib.request
import urllib.parse
import urllib.error
from pathlib import Path

# 같은 디렉터리의 item_prompts.py
sys.path.insert(0, str(Path(__file__).parent))
from item_prompts import ITEM_PROMPTS, make_positive_prompt, make_negative_prompt

# =====================================================================
# CONFIG — 환경에 맞춰 조정
# =====================================================================

COMFYUI_URL = "http://127.0.0.1:8188"
WORKFLOW_PATH = Path(__file__).parent / "GameItem_api.json"
OUTPUT_DIR = Path(__file__).parent.parent / "Assets" / "Art" / "Sprites" / "Items" / "Generated"
IMAGES_PER_ITEM = 3

# 워크플로우 JSON 안에서 양성/음성 프롬프트가 들어있는 노드 식별 방식.
# - "by_class": class_type 으로 자동 탐색 ("CLIPTextEncode" 노드 2개를 찾되,
#   첫 번째 = 양성, 두 번째 = 음성 가정. 일반 워크플로우의 흔한 배치).
# - "by_id": NODE_ID_POSITIVE / NODE_ID_NEGATIVE 의 값을 사용 (사용자 직접 지정).
PROMPT_LOOKUP_MODE = "by_class"   # "by_class" 또는 "by_id"
NODE_ID_POSITIVE = "6"            # PROMPT_LOOKUP_MODE == "by_id" 일 때만 사용
NODE_ID_NEGATIVE = "7"

# 시드를 매 호출마다 다르게 하기 위해 KSampler 노드도 식별
SEED_LOOKUP_MODE = "by_class"     # "by_class" 또는 "by_id"
NODE_ID_KSAMPLER = "3"            # SEED_LOOKUP_MODE == "by_id" 일 때만 사용

# 폴링 타임아웃
POLL_INTERVAL_SEC = 1.0
TOTAL_TIMEOUT_SEC = 300           # 1장당 최대 대기 (5분)

CLIENT_ID = str(uuid.uuid4())     # WebSocket 미사용 — POST/GET만으로 진행

# =====================================================================
# ComfyUI HTTP 호출
# =====================================================================

def _http_get(path: str) -> bytes:
    url = COMFYUI_URL + path
    with urllib.request.urlopen(url, timeout=30) as resp:
        return resp.read()

def _http_post_json(path: str, payload: dict) -> dict:
    url = COMFYUI_URL + path
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        url, data=data, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))

def queue_prompt(workflow: dict) -> str:
    """워크플로우 제출 → prompt_id 반환."""
    payload = {"prompt": workflow, "client_id": CLIENT_ID}
    result = _http_post_json("/prompt", payload)
    if "prompt_id" not in result:
        raise RuntimeError(f"ComfyUI /prompt 응답 비정상: {result}")
    return result["prompt_id"]

def wait_for_completion(prompt_id: str) -> dict:
    """폴링으로 완료 대기 → history dict 반환."""
    deadline = time.time() + TOTAL_TIMEOUT_SEC
    while time.time() < deadline:
        try:
            raw = _http_get(f"/history/{prompt_id}")
            hist = json.loads(raw.decode("utf-8"))
            if prompt_id in hist:
                entry = hist[prompt_id]
                # status.completed == True 면 작업 종료
                status = entry.get("status", {})
                if status.get("completed"):
                    return entry
                # status_str == "error"는 즉시 실패
                if status.get("status_str") == "error":
                    raise RuntimeError(f"ComfyUI 작업 실패: {status}")
        except urllib.error.URLError:
            pass
        time.sleep(POLL_INTERVAL_SEC)
    raise TimeoutError(f"prompt_id={prompt_id} {TOTAL_TIMEOUT_SEC}초 안에 미완료")

def fetch_image(filename: str, subfolder: str, type_: str) -> bytes:
    qs = urllib.parse.urlencode({"filename": filename, "subfolder": subfolder, "type": type_})
    return _http_get(f"/view?{qs}")

# =====================================================================
# 워크플로우 변형
# =====================================================================

def find_node_by_class(workflow: dict, class_type: str) -> list:
    return [nid for nid, node in workflow.items() if node.get("class_type") == class_type]

def find_prompt_nodes_via_ksampler(workflow: dict) -> tuple:
    """
    KSampler의 positive/negative 입력 링크를 추적해 양성/음성 CLIPTextEncode 노드 ID 반환.
    링크는 ["from_node_id_str", from_slot] 형태.
    """
    ksampler_classes = ("KSampler", "KSamplerAdvanced")
    for nid, node in workflow.items():
        if node.get("class_type") not in ksampler_classes:
            continue
        inputs = node.get("inputs", {})
        pos_link = inputs.get("positive")
        neg_link = inputs.get("negative")
        if isinstance(pos_link, list) and isinstance(neg_link, list):
            return pos_link[0], neg_link[0]
    return None, None

def patch_workflow(workflow: dict, positive: str, negative: str, seed: int) -> dict:
    """
    원본 workflow를 deepcopy하고 prompts/seed만 갈아끼움.
    워크플로우 JSON 구조 가정: ComfyUI "Save (API Format)" 출력.
    """
    wf = copy.deepcopy(workflow)

    # === 프롬프트 노드 식별 + 갱신 ===
    if PROMPT_LOOKUP_MODE == "by_class":
        # 우선: KSampler의 positive/negative 링크를 따라가 정확히 식별
        pos_id, neg_id = find_prompt_nodes_via_ksampler(wf)
        if pos_id is None:
            # 폴백: dict 순서 첫 번째 = positive 가정
            text_nodes = find_node_by_class(wf, "CLIPTextEncode")
            if len(text_nodes) < 2:
                raise RuntimeError(
                    f"CLIPTextEncode 노드를 2개 찾아야 하는데 {len(text_nodes)}개. "
                    f"PROMPT_LOOKUP_MODE를 'by_id'로 바꾸고 NODE_ID_POSITIVE/NEGATIVE 직접 지정.")
            pos_id, neg_id = text_nodes[0], text_nodes[1]
    else:
        pos_id, neg_id = NODE_ID_POSITIVE, NODE_ID_NEGATIVE

    if pos_id not in wf or neg_id not in wf:
        raise RuntimeError(f"노드 ID {pos_id}/{neg_id} 가 워크플로우에 없음")
    wf[pos_id]["inputs"]["text"] = positive
    wf[neg_id]["inputs"]["text"] = negative

    # === KSampler 시드 갱신 (재현성/다양성 위해 매 호출마다 다른 시드) ===
    if SEED_LOOKUP_MODE == "by_class":
        ksampler_nodes = find_node_by_class(wf, "KSampler")
        if not ksampler_nodes:
            ksampler_nodes = find_node_by_class(wf, "KSamplerAdvanced")
        if not ksampler_nodes:
            raise RuntimeError("KSampler/KSamplerAdvanced 노드 못 찾음")
        sampler_id = ksampler_nodes[0]
    else:
        sampler_id = NODE_ID_KSAMPLER
    if sampler_id in wf:
        # KSampler는 'seed', KSamplerAdvanced는 'noise_seed'
        if "seed" in wf[sampler_id]["inputs"]:
            wf[sampler_id]["inputs"]["seed"] = seed
        elif "noise_seed" in wf[sampler_id]["inputs"]:
            wf[sampler_id]["inputs"]["noise_seed"] = seed

    return wf

# =====================================================================
# 메인 루프
# =====================================================================

def parse_only_filter() -> set:
    """--only Name1,Name2 인자 파싱 → 이름 set 반환. 없으면 빈 set (=전체 생성)."""
    only = set()
    for i, arg in enumerate(sys.argv):
        if arg == "--only" and i + 1 < len(sys.argv):
            for name in sys.argv[i + 1].split(","):
                name = name.strip()
                if name:
                    only.add(name)
    return only

def main():
    if not WORKFLOW_PATH.exists():
        print(f"[ERROR] 워크플로우 파일 없음: {WORKFLOW_PATH}")
        print("       ComfyUI 웹 UI에서 'Save (API Format)' 으로 저장 후 위 경로에 두세요.")
        print("       (Settings에서 'Enable Dev mode Options' 켜면 메뉴 보임)")
        sys.exit(1)

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    with open(WORKFLOW_PATH, encoding="utf-8") as f:
        base_workflow = json.load(f)

    only_names = parse_only_filter()
    items_to_generate = [it for it in ITEM_PROMPTS if (not only_names or it["name"] in only_names)]

    if only_names and not items_to_generate:
        print(f"[ERROR] --only {only_names} 로 매칭되는 ITEM_PROMPTS 항목 없음")
        sys.exit(1)

    print(f"[INFO] ComfyUI: {COMFYUI_URL}")
    print(f"[INFO] Workflow: {WORKFLOW_PATH.name} ({len(base_workflow)} nodes)")
    print(f"[INFO] Output: {OUTPUT_DIR}")
    if only_names:
        print(f"[INFO] Filter (--only): {','.join(only_names)}")
    print(f"[INFO] {len(items_to_generate)} items × {IMAGES_PER_ITEM} = {len(items_to_generate) * IMAGES_PER_ITEM} images")
    print()

    total = 0
    base_seed = int(time.time()) & 0x7FFFFFFF

    for item in items_to_generate:
        positive = make_positive_prompt(item)
        negative = make_negative_prompt()
        for k in range(1, IMAGES_PER_ITEM + 1):
            seed = (base_seed + total * 7919) & 0x7FFFFFFF
            print(f"[{total+1:>2}/{len(items_to_generate)*IMAGES_PER_ITEM}] {item['name']} #{k} (seed={seed})")
            try:
                wf = patch_workflow(base_workflow, positive, negative, seed)
                prompt_id = queue_prompt(wf)
                entry = wait_for_completion(prompt_id)
                # outputs 에서 이미지 추출 → 파일로 저장
                saved = save_outputs(entry, item["name"], k)
                print(f"     → saved {saved}")
            except Exception as e:
                print(f"     ✗ FAIL: {e}")
            total += 1

    print()
    print(f"[DONE] {total} images attempted. Check {OUTPUT_DIR}")

def save_outputs(history_entry: dict, item_name: str, idx: int) -> list:
    """history entry의 모든 outputs.images를 읽어 디스크 저장. 저장 경로 리스트 반환."""
    saved = []
    outputs = history_entry.get("outputs", {})
    img_count = 0
    for node_id, node_out in outputs.items():
        for img in node_out.get("images", []):
            img_count += 1
            ext = Path(img["filename"]).suffix or ".png"
            # 한 호출에 여러 이미지가 나오면 서픽스 추가
            suffix = "" if img_count == 1 else f"-{img_count}"
            target = OUTPUT_DIR / f"{item_name}_{idx}{suffix}{ext}"
            data = fetch_image(img["filename"], img.get("subfolder", ""), img.get("type", "output"))
            target.write_bytes(data)
            saved.append(target.name)
    return saved

if __name__ == "__main__":
    main()
