# Tools — ComfyUI 일괄 이미지 생성

게임 오브젝트/아이템 N종에 대해 ComfyUI로 1종당 K장씩 자동 생성.
파이썬 표준 라이브러리만 사용 (urllib, json) — `pip install` 불필요.

---

## 파일 구조

```
Tools/
├── README.md                       # 이 문서
├── comfyui_generate.py             # 메인 실행 스크립트
├── item_prompts.py                 # 생성할 아이템 + 프롬프트 정의
├── convert_workflow_to_api.py      # 일반 Save → API Format 변환기
├── GameItem.json                   # ComfyUI에서 받은 워크플로우 (일반 Save)
└── GameItem_api.json               # 변환 결과 (스크립트가 실제로 사용)
```

생성 결과: `Assets/Art/Sprites/Items/Generated/{ItemName}_{1..K}.png`

---

## 사용 시나리오

### 시나리오 A — 같은 아이템 다시 생성 (스타일 다양화)

기존 13종을 다른 시드로 한 번 더 뽑고 싶을 때.

```bash
# 1. ComfyUI 띄우기 (이미 떠있으면 스킵)
# 2. 그냥 실행. 시드는 매 호출마다 자동으로 다름.
PYTHONIOENCODING=utf-8 python Tools/comfyui_generate.py
```

**주의**: 같은 파일명으로 덮어쓰기됨. 이전 결과 보관 필요하면 `Generated/` 폴더 백업 후 실행.

### 시나리오 B — 신규 아이템 추가 생성

Phase D/E 등에서 새 오브젝트 종류가 생긴 경우.

1. **`Tools/item_prompts.py`** 의 `ITEM_PROMPTS` 리스트에 새 항목 추가:

```python
{
    "id": 200,
    "name": "MyNewItem",
    "core": (
        "specific visual description here, "
        "key shape and material, "
        "context vibe and props"
    ),
},
```

2. (선택) `PREFIX` / `SUFFIX` / `NEGATIVE` 도 필요하면 같은 파일에서 조정.

3. 실행:
```bash
PYTHONIOENCODING=utf-8 python Tools/comfyui_generate.py
```

기존 항목까지 모두 다시 생성됩니다. **신규 항목만** 뽑고 싶으면 `ITEM_PROMPTS`를 임시로 신규 항목만 남기고 실행.

### 시나리오 C — 다른 워크플로우 / 다른 모델로 생성

Stable Diffusion 모델 교체, LoRA 추가, 샘플러 변경 등.

1. ComfyUI 웹 UI에서 새 워크플로우 셋업 + 1회 테스트 생성으로 정상 동작 확인
2. 워크플로우를 Save → 다운로드된 JSON을 **`Tools/GameItem.json`** 에 덮어쓰기
3. 변환 + 실행:

```bash
# Step 3-1: 일반 Save → API Format 변환
PYTHONIOENCODING=utf-8 python Tools/convert_workflow_to_api.py Tools/GameItem.json Tools/GameItem_api.json

# Step 3-2: 본 생성
PYTHONIOENCODING=utf-8 python Tools/comfyui_generate.py
```

---

## ComfyUI 워크플로우 저장 방법

ComfyUI는 **두 가지 JSON 포맷**이 있고 우리는 **API Format**이 필요합니다:

| 포맷 | 저장 메뉴 | 구조 | 우리 스크립트가 |
|------|----------|------|----------------|
| **일반 Save** | `Save` (기본) | `{"nodes": [...], "links": [...]}` | ✗ 직접 못 씀 |
| **API Format** | `Save (API Format)` (Dev mode 필요) | `{"node_id": {...}}` | ✓ 그대로 사용 |

### 권장 워크플로우: 일반 Save → 변환

이유: "Save (API Format)" 메뉴 위치가 ComfyUI 버전마다 다르고 못 찾기 쉬움. 일반 Save는 항상 보임.

