# 스킬북 시스템 — 설계 문서

> 선행: [SKILL_DESIGN.md](SKILL_DESIGN.md) · [PHASE_D_DESIGN.md](PHASE_D_DESIGN.md) (상점 거래 컬럼)
>
> **상태**: 코드 구현 완료 ✅. 시트 데이터 입력은 사용자 측 작업.
>
> 이 문서는 **"왜 이렇게 정했나"** + **"어디서 시작해 들어가야 하나"** 만 담는다. 어떻게 구현되어 있는지는 코드와 git이 진실의 출처.

---

## 1. 범위

**다루는 것**
- 플레이어 스킬을 하드코딩이 아닌 **스킬북 아이템 장착**으로 결정
- 스킬북은 인벤토리 일반 아이템처럼 드랍·매매·저장 가능
- 신규 스킬 UI에서 슬롯에 책 장착 → 그 슬롯의 ECS 스킬 엔티티가 책의 SkillId로 갱신

**다루지 않는 것**
- 스킬북 강화/Mod (책에는 단일 SkillId만 — 무기와 다름)
- 학습형(영구 해금) 모드 — 본 설계는 **장착식**만
- AI 측 스킬북 — 몬스터/NPC는 기존 `AiTable.SkillId1/2/3` 유지 (§2.7 참고)
- 키바인딩 UI — 슬롯↔키 매핑은 정적

> **한 줄**: "플레이어 스킬은 인벤토리에 있는 스킬북을 스킬 UI 슬롯에 장착해서 결정한다."

---

## 2. 핵심 설계 결정

### 2.1 스킬북 슬롯은 장비 슬롯과 분리
별도 배열 `_skillBookSlots: ItemData?[10]` 사용. 장비(`_inventoryEquip`)는 신체 부위 + 스탯 mod 흐름이지만, 스킬북은 입력 키 인덱스 + ECS 스킬 엔티티 갱신이라 의미가 다르다. 룰·UI·저장 모두 분리하는 편이 단순.

### 2.2 SkillId는 ItemData 인스턴스에 (ItemTable은 등급만)
ItemTable에는 등급별 책 3행만 (Common/Rare/Epic). **어떤 스킬이 들어 있는가는 ItemData 인스턴스의 `SkillBookData.SkillId`**가 결정.

```
ItemTable (3행):  5000 낡은 책  /  5001 고급 책  /  5002 전설 책
ItemData 인스턴스 예: { Id=5000, SkillBook={SkillId=1 Strike} }
                    { Id=5000, SkillBook={SkillId=5 QuickHop} }
```

**왜**: 새 스킬 추가 = `SkillTable` 1행 + 풀 등록만. ItemTable은 무변경. `EquipmentData`(Mod 인스턴스)와 같은 패턴이라 일관.

**스택은 false** — 같은 ItemId라도 SkillId가 다르면 다른 책. 슬롯 장착·매매 단위는 1권으로 통일.

### 2.3 슬롯 개수 = 10, 키 매핑
| 슬롯 | 입력 |
|---|---|
| 0 | 좌클릭 |
| 1 | Space |
| 2~9 | 숫자키 1~8 |

10개로 시작하는 이유: "곧 모자라" 압박 회피, 키보드 1~9·0 한 줄에 자연 매핑(현재는 1~8까지만 사용). AI는 3슬롯이라 플레이어 우위는 자연스럽게 확보.

### 2.4 빈 슬롯 폴백
**옵션 C 채택** — 신규 플레이어 시 슬롯 0에 기본 책(Common 5000 + Strike SkillId=1)을 자동 시드. V1→V2 마이그레이션도 동일. 첫 플레이에서 "공격이 안 됨"을 만나지 않도록.

다른 슬롯이 비면 그 키는 무동작.

### 2.5 같은 SkillId 다중 슬롯 — 허용
유저의 빌드 선택. 슬롯별 쿨타임이 독립이라 같은 스킬을 두 키로 번갈아 사용하면 사실상 쿨이 절반이 되지만, 어뷰징 우려보다 빌드 자유도가 우선. 실제 밸런스 문제로 드러나면 그때 `SkillTable.GlobalCooldown` 같은 메커닉을 별도 추가.

`PlayerSkillManager.EquipSkillBook`는 SkillId 중복 검증을 하지 않는다.

