# 마을 경제와 물리 NPC의 경계

> **상태**: 현행 정본
>
> 관련 문서: [VILLAGE_RUNTIME_DESIGN.md](../village/VILLAGE_RUNTIME_DESIGN.md) · [NPC_SIMULATION_DESIGN.md](NPC_SIMULATION_DESIGN.md) · [ABSTRACT_CONSTRUCTION_DESIGN.md](../village/ABSTRACT_CONSTRUCTION_DESIGN.md)
>
> **책임**: 마을의 경제 인구와 물리 NPC가 무엇을 소유하고 서로 어디까지 영향을 주는지 정의한다.

---

## 1. 핵심 결정

마을 경제와 물리 NPC는 서로 다른 모델이다.

- **경제 인구**: `VillageData.Population`. 생산, 식량 목표, Tier, 건설 후보를 구동한다.
- **물리 NPC**: 월드를 이동하고 전투·활동·관계·장비를 가지는 모험가다.

물리 NPC 수, 생존 여부, 현재 위치, 직업은 마을 경제 인구를 변경하지 않는다.

이 분리를 선택한 이유:

1. 모험가가 원정을 떠나거나 죽었다는 이유로 마을 생산과 Tier가 즉시 붕괴하는 결합을 막는다.
2. 청크가 꺼져 물리 NPC가 없어도 원거리 마을 경제를 같은 규칙으로 진행할 수 있다.
3. NPC 활동의 재미와 마을 성장 밸런스를 독립적으로 조정할 수 있다.
4. 작업자 배정·이동·AI 상태가 건설 완료의 필수 조건이 되는 복잡성을 제거한다.

---

## 2. 경제 인구의 소유와 동기화

원본 인구는 `HistoryManager`가 소유한 `SettlementLiveState.Pop`이다. `VillageManager.QuantizeAbstractPopulation`이 이를 경제 범위로 변환해 `VillageData.Population`에 기록한다.

정량화는 raw 정착지 인구를 그대로 생산식에 넣어 자원이 폭증하는 것을 막고, 기존 Tier·식량·건설 수치를 제한된 범위에서 유지한다. 현재 공통 상수는 다음과 같다.

- `ABSTRACT_POP_DIVISOR = 50`
- `ABSTRACT_POP_CAP = 25`

`System_SettlementSim`이 Priority 50에서 인구를 동기화한다. 이후 생산 52, Tier 60, 건설 66이 같은 tick에서 최신 경제 인구를 읽는다. materialize 직전에도 해당 정착지 인구를 먼저 반영한 뒤 건설 catch-up을 수행한다.

`NpcEntityIds`는 물리 NPC 명단일 뿐 `VillageData.Population`의 계산 근거가 아니다.

---

## 3. 자원 생산

`VillageResourceSimulation`은 `VillageData.Population`을 입력으로 Food, Wood, Stone, Iron, Gold를 생산한다. 개별 NPC의 `JobType`, Working 상태, 배치 오브젝트 사용 여부는 읽지 않는다.

경제 인구 1~25를 생산 인구 1~2 범위로 압축하는 이유는 큰 마을이 선형으로 자원을 폭증시키지 않으면서도 성장에 따른 이득을 남기기 위해서다.

carry는 `VillageData`가 소유한다. 실체화 경로의 `VillageStorageComponent`는 수량·Cap·기아 상태를 보여주는 미러이며 독립 생산 원장을 만들지 않는다.

`JobBonusTable`이 데이터 로더에 남아 있더라도 현행 마을 생산에는 사용하지 않는다. 새 경제 기능이 이 테이블을 다시 사용하려면 별도 설계 결정이 필요하다.

---

## 4. 자가 건설

건물과 벽은 NPC를 배정하지 않고 게임시간 경과로 완성한다. Wood와 Stone 비용은 마을 성장 속도를 제한하는 경제 게이트이므로 유지한다.

이 결정으로 제거되는 결합:

- 건설 작업자 탐색과 1:1 배정
- 작업 지점까지의 물리 이동 성공 여부
- NPC AI의 Build 상태
- NPC 사망·이탈 시 task 재배정

물리 NPC가 한 명도 화면에 없어도 경제 인구가 있고 정착지가 운영 가능하면 실시간·추상 건설은 진행할 수 있다.

---

## 5. 서비스

Shop, Forge, Inn, Shrine은 NPC 직업이 아니라 배치 건물의 `ProvidedService`가 제공한다. `System_VillageServiceProximity`가 플레이어 주변 서비스를 집계하고 `ServiceUIRouter`가 UI를 연다.

- 상인 NPC가 없어도 MerchantStall이 있으면 Shop을 사용할 수 있다.
- 대장장이 NPC가 없어도 Forge 세트가 있으면 Forge를 사용할 수 있다.
- Inn은 방문자 고용 시설이 아니라 건물 기반 휴식·세이브 서비스다.

서비스를 건물에 귀속하는 이유는 물리 NPC의 현재 위치와 상관없이 마을 기능을 안정적으로 제공하고, 오브젝트 건설·파괴가 서비스 상태에 직접 반영되게 하기 위해서다.

---

## 6. 물리 NPC의 책임

물리 NPC는 모험가로서 다음 상태를 가진다.

- 활동·원정·이동
- 전투와 생존
- 관계와 기억
- 장비와 성향
- 대화와 퀘스트

현재 NPC 총량 보충과 오프스크린 활동은 [NPC_SIMULATION_DESIGN.md](NPC_SIMULATION_DESIGN.md)가 소유한다. 그 정책은 모험가 수를 다루며 경제 인구를 재계산하지 않는다.

`JobType`은 설명·장비 시드 같은 모험가 특성으로 사용할 수 있지만, 마을 작업자나 생산 보너스를 뜻하지 않는다.

---

## 7. Fate와 운영 가능 상태

`Alive`와 `Conquered` 정착지는 경제·건설을 진행할 수 있다. `Ruined`와 `Merged`는 정상 마을로 materialize하지 않으며 추상 advance는 clock만 현재 시각으로 정렬한다.

런타임 폐허 전환은 인구 0만 기록해서 끝내지 않는다. Fate, 진행 task, 건설 중 Entity, materialize 차단과 추상 clock을 한 진입점에서 함께 변경해야 한다. 이 원자적 전환은 [ABSTRACT_CONSTRUCTION_DESIGN.md](../village/ABSTRACT_CONSTRUCTION_DESIGN.md)의 후속 계약을 따른다.

---

## 8. 금지되는 재결합

- `NpcEntityIds.Count`로 `VillageData.Population`을 덮어쓰지 않는다.
- NPC 직업이나 Working 상태를 마을 자원 생산식에 넣지 않는다.
- 건설 task에 NPC EntityId를 배정하지 않는다.
- 모험가 사망을 직접 마을 폐허 전환으로 사용하지 않는다.
- 서비스 이용 가능 여부를 특정 NPC의 존재로 판정하지 않는다.

이 경계를 바꾸는 기능은 NPC와 마을 양쪽의 LOD·세이브·밸런스에 영향을 주므로 별도 설계 결정으로 다룬다.
