# 스킬 페이지 시스템 — 설계 문서

> 선행: [SKILL_DESIGN.md](SKILL_DESIGN.md) §6.8 SkillEffect 합성 시스템 · [SKILLBOOK_DESIGN.md](SKILLBOOK_DESIGN.md)
>
> **상태**: Phase P-1 + 인스턴스 roll 본체 구현 완료. Phase P-3(드랍/상점) 일부 구현. Phase P-2(조건부 효과)는 데이터 컬럼만 보유, 런타임 처리 보류.
>
> 이 문서는 **"무엇을 만들 것인가"** + **"왜 이렇게 정했나"** 를 담는다. 본 문서가 정한 정책이 코드와 어긋나면 코드를 진실로 본다.

---

## 0. 작성 원칙

- **기존 인프라 위에 얹는다**: §6.8 `SkillEffectExecutor` / `SkillEffectTable` / 8종 `SkillTrigger`가 이미 골격으로 존재. 이 문서가 정의하는 스킬 페이지는 본질적으로 SkillEffect의 **인스턴스화 + 비용/슬롯 메타데이터** 추가.
- **데이터 우선**: 새 스킬 페이지 1종 = 시트 행 1개 + (필요 시) `SkillEffectType` enum 1줄 + `SkillEffectExecutor` case 1개. 코드 분기 폭발 금지.
- **양극화 방지를 1차 목표**로 한다: 슬롯 N개 자유 선택 모델의 함정(소수 OP 페이지만 사용됨)을 디자인으로 풀어낸다.
- **v1은 의도적으로 단순화**: 페이지 용량 + 비용 + 단순 페이지 슬롯 N개로만 시작. 트리거 카테고리 분리(§4)는 메타 양극화가 실제로 관측될 때 후속 페이즈에서 도입.

---

## 1. 문제 정의

기존 PoE형 보조 젬 모델(스킬에 보조 N개를 끼워 합성)은 **양극화** 문제를 안고 있다.

| 원인 | 결과 |
|---|---|
| DPS 보조는 곱연산 | 화력 대비 다른 옵션 압도 |
| 넉백/스턴 등 유틸은 정수형 | 빌드 사이즈 커지면 가치 희석 |
| 슬롯 = 제로섬 게임 | "더 좋은 게 있으면 무조건 갈아끼움" |

**한 줄**: "슬롯 N개 자유 선택"은 메타 1~2개로 수렴한다. 본 시스템은 **두 축의 차등화**로 이를 푼다 (v1 단순화 — 트리거 카테고리 분리는 후속 페이즈 §10에 보류).

---

## 2. 핵심 모델 — 두 축의 결합

| 축 | 역할 | 양극화 해소 방식 |
|---|---|---|
| **(A) 스킬북 페이지 용량** | **각 스킬북이 자체 페이지 용량을 가지며**, 페이지 비용을 차감 | 슬롯이 아니라 **자원**을 두고 경쟁. 약한 페이지도 가성비로 살아남음. 책 등급이 페이지 용량 크기를 결정 → 그라인드 동기 (Last Epoch 스타일 + ARPG 드랍 루프) |
| **(C) 조건부 효과** | 스킬 페이지에 `Condition` 부착 (벽 충돌, 스턴, HP < N% 등) | 유틸 페이지를 "조건부 DPS 페이지"로 변환 — 넉백이 깡뎀과 맞붙을 수 있음 |

두 축은 독립적이며 **각각 부분 도입 가능**. 페이즈 분할의 기준이 됨.

> **보류**: 초안에 있던 "트리거 카테고리 슬롯(OnHit/OnCrit/OnKill 분리)"은 v1에서 제거. **단순 페이지 용량 + 비용** 모델만으로 시작하고, 메타 양극화가 실제 관측되면 후속 페이즈에서 재도입 (§10 참조).

---

## 3. (A) 스킬북 페이지 용량

### 3.1 페이지 용량 정의

**각 스킬북 인스턴스가 자체 페이지 용량을 보유한다.** 캐릭터 레벨이 아니라 **책 등급 + 인스턴스 roll**에 종속.