### 2.6 스킬 엔티티 라이프사이클
슬롯에 대응하는 스킬 엔티티는 `EntityIdHelper.GetDeterministicId(ownerId, Skill, slotIndex)`로 결정적 ID. 책 교체 시 `EntityFactory.RemoveSkill → CreateSkill` 순서로 갱신. 발동 중(`SkillStateComponent.IsRunning`) 슬롯은 UI에서 해제 차단.

### 2.7 AI는 스킬북 안 씀
몬스터/NPC는 기존 `AiTable.SkillId1/2/3` 경로 유지 (`AddSkillsFromAiTable`). 스킬북은 **플레이어 빌드 다양성·경제 시스템 도구**, AiTable은 **디자이너가 만든 캐릭터 프리셋** — 두 개념을 같은 시스템에 묶으면 둘 다 어색해진다.

발동 자체(SkillCommandComponent → System_Skill 흐름)는 플레이어/AI 동일.

### 2.8 ItemTable.Tier == SkillTable.Tier (같은 등급 체계)
별도 `BookTier`를 두지 않고 같은 `Tier` 컬럼 의미를 공유. 룰 한 줄: **`ItemTable.Tier == SkillTable.Tier` 인 책에 그 스킬이 들어간다**. 드랍/상점 풀 빌드도 이 룰로 작동.

---

## 3. 데이터 모델 요점

### 신규 컬럼
- `SkillTable.Tier` (D열, ItemTable.Tier와 같은 의미). 0이면 책 풀에서 제외
- `DropTable.SkillBookRate` (J열). 0이면 드랍 없음

### 신규 인스턴스 데이터
- `ItemData.SkillBook : SkillBookData?` — `{ SkillId; [NonSerialized] SkillTable Table }`. `OnLoadCompleted`에서 SkillTable 재바인딩

### PlayerData V1→V2
- `_skillBookSlots: ItemData?[10]` 추가
- 마이그레이션: 배열 초기화 + 슬롯 0에 기본 책 시드

### 시트 작업 (사용자 측)
| 시트 | 작업 |
|---|---|
| `ItemTable` | 등급별 책 3행 (5000/5001/5002) |
| `SkillTable` | D열 `Tier` 헤더 + 25행 분배 |
| `DropTable` | J열 `SkillBookRate` 헤더 + 가중치 입력 |

---

## 4. 통합 흐름

```
플레이어 생성 (EntityFactory.CreatePlayer)
   PlayerData._skillBookSlots 순회
   슬롯 i에 책 있으면 CreateSkill(playerId, i, book.SkillBook.SkillId)

장착/해제 (PlayerSkillManager)
   인벤 ↔ 슬롯 스왑
   RemoveSkill → CreateSkill
   SkillBookChangedMessage 발행 → UI 갱신
   AR.s.Data.Save()

발동 (System_Input)
   좌클릭 / Space / 숫자키 1~8 → UseSkill(slotIndex)
   SkillCommandComponent → System_Skill 일반 흐름

드랍 (DropHelper.ProcessDrop)
   4-way 가중치(Nothing/Currency/Equipment/SkillBook)
   책 카테고리 픽 → CreateRandomSkillBookOfTier(DropTable.Tier)
   ItemManager.CreateItemFromData → 월드 인스턴스화

상점 (VillageManager)
   RollMerchantStock에서 책 매물 픽 시 같은 Tier SkillId 함께 픽
   MerchantStockEntry { ItemTableId, SkillId, RemainingCount } 저장
   BuyItemFromMerchant → CreateSkillBook(itemId, entry.SkillId)
```

---

## 5. UI 동작 모델

좌측 인벤토리 그리드(스킬북만 필터) + 우측 5×2 슬롯 그리드.

- **인벤 책 클릭** → 첫 빈 슬롯에 자동 장착
- **장착 슬롯 클릭** → 인벤 빈 슬롯으로 해제
- 표시: 슬롯=책 표지 아이콘 + 키 라벨(LMB/SPACE/1~8) + 스킬명. 호버 툴팁=`SkillTable.Name + Description`
- 인벤 책=책 표지 + Tier 라벨(T1/T2/T3)
- 같은 ItemId 여러 권이 SkillId 다르면 각각 별개 슬롯에 표시 (Stackable=false)

UXML/USS 자체가 진실의 출처 — 자세한 구조는 [SkillBook.uxml](../Assets/UI/SkillBook/SkillBook.uxml) 참고.

---

## 6. 진입점 (코드 어디서부터 들어가나)