1. ComfyUI 웹 UI에서 워크플로우 작업 후 평소처럼 **Save** 클릭
2. 다운로드된 JSON을 `Tools/GameItem.json` 으로 두기
3. `convert_workflow_to_api.py` 실행 → `GameItem_api.json` 생성

### 직접 API Format으로 저장하는 방법 (가능한 경우)

1. **Settings(⚙) → "Enable Dev mode Options"** 체크
2. 메뉴에서 **"Save (API Format)"** 클릭
3. 다운로드된 JSON을 `Tools/GameItem_api.json` 으로 두기 (이름이 `_api` 포함)
4. 변환 단계 스킵, 바로 `comfyui_generate.py` 실행

---

## 스크립트 설정 (CONFIG)

`Tools/comfyui_generate.py` 상단:

```python
COMFYUI_URL = "http://127.0.0.1:8188"               # ComfyUI 서버 주소
WORKFLOW_PATH = ... / "GameItem_api.json"            # 사용할 워크플로우
OUTPUT_DIR = ... / "Assets/Art/Sprites/Items/Generated"  # 결과 저장 위치
IMAGES_PER_ITEM = 3                                  # 1종당 생성 장수

PROMPT_LOOKUP_MODE = "by_class"   # 양성/음성 노드 자동 탐색 방식
SEED_LOOKUP_MODE = "by_class"     # KSampler 노드 자동 탐색 방식
```

### 자동 탐색 동작

- **양성/음성 프롬프트**: 우선 KSampler의 `positive`/`negative` 입력 링크를 따라가서 정확히 식별. 실패 시 dict 순서 첫 번째 = 양성으로 fallback.
- **KSampler 시드**: 첫 번째 `KSampler` 또는 `KSamplerAdvanced` 노드. `seed` 또는 `noise_seed` 필드 자동 감지.

### 자동 탐색 실패 시

`CLIPTextEncode`가 2개가 아니거나 KSampler가 다른 노드 타입이면 직접 지정:

```python
PROMPT_LOOKUP_MODE = "by_id"
NODE_ID_POSITIVE = "11"   # 본인 워크플로우의 양성 프롬프트 노드 ID
NODE_ID_NEGATIVE = "12"

SEED_LOOKUP_MODE = "by_id"
NODE_ID_KSAMPLER = "19"
```

노드 ID는 `GameItem_api.json` 파일을 열어서 `class_type`이 `CLIPTextEncode` / `KSampler`인 항목 찾으면 됨.

---

## Windows 환경 주의사항

### 한글 콘솔 인코딩

스크립트가 한글을 출력할 때 `cp949` 에러가 나면:

```bash
# 매번 명령 앞에 붙이기
PYTHONIOENCODING=utf-8 python Tools/comfyui_generate.py

# 또는 세션 시작 시 한 번 export
export PYTHONIOENCODING=utf-8
```

### Python stdout 버퍼링

장시간 실행 중 콘솔에 진행 로그가 안 보일 수 있음 (Windows + Python 조합 특성).
이 경우 **출력 폴더 파일 개수**로 진행 확인:

```bash
ls Assets/Art/Sprites/Items/Generated/ | wc -l
```

또는 `python -u` 옵션으로 unbuffered 실행:
```bash
PYTHONIOENCODING=utf-8 python -u Tools/comfyui_generate.py
```

---

## 트러블슈팅

| 증상 | 원인 / 해결 |
|------|-------------|
| `[ERROR] 워크플로우 파일 없음` | `GameItem_api.json` 경로/이름 확인 |
| `'nodes' 키 없음 — 알 수 없는 포맷` | API Format이 아닌 다른 형태. ComfyUI에서 다시 받기 |
| `ComfyUI 연결 실패` | ComfyUI 서버 실행 여부, `COMFYUI_URL` 포트 확인 |
| `CLIPTextEncode 노드를 2개 찾아야 하는데 N개` | 워크플로우 구조 다름 → `by_id` 모드 + 노드 ID 직접 지정 |
| `KSampler 노드 못 찾음` | 커스텀 샘플러 사용 중. 노드 클래스명 확인 후 `KSampler`/`KSamplerAdvanced` 외 추가 필요 |
| `prompt_id ... 타임아웃` | 1장 생성에 5분 이상 걸림 → 모델/이미지 크기 줄이거나 `TOTAL_TIMEOUT_SEC` 늘리기 |
| 생성 결과가 의도와 다름 | `item_prompts.py`의 `core` 묘사 + `PREFIX`/`SUFFIX`/`NEGATIVE` 튜닝 |