| 위치 | 필드 | 의미 |
|---|---|---|
| `SkillBookTable` (등급별) | `PageCapacity: int`, `PageSlots: int` | 등급이 부여하는 기본 페이지 용량/슬롯 수 |
| `ItemData.SkillBookData` (인스턴스, roll) | `PageCapacityBonus: int` | 책 생성 시 roll되는 용량 보너스. v1 정책 **+1~+5 균등 랜덤** |
| `ItemData.SkillBookData` (인스턴스, roll) | `PageSlotsBonus: int` | 책 생성 시 roll되는 슬롯 보너스. v1 정책 **50% 확률 +1, 50% +0** |
| 런타임 계산 | `UsedPageCapacity` | 현재 장착된 `Σ PageCost` |

**유효 페이지 용량** = `Table.PageCapacity + SkillBookData.PageCapacityBonus`.
**유효 페이지 슬롯** = `Table.PageSlots + SkillBookData.PageSlotsBonus`.

`PlayerSkillManager.GetPageCapacity(book)` / `GetPageSlots(book)` 한 진입점으로 합산값을 조회하므로, 검증·UI·툴팁 모두 자동으로 따라온다. roll은 `ItemManager.CreateSkillBook(itemId, skillId)` 한 곳에서 적용되며 모든 책 생성 경로(드랍/상점/치트)가 자동으로 거친다.

**왜 책 단위인가**:
- 캐릭터 단위 용량이면 강한 스킬에 모든 용량을 몰빵하는 단일 빌드로 수렴.
- 책 단위면 **장착한 책마다 독립 빌드 피스** → 10개 슬롯이 각자 다른 커스텀 폭을 가짐.
- "좋은 책을 찾으면 커스터마이즈 폭이 넓어진다"가 직관적인 그라인드 동기.

### 3.2 등급별 차등 (초안)

`SkillBookTable`이 등급별로 **페이지 용량 + 페이지 슬롯 개수**를 정의. 슬롯은 트리거 무관 단순 N개. 등급 차이가 **체감되도록 가파르게**.

| 등급 (ItemTable Tier) | Table PageCapacity | Table PageSlots | 인스턴스 roll 합산 | 비고 |
|---|---|---|---|---|
| Common (Tier 1) | **8** | 1 | 9~13 / 1~2 | 약한 페이지 1~2개 |
| Rare (Tier 2) | **24** | 3 | 25~29 / 3~4 | 중급 페이지 1~2개 또는 약한 페이지 다수 |
| Epic (Tier 3) | **60** | 5 | 61~65 / 5~6 | 강한 페이지 1~2개 + 보조 페이지 가능. 메타 빌드의 진입점 |

**비율 의도**: Common→Rare는 용량 3배·슬롯 3개, Rare→Epic은 용량 2.5배·슬롯 +2. 등급이 오르면 **단순 수치 증가가 아니라 빌드 카테고리 자체가 열림**(약한 페이지만 vs 메타 페이지 1개 + 보조).

**인스턴스 roll (v1에서 활성화)**: 모든 책 생성 시 `PageCapacityBonus = +1~+5` 균등 랜덤, `PageSlotsBonus = 50% +1` roll 적용. 같은 등급 책 간에도 미세한 가치 차별화가 살아있어 ARPG의 "더 좋은 인스턴스를 찾는다"는 그라인드 동기를 P-1에서부터 살림. 정책 차등(예: Epic만 더 큰 roll)이 필요하면 `ItemManager.RollSkillBookBonuses`에 Tier 인자 추가하는 것으로 확장.

### 3.2 페이지 비용

각 스킬 페이지(=`SkillEffectTable` 행)는 비용을 가진다. 강할수록 비싸다.

```
SkillEffectTable
├─ ... (기존 컬럼: EffectType, Trigger, Param1~3, Probability)
└─ PageCost: int                  // 신규 컬럼. 0이면 페이지 용량 모델 미사용 (시트 호환)
```

**비용 책정 가이드** (초안):

| 스킬 페이지 종류 | 효과 강도 | 비용 |
|---|---|---|
| 깡뎀 곱연산 (Damage +50%) | 매우 큼 | 30~40 |
| 흡혈 15% / 분열 / 관통 | 큼 | 15~25 |
| 점화/감전/중독 디버프 | 중 | 8~15 |
| 넉백 / 스턴 0.5초 / 둔화 | 작음 | 3~8 |
| 시각·정수 효과(범위 +10%) | 매우 작음 | 1~5 |