| 책임 | 진입점 |
|---|---|
| 장착/해제 API | [`AR.s.PlayerSkill`](../Assets/Scripts/Manager/PlayerSkillManager.cs) (PlayerSkillManager) |
| 책 ItemData 생성 | [`AR.s.Item`](../Assets/Scripts/Manager/ItemManager.cs) — `CreateSkillBook`, `CreateSkillBookForSkill`, `CreateRandomSkillBookOfTier` |
| ECS 스킬 엔티티 | [`EntityFactory.CreateSkill / RemoveSkill`](../Assets/Scripts/Factory/EntityFactory.cs) |
| 입력 매핑 | [`System_Input`](../Assets/Scripts/Common/System/System_Input.cs) |
| UI | [`UISkillBook`](../Assets/Scripts/UI/UISkillBook.cs) + [SkillBook.uxml](../Assets/UI/SkillBook/SkillBook.uxml) |
| 드랍 분기 | [`DropHelper.ProcessDrop`](../Assets/Scripts/Common/Utility/DropHelper.cs) |
| 상점 분기 | [`VillageManager.RollMerchantStock / BuyItemFromMerchant`](../Assets/Scripts/Village/VillageManager.cs) |
| 치트 (테스트 경로) | [`UICheat`](../Assets/Scripts/UI/UICheat.cs) — "스킬 ID" 칸 |

다른 데이터 모델 변경(`SkillTable.Tier`, `DropTable.SkillBookRate`, `ItemData.SkillBook`, `MerchantStockEntry.SkillId`, `PlayerData._skillBookSlots`, `SkillBookChangedMessage` 등)은 위 진입점에서 grep으로 추적 가능.

---

## 7. 위험 요소 / 결정 메모

### 7.1 발동 중 슬롯 해제
`SkillStateComponent.IsRunning` 시 해제는 UI 측에서 차단. 슬롯이 사라지면 진행 중 스킬도 즉시 취소.

### 7.2 좌클릭이 슬롯 0에 묶여 있음
슬롯 0이 비면 좌클릭은 무동작. 신규 플레이어는 §2.4 시드로 진입 보장. 플레이어가 슬롯 0 책을 의도적으로 빼는 건 허용 (자유).

### 7.3 1슬롯 = 1책 (SkillId만 저장 안 함)
저장 효율로는 SkillId int만 두는 편이 가벼우나, 책을 인벤↔슬롯으로 옮기는 의미상 일관성 + 추후 책에 변동 데이터(강화 단계 등) 생길 가능성을 위해 `ItemData?[]` 사용.

### 7.4 SkillTable.Tier 미입력 / 책 ItemTable 미입력
시트 작업이 안 되어 있으면 드랍/상점 풀 비어 책이 안 나옴. 콘솔 경고로 안내.

---

## 8. 인터페이스 요약

```csharp
// AR.s.PlayerSkill
class PlayerSkillManager
{
    bool EquipSkillBook(int slotIndex, int inventorySlotIndex);
    bool UnequipSkillBook(int slotIndex);
    ItemData? GetEquippedBook(int slotIndex);
    int GetEquippedSkillId(int slotIndex);   // 0 if empty
}

// AR.s.Item (스킬북 헬퍼)
ItemData? CreateSkillBook(int itemId, int skillId);
ItemData? CreateSkillBookForSkill(int skillId);          // SkillId만 → Tier 자동 룩업
ItemData? CreateRandomSkillBookOfTier(int tier);          // Tier만 → 책+스킬 모두 랜덤
Task<bool> CreateItemFromData(ItemData data, Vector3 position);

// EntityFactory
static void RemoveSkill(int ownerEntityId, int slotIndex);

// Broadcast 메시지
struct SkillBookChangedMessage { int SlotIndex; int NewSkillId; }   // 해제 시 NewSkillId=0
```

---

## 9. 향후 확장 (스코프 외)

- **스킬북 강화** — 책에 Mod 인스턴스 추가
- **학습형(영구 해금)** — `LearnedSkillIds` + 슬롯엔 SkillId만 매핑
- **글로벌 쿨다운** — 다중 슬롯 어뷰징이 실제 문제로 드러나면 (§2.5)
- **Explicit Drop 풀** — 보스용 특정 SkillId 드랍이 필요하면 `DropSkillBookTable`
- **AI용 스킬북** — NPC 인벤토리 + NpcSkillManager. 큰 재설계 필요 (§2.7)
- **키바인딩 UI** — raw `Keyboard.current[Key.DigitN]` → `ArpgInput` 액션으로 이전