---

## 프롬프트 작성 가이드

`item_prompts.py`의 각 항목은 다음 구조로 합쳐집니다:

```
{PREFIX}, {core}, fantasy RPG style, {SUFFIX}
```

### 좋은 `core` 묘사 패턴

1. **주제어 (1구절)** — `wooden chopping block`
2. **세부 묘사 (1~2구절)** — `axe stuck in tree stump, woodcutter station`
3. **재질/디테일 (1~2구절)** — `fresh wood chips scattered, exposed tree rings, bark texture`
4. **분위기/맥락 (1구절)** — `rustic lumberjack prop, sturdy round base`

각 구절은 쉼표로 구분, 한 항목당 50~80 단어 권장. 너무 길면 모델이 무시하기 시작.

### NEGATIVE 활용

기본값 `low quality, blurry, jpeg artifacts, watermark, text, logo, multiple objects, busy background, photorealistic, human figure` 는 게임 에셋용으로 일반화된 음성. 특정 모델/LoRA에서 자주 나오는 결함이 있으면 추가:

```python
NEGATIVE = "기본값 + 추가 키워드"
```

---

## 작업 흐름 요약 (다음에 다시 시작할 때)

```bash
# 1. ComfyUI 띄움
# 2. 워크플로우 셋업 + 1회 테스트
# 3. ComfyUI에서 Save → Tools/GameItem.json 에 두기
# 4. 변환 (워크플로우 변경 시에만)
PYTHONIOENCODING=utf-8 python Tools/convert_workflow_to_api.py Tools/GameItem.json Tools/GameItem_api.json

# 5. (선택) item_prompts.py에서 아이템 추가/수정
# 6. 실행
PYTHONIOENCODING=utf-8 python Tools/comfyui_generate.py

# 7. 결과 확인
ls Assets/Art/Sprites/Items/Generated/
```

---

## Step U1 자동화 — Generated/ → Items/ + Addressable 등록

`comfyui_generate.py` 결과 39장 중 마음에 드는 1장씩만 남기고 Unity에 정식 자산으로 import + Addressable 등록까지 자동화.

### 사용 방법

1. **불필요한 이미지 삭제**: `Assets/Art/Sprites/Items/Generated/` 폴더에서 사용 안 할 이미지 직접 삭제
   - 각 오브젝트당 1장만 남기는 것을 권장 (예: `Anvil_2.png`만 두고 `Anvil_1.png`, `Anvil_3.png` 삭제)
   - 같은 이름 여러 장 남기면 가장 작은 인덱스(`_1`) 우선 사용
2. **Unity 메뉴 클릭**: `ARPG → Sprites → Import Generated Items`
3. 콘솔 로그에서 결과 확인

### 에디터 스크립트 동작

[Assets/Scripts/Editor/ImportGeneratedSprites.cs](../Assets/Scripts/Editor/ImportGeneratedSprites.cs) 가:
- `Generated/{Name}_N.png` → `Items/{Name}.png` 로 이동
- Sprite import 설정 적용 (Pivot Bottom, PixelsPerUnit 100, Alpha Transparency)
- Default Local Group에 `Sprites/Items/{Name}` 키로 Addressable 등록 (이미 있으면 갱신)

### Build Addressables

import 후 Addressable 빌드 필요:
- 메뉴: `ARPG → Sprites → Build Addressables`
- 또는: `Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script`

---

## 의존성

- Python 3.7+ (표준 라이브러리만)
- ComfyUI (어떤 버전이든 OK — `/prompt`, `/history`, `/view`, `/object_info` API 사용)
- `pip install` 불필요