**원칙**: 용량 1당 기대 효율(증가 DPS / 생존성)이 스킬 페이지 종류와 무관하게 비슷해지도록 책정. 약한 페이지는 **싸기 때문에** 빈 자리에 채워 넣는 가치가 생김.

### 3.3 슬롯 모델

**슬롯은 무한이 아니다 — 그러나 페이지 용량이 슬롯보다 먼저 한도가 된다.** 슬롯 자체는 등급별 단순 N개(트리거 무관).

```
ItemData.SkillBookData
├─ SkillId: int
├─ SocketedPages: List<int>       // SkillEffectTable IDs (최대 effective slots, 동일 ID 중복 불가)
├─ PageCapacityBonus: int          // 인스턴스 roll 보너스 (+1~+5)
└─ PageSlotsBonus: int             // 인스턴스 roll 보너스 (0 또는 +1)
```

장착 시 검증 (이 책 단위로 닫힌 검증, `PlayerSkillManager.CanSocketSkillPage`):
1. `SocketedPages.Count < (Table.PageSlots + PageSlotsBonus)` 인가
2. `Σ(페이지 비용) ≤ (Table.PageCapacity + PageCapacityBonus)` 인가
3. `SocketedPages` 안에 같은 스킬 페이지 ID가 이미 없는가

**다른 책의 페이지 용량은 무관**. A 책에 깡뎀 페이지를 장착했어도 B 책의 페이지 용량과는 독립.

**중복 적용 금지**: 같은 스킬북 안에서는 동일한 스킬 페이지를 2개 이상 장착할 수 없다. 중복 기준은 v1에서 `SkillEffectTable.Id`로 본다. 인벤토리에 같은 페이지 아이템이 여러 개 있어도 한 책에는 한 번만 적용되며, 같은 페이지를 다른 책에 각각 장착하는 것은 허용한다.

**의도된 단순함**: 어떤 트리거의 스킬 페이지든 빈 슬롯에 장착 가능. 페이지 용량(=비용 예산)이 1차 제약, 슬롯 수가 2차 제약. 용량이 먼저 닿도록 비용 곡선을 책정 (§9.2).

---

## 4. (보류) 트리거 카테고리 슬롯 — 후속 페이즈

> **v1에서는 도입하지 않음.** §3의 단순 페이지 용량 모델만으로 시작.

스킬 페이지의 트리거(OnHit/OnCrit/OnKill 등)별로 슬롯을 분리해, 같은 트리거 안에서만 메타 경쟁이 일어나게 하는 확장. v1 출시 후 메타 양극화가 실제로 관측되면 도입을 검토한다. 도입 시:

- `SkillBookPageTable`에 카테고리별 슬롯 컬럼 추가 (HitSlots/CritSlots/...)
- 페이지 장착 시 카테고리 매칭 검증 1줄 추가
- 기존 `SocketedPages: List<int>`는 그대로 — 카테고리 정보는 스킬 페이지 자신(`SkillEffectTable.TriggerCategory`)이 들고 있음

후속 페이즈 도입 비용은 **데이터 컬럼 + 검증 코드 추가** 수준이라 마이그레이션 부담 작음.

---

## 5. (C) 조건부 스킬 페이지

### 5.1 모델

`SkillEffectTable`에 발동 조건 컬럼 추가. 조건을 만족할 때만 효과 적용.

```
SkillEffectTable
├─ ... (기존)
├─ PageCost: int
└─ Condition: GE.PageCondition    // 신규 enum (None/TargetStunned/HpBelowN/...)
└─ ConditionParam: float          // 조건 파라미터
```

### 5.2 PageCondition 카탈로그 (초안)

| 조건 | 의미 | ConditionParam |
|---|---|---|
| `None` | 항상 발동 (현재와 동일) | - |
| `TargetHpBelow` | 타겟 HP < N% | 임계값(%) |
| `TargetStunned` | 타겟이 스턴 상태 | - |
| `TargetIgnited` | 타겟에 점화 디버프 | - |
| `OwnerHpBelow` | 시전자 HP < N% | 임계값(%) |
| `WallNearby` | 타겟 주변 벽 N칸 이내 | 거리 |
| `KnockbackTarget` | 동일 스킬 명중에서 넉백 발생 | - |
| `IsBoss` | 타겟이 보스 | - |

조건 체크는 `SkillEffectExecutor.Trigger` 안 `Probability` 체크 직후로 추가. 미만족 시 효과 스킵, 비용은 그대로 발생(장착 비용은 페이지 용량에서 차감되어 있음).

### 5.3 디자인 효과 — 유틸 → 조건부 DPS

같은 페이지 카탈로그에 새 변종을 추가:

| 스킬 페이지 | EffectType | Condition | 가치 변화 |
|---|---|---|---|
| 넉백 페이지 (기본) | KnockbackOnHit | None | 유틸. 비용 5 |
| **벽쳐박기 페이지** | KnockbackOnHit + DamageBonus | WallNearby | **조건부 DPS**. 비용 12 |
| 스턴 페이지 (기본) | StunOnHit | None | 유틸. 비용 8 |
| **약점 노출 스킬 페이지** | DamageBonus | TargetStunned | **시너지형**. 비용 15 |

→ 넉백 페이지를 한 슬롯에 + 벽쳐박기 페이지를 다른 슬롯에 장착하면 **둘이 함께 빌드의 축**이 됨. 깡뎀 단일 스킬 페이지와 경쟁 가능.

---

## 6. 데이터 모델 요약

### 6.1 신규 / 수정 컬럼

```
SkillEffectTable (수정)
├─ Id, Name, EffectType, Trigger, Param1~3, Probability   (기존)
├─ PageCost: int                  // 페이지 비용
├─ Condition: GE.PageCondition    // 발동 조건 (데이터만 보유, 런타임 처리 보류)
└─ ConditionParam: float          // 조건 파라미터 (데이터만 보유, 런타임 처리 보류)

ItemData.SkillBookData (수정)
├─ SkillId: int                   (기존)
├─ SocketedPages: List<int>       // SkillEffectTable IDs, 동일 ID 중복 불가
├─ PageCapacityBonus: int         // 인스턴스 roll 보너스 (v1: +1~+5 균등 랜덤)
└─ PageSlotsBonus: int            // 인스턴스 roll 보너스 (v1: 50% 확률 +1)

ItemData.SkillPage (신규)
└─ SkillEffectId: int             // 페이지 아이템의 인스턴스 측 SkillEffectId

GlobalEnum (신규)
├─ ItemType.SkillPage = 101
├─ Category.SkillPage = 101
└─ PageCondition (P-2용 데이터)   // None/TargetStunned/...

SkillBookTable (신규, 등급별 룰)
├─ Id (=ItemTable.Tier와 매칭)
├─ PageCapacity: int              // 등급이 부여하는 페이지 용량 기본값
└─ PageSlots: int                 // 등급이 부여하는 슬롯 개수 기본값 (트리거 무관)

DropTable (수정)
└─ SkillPageRate: int             // 페이지 드랍 가중치 (등급별)

MerchantStockEntry (수정)
└─ SkillEffectId: int             // 매물이 SkillPage일 때 어떤 효과인지 고정
```

**StatComponent 변경 없음**: 페이지 용량은 캐릭터가 아닌 책이 갖는다. 캐릭터 레벨 곡선 / Stat modifier에 페이지 용량 노출 X.

**`TriggerCategory`/`PageCategory` 컬럼은 v1에서 도입 안 함** (§4 후속 페이즈로 보류).

**`Condition`/`ConditionParam`은 v1에서 데이터만 보유**, `SkillEffectExecutor`에 런타임 체크 추가는 P-2로 보류 (§5).

### 6.2 런타임 합성 로직

스킬 시전 시점에 본체 효과 + 페이지 효과를 합성하여 `SkillEffectExecutor.Trigger`에 전달:

```
effectiveEffectIds = SkillTable.SkillEffectIds ∪ ItemData.SkillBook.SocketedPages
SkillEffectExecutor.Trigger(trigger, ctx, effectiveEffectIds)
```

이 부분이 **가장 작은 코드 변경**으로 끝남. 디스패처는 이미 List 순회 구조라 합집합 한 번이면 됨. (구체 위치는 구현 페이즈에서 확정.)

---

## 7. UI / UX

### 7.1 스킬 페이지 인벤토리

스킬 페이지는 인벤토리 일반 아이템(스킬북과 동일 패턴). `ItemData.SkillPageId` 인스턴스 측 보관.

| ItemTable | 등급 |
|---|---|
| 5100 일반 스킬 페이지 | Common |
| 5101 마법 스킬 페이지 | Rare |
| 5102 전설 스킬 페이지 | Epic |

### 7.2 장착 화면

스킬북 슬롯을 우클릭 → **그 책 단위의** 페이지 장착 화면:
- 좌측: 책 등급/스킬 정보 + 단순 페이지 슬롯 N개 (등급에 따라 1/3/5)
- 중앙: 인벤토리의 페이지 목록 (드래그)
- 우측: **이 책의 페이지 용량 게이지** — `UsedPageCapacity / (PageCapacity + PageCapacityBonus)`

장착 시도 시:
- 빈 슬롯이 있는가 → 없으면 무효
- 이 책의 페이지 용량이 충분한가 → 부족 시 "페이지 용량 부족"
- 같은 스킬 페이지가 이미 장착되어 있는가 → 중복이면 "이미 장착된 페이지"

**다른 책의 페이지 용량은 별도 화면**. 책 간 페이지 용량 이전/공유 없음 — 그래야 책 등급 가치가 명확하게 살아남.

### 7.3 비용 대비 효율 표시 (양극화 디버깅용)

각 페이지 툴팁에 **`효과 강도 / PageCost`** 표기. 플레이어가 가성비 비교를 직관적으로 할 수 있게. 디자이너 입장에서도 비용 책정의 검증 도구.

---

## 8. 드랍/획득 루프

### 8.1 스킬 페이지 드랍 (구현됨)

`DropTable.SkillPageRate` 가중치로 페이지 드랍을 제어. 시트의 DropTable 행에 가중치를 채우면 몬스터 처치 시 페이지가 떨어진다.

코드 흐름:
- `DropHelper.CreateDropItem(...)` 가중치 합산에 `SkillPageRate` 포함
- 페이지 카테고리 당첨 시 `DropHelper.CreateSkillPageDropAsync(tier, position)` → `ItemManager.CreateRandomSkillPageOfTier(tier)` → `CreateSkillPage(...)` 경로로 항상 `SkillPageData` 채워서 드랍

PageCost 범위 매핑(현 단순 cutoff):

| 등급 | 드랍 등장 | PageCost 범위 |
|---|---|---|
| Common | 초반 던전 | 1~10 |
| Rare | 중반 | ~25 |
| Epic | 후반/보스 | 26~ |

### 8.2 상점 (구현됨)

`MerchantStockEntry.SkillEffectId` 신규. 매물이 SkillPage일 때 어떤 효과인지 고정해 같은 stock entry는 항상 같은 페이지를 판다.

코드 흐름:
- `VillageManager.RollMerchantStock` — `ItemType==SkillPage` 행을 골라 `PickRandomSkillEffectIdByTier(tier)`로 SkillEffect 픽 → `MerchantStockEntry.SkillEffectId` 저장
- `VillageManager.BuyItemFromMerchant` — SkillPage 분기에서 `ItemManager.CreateSkillPage(itemId, skillEffectId)` 호출
- `UIShopMerchant` — 매물 표시명 `"{페이지명} — {효과명}"`, 호버 시 글로벌 툴팁 표시 (임시 ItemData 빌드해 `BuildSkillPageContentFromItem`로 표시)

상점 풀 자격 조건은 SkillBook과 동일: `ItemTable.Tier ≤ village.Stage + 1` AND `BasePrice > 0`.

### 8.3 페이지 식별/봉인 (선택)

PoE 형태로 "미식별 페이지"를 두고 식별 시 무작위 효과/비용 결정 → 수집 욕구 자극. 1차 페이즈에선 도입 X, 후속 시즌 이벤트로 보류.

---

## 9. 밸런싱 고려사항

### 9.1 비용 곡선이 양극화 해소의 핵심

비용 곡선이 잘못되면 (A) 모델은 무력화된다.

- **너무 평탄하면**: 깡뎀 페이지를 8개 장착하는 게 항상 정답 → 슬롯만 풀려난 PoE
- **너무 가파르면**: 약한 페이지 1~2개만 장착하는 게 정답 → 빌드 다양성 소멸

**권장 절차**:
1. 스킬 페이지 종별 평균 효과를 정량화 (DPS 환산)
2. `PageCost = round(EffectScore × k)` 로 1차 책정
3. 플레이테스트로 카테고리별 메타 페이지 추적, OP 페이지는 비용 ↑

### 9.2 페이지 용량 vs 슬롯 — 어느 쪽이 한도가 먼저 닿는가

설계 의도: **페이지 용량이 슬롯보다 먼저 한도**. 슬롯은 단순 상한이고, **양적 제약은 페이지 용량**이 만든다.

**역전 시 위험**: 슬롯이 더 빨리 차면 페이지 용량 시스템 자체가 사실상 무의미해짐 → 등급별 검증:
- Common: 용량 8 vs 슬롯 1 → 용량이 한도. ✓ (의도대로)
- Rare: 용량 24 vs 슬롯 3 → 평균 페이지 비용 8 이상이면 용량이 먼저 닿음
- Epic: 용량 60 vs 슬롯 5 → 평균 12 이상이면 용량이 먼저 닿음

**비용 곡선 책정 시 기준**: 스킬 페이지 평균 비용이 **페이지 용량 / 슬롯 수 × 1.2** 이상이 되도록.

### 9.3 조건부 스킬 페이지는 보스전 vs 잡몹전 균형

`Condition`이 보스에서만 활성(예: `IsBoss`)이거나 잡몹에서만 활성(`TargetHpBelow 30%`처럼 빠른 정리에서 발동 안 함)이면 둘 중 한쪽 컨텐츠에서 스킬 페이지가 죽는다.

→ 카탈로그를 **잡몹용 / 보스용 / 양쪽** 균등 배분.

---

## 10. 구현 페이즈 분할

각 페이즈는 독립 출시 가능. 페이즈마다 단독으로도 시스템이 굴러가도록 설계.

### Phase P-1: 페이지 용량 + 비용 + 단순 페이지 슬롯 (v1 본체) ✅ 완료
- `SkillBookTable` 신규 (Id, PageCapacity, PageSlots) — 등급별 룰
- `SkillEffectTable.PageCost`/`Condition`/`ConditionParam` 컬럼 추가 (Condition은 데이터만)
- `ItemData.SkillBookData.SocketedPages` + `PageCapacityBonus` + `PageSlotsBonus` 추가
- `ItemData.SkillPage` 신규 (페이지 아이템 인스턴스)
- 장착 검증: 슬롯 수, 페이지 용량, 동일 페이지 중복 여부 (`PlayerSkillManager.CanSocketSkillPage`)
- 시전 시 본체 효과 + 페이지 효과 합집합 합성 (`EntityFactory.BuildEffectiveSkillEffectIds`)
- UI: 책 우클릭 → 페이지 장착 모달, 페이지 슬롯 N개 + 용량 게이지 (`UISkillBook` 동적 모달)
- 페이지 / 스킬북 툴팁 분리 빌더 (`TooltipManager.BuildSkillPageContentFromItem` / `FromEffect`, `Show(int)` 오버로드)
- 글로벌 툴팁 anchor 정책: 슬롯 우측 + 4방향 화면 경계 회피 (Rect anchor, 마우스 추적 X)
- 안전장치: `ItemManager.CreateInventoryItemData` 공통 팩토리로 SkillBook/SkillPage 인스턴스 데이터 누락 방지

### 인스턴스 roll 활성화 ✅ 완료 (P-1에 통합)
- `PageCapacityBonus`: +1~+5 균등 랜덤 (모든 책 생성 시 적용)
- `PageSlotsBonus`: 50% 확률 +1
- 적용 위치: `ItemManager.CreateSkillBook(itemId, skillId)` 한 진입점

### Phase P-2: 조건부 효과 (보류)
- 데이터 컬럼 (`Condition`, `ConditionParam`)은 P-1에서 이미 추가됨
- 미구현: `SkillEffectExecutor.Trigger`에서 `Probability` 체크 직후 조건 체크 추가
- 미구현: 조건부 스킬 페이지 카탈로그 5~10종 시트 추가

### Phase P-3: 드랍/상점/UI 폴리시 ✅ 일부 완료
- ✅ `DropTable.SkillPageRate` 컬럼 + `DropHelper.CreateSkillPageDropAsync`
- ✅ `MerchantStockEntry.SkillEffectId` + `VillageManager` 매물 roll/구매
- ✅ `UIShopMerchant` 매물 호버 툴팁 (글로벌 툴팁 사용)
- 미구현: 드래그앤드롭 페이지 장착 UI (현재는 클릭만)
- 미구현: 효율 표기, 미리보기 디자인 폴리시

### Phase P-4 (조건부 도입): 트리거 카테고리 슬롯
- v1~v3 운영 후 메타 양극화가 실제 관측되면 도입
- `TriggerCategory` 컬럼 + `SkillBookTable`에 카테고리별 슬롯 컬럼 추가
- 페이지 장착 검증에 카테고리 매칭 1줄
- **§4 보류 항목 회수**.

---

## 11. 다른 시스템과의 관계

| 시스템 | 관계 |
|---|---|
| **SKILL_DESIGN §6.8 SkillEffect** | 스킬 페이지 = `SkillEffectTable`의 인스턴스. 본 시스템은 §6.8의 사용처를 확장 |
| **SKILL_DESIGN §6.9 토템·지뢰** | `OnSkillCommand` 카테고리 슬롯에 위임 페이지 장착 가능 |
| **SKILLBOOK_DESIGN** | 스킬 페이지는 스킬북 인스턴스의 페이지 슬롯에 장착. 책=스킬, 스킬 페이지=커스터마이즈 |
| **장비 스탯 시스템** | 장비는 페이지 용량에 직접 영향 X. 페이지 용량은 책 단위로만 결정. v1부터 인스턴스 roll(`PageCapacityBonus`/`PageSlotsBonus`)이 활성화되어 같은 등급 책 간 가치 차별화가 살아있음. |
| **버프 시스템** | `ApplyBuffOnHit` 스킬 페이지는 기존 `BuffHelper.AddBuff` 그대로 사용 |

---

## 12. 열린 질문

설계 합의 후 결정 필요:

1. **페이지 교체 비용**: 스킬 페이지 교체 시 무료인가, 화폐 소모인가? (PoE는 자유, D2식 영구 장착은 비가역) — **현재 무료**
2. **등급 차등의 가파름**: §3.2 용량(8/24/60) + 슬롯(1/3/5) 초안. 차이를 더 가파르게 할지(체감 ↑) 평탄하게 할지(저급 책 가치 ↑)?
3. **조건부 스킬 페이지의 비용 가중치**: 조건이 까다로울수록 비용 ↓ 인가 ↑ 인가? (P-2 보류)
4. **유니크 페이지 도입 여부**: "Build-Defining 스킬 페이지"를 별도 등급으로 둘 것인가, Epic 스킬 페이지에 포함시킬 것인가?
5. ~~**인스턴스 roll 도입 시점**~~ — **결정됨**. v1에서 도입. `PageCapacityBonus = +1~+5 균등 랜덤`, `PageSlotsBonus = 50% +1` 정책으로 모든 책 생성 시 자동 roll.
6. **AI 적용 범위**: 몬스터/NPC도 스킬 페이지를 장착하는가? — **v1 미적용**. `EntityFactory.GetPlayerSkillBookForSlot`이 player entity일 때만 책 조회.
7. **트리거 카테고리(§4) 도입 트리거 조건**: 무엇을 보고 도입을 결정할지? (특정 스킬 페이지 사용률, 메타 빌드의 다양성 지수 등)
8. **`GetSkillPageTierByCost` 매핑 정책**: 현재 단방향 cutoff(`≤10/≤25/>`). 등급별 PageCost 범위 겹침을 허용할지, 또는 `SkillEffectTable`에 `Tier` 컬럼 직접 추가할지 결정 필요.
9. **기존 책 마이그레이션**: 인스턴스 roll 활성화 이전에 만들어진 책들은 보너스 0. 기존 책에 일괄 roll을 적용할지, 그대로 둘지 결정 필요.

---

## 13. 한 줄 요약

> **스킬 페이지는 SkillEffect의 인스턴스화 + (스킬북 단위 페이지 용량 / 조건부 발동) 두 축으로 양극화를 해소한다.**

핵심은 **책 단위 페이지 용량**: 캐릭터 단위 용량이 아니라 책 등급(테이블) + 인스턴스 roll(`+1~+5` / `50% +1`)이 용량·슬롯을 결정 → "좋은 책을 찾으면 그 스킬의 커스텀 폭이 넓어진다"는 ARPG 그라인드 동기를 v1부터 살림. 조건부 스킬 페이지는 유틸 효과를 조건부 DPS로 변환해 메타 후보로 끌어올린다(P-2 보류). 트리거 카테고리 슬롯(§4)은 후속 페이즈로 보류해 v1 복잡도를 낮춘다.
