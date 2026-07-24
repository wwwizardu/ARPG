# 몬스터 스폰 디자인 (Monster Spawn Design)

## 개요

현재 몬스터 스폰은 `MonsterManager` 인스펙터에 직접 박은 `_mainMonsterTableId` / `_subMonsterTableId` 두 개의 ID로 모든 청크의 몬스터 종류가 결정된다. 종류가 늘어나도 청크 하나에 나올 수 있는 종은 항상 2종이며, Zone(난이도 구간)별로 다른 몬스터를 배치하려면 매번 코드/씬을 수정해야 한다.

본 문서는 **데이터 주도 스폰 풀(Spawn Pool) 시스템**으로 전환하는 설계 기준을 정의한다. 단계적 도입(Phase 1~4)을 전제로, 우선 **테이블에서 랜덤으로 뽑는 가장 단순한 형태(Phase 1)** 부터 시작해 **Zone × 레벨 × 등급(Archetype) 매핑**으로 확장한다.

본 문서는 **설계 기준**이며 실제 코드/테이블 마이그레이션은 별도 작업 항목으로 진행한다.

> **구현 현황 (2026-07-24)** — 본 문서의 Phase 1~3(`SpawnPool*` 테이블·가중치·엘리트/보스)은 아직 미구현이다.
> 대신 **바이옴 기반 경량 스폰 풀**이 먼저 구현되었다(상세: §7.2). 별도 테이블 없이 `MonsterTable.Biome`을
> 풀 키로 직접 쓴다 — `BiomeType.Any`=공용(모든 biome 풀에 포함), 특정 biome=그 biome 전용.
> 청크 biome의 풀 = **공용 ∪ 해당 biome 전용**에서 **그룹마다 균등 샘플링**하며, 풀이 비면 MonsterManager 인스펙터 fallback을 쓴다.
> 가중치·MinZone/MaxZone·Archetype 승격·보스 청크는 여전히 미구현(향후 이 문서의 Phase 2~3로 확장).

---

## 1. 현재 시스템과 한계

### 1.1 현재 구조 요약

| 영역 | 현재 처리 | 위치 |
|---|---|---|
| 몬스터 종 결정 | ~~인스펙터 고정 ID 2개~~ → **바이옴 경량 풀**(§7.2.1): `MonsterTable.Biome` 기반 `공용(Any)∪biome전용` 그룹별 샘플. 풀 비면 인스펙터 fallback | [BiomeMonsterMapping.cs](../../Assets/Scripts/Map/Biome/BiomeMonsterMapping.cs), [MonsterManager.cs](../../Assets/Scripts/Manager/MonsterManager.cs) `_mainMonsterTableId`/`_subMonsterTableId`(fallback) |
| 스폰 밀도 | Zone 기반 (ZoneTable) | [Tables.cs](../Assets/Scripts/Common/Tables.cs) `ZoneTable` |
| 레벨 | Zone에서 결정 → MonsterTable.Level override | [EntityFactory.cs](../Assets/Scripts/Factory/EntityFactory.cs) `CreateMonster(level)` |
| 위치 | 그룹 거부 샘플링 (anchors + radius) | [MonsterManager.cs](../Assets/Scripts/Manager/MonsterManager.cs) `SpawnInitialMonstersInChunk` |
| 리스폰 | 60초 주기, 죽은 수만큼 같은 종 재생성 | [MonsterManager.cs](../Assets/Scripts/Manager/MonsterManager.cs) `RespawnMonsterInChunk` |
| 드랍 | MonsterTable.DropId → DropTable | [DropHelper.cs](../Assets/Scripts/Manager/DropHelper.cs) |

### 1.2 한계점

1. **종 다양성 결여**: 청크당 최대 2종. 새 몬스터를 만들어도 어떤 청크에 등장할지 결정할 채널이 없음.
2. **Zone과 종류의 분리 부재**: Zone은 밀도/레벨만 제어하고, 종류는 전역 인스펙터 값을 그대로 씀 → 깊은 청크에 들어가도 같은 몬스터.
3. **가중치 없음**: 일반/희귀 종이 동일 확률로 등장.
4. **등급(Archetype) 제어 없음**: MonsterTable.Archetype은 종 단위로 고정. 같은 종에서 Magic/Rare 변종을 만들 수 없음.
5. **보스 / 엘리트 스폰 채널 없음**.
6. **리스폰이 원래 종만 재사용**: 청크가 점진 변화하는 느낌을 못 줌.

> **경량 풀(§7.2.1)로 해소됨**: #1(종 다양성 — biome 풀로 다종), #2(Zone과 종류 분리 — 종류가 이제 청크 biome별로 결정), #6(리스폰이 풀에서 재샘플 → 다른 종 가능).
> **여전히 미해소**: #3(가중치), #4(Archetype 승격), #5(보스/엘리트) — Phase 2~3.

## 2. 핵심 설계 원칙

1. **데이터 주도**: 풀(Pool)과 가중치는 모두 테이블로. 새 몬스터 추가 = 테이블 행 하나.
2. **단계적 도입**: Phase 1은 인스펙터 List<int>만으로도 작동. Phase 2부터 테이블 도입.
3. **Zone과 풀의 직교**: Zone = 밀도 + 레벨 + 어떤 풀을 쓸지. 풀 = 어떤 종이 어떤 비율로 나올지.
4. **그룹 단위 일관성**: 한 그룹 내 몬스터는 같은 종 (시각적 일체감, 현재 동작과 호환). 다종 혼합은 옵션.
5. **드랍/AI 시스템과 무관**: 풀은 MonsterTableId만 반환. 이후 `EntityFactory.CreateMonster()` 흐름은 그대로.
6. **결정론적 시드 옵션**: 동일 청크 좌표 + 동일 시드 → 동일 스폰 결과 (Zone 시스템과 동일 정책).

---

## 3. 데이터 모델

### 3.1 SpawnPoolTable (신규)

스폰 풀의 헤더 행. 풀 단위로 ID를 부여한다.

| 컬럼 | 타입 | 의미 | 예 |
|---|---|---|---|
| Id | int | 풀 ID | 100 |
| Name | string | 디버그용 이름 | "Forest_Main_Tier1" |
| GroupMode | enum | `Homogeneous` / `Mixed` | Homogeneous |
| EntryIds | int[] | SpawnPoolEntry ID 목록 | [1001, 1002, 1003] |

- **Homogeneous**: 그룹 단위로 풀에서 1종 샘플링 → 그룹 내 동일 종 (현재 동작).
- **Mixed**: 개체 단위로 풀에서 매번 샘플링 → 그룹 내 다종 혼합.

### 3.2 SpawnPoolEntry (신규)

풀 안에 들어가는 한 종의 가중치/조건.

| 컬럼 | 타입 | 의미 |
|---|---|---|
| Id | int | 엔트리 ID |
| MonsterTableId | int | 어떤 몬스터인지 |
| Weight | int | 풀 내 상대 가중치 (가중치 합 W → [0,W) 균등 샘플링) |
| MinZone | int | 이 엔트리가 활성화되는 최소 Zone (0=무제한) |
| MaxZone | int | 최대 Zone (0=무제한) |
| LevelOffset | int | Zone 레벨 대비 +오프셋 (예: 강한 종은 +2) |
| ArchetypeOverride | enum | `None` / `Magic` / `Rare` / `Boss` (None이면 MonsterTable.Archetype 사용) |

> **MinZone/MaxZone**: 같은 풀 안에서도 Zone 깊이에 따라 "초반엔 고블린만, 후반엔 오크가 섞임" 같은 점진적 변이를 표현.

### 3.3 ZoneTable 확장

기존 ZoneTable에 풀 참조 컬럼을 추가한다. 기존 그룹 카운트/크기/반경 컬럼은 그대로 유지.

| 신규 컬럼 | 타입 | 의미 |
|---|---|---|
| MainPoolId | int | MainGroup이 사용할 SpawnPool ID |
| SubPoolId | int | SubGroup이 사용할 SpawnPool ID (0이면 Main과 동일) |
| ElitePoolId | int | 엘리트 승격 시 사용할 풀 (0이면 Main 풀 + Archetype 승격) |
| BossPoolId | int | 보스 청크용 풀 (0이면 보스 없음) |
| EliteRate | int | SubGroup 중 엘리트로 승격될 비율 (%) |
| BossChunkPeriod | int | N 청크마다 1개 보스 청크 (0이면 없음) |

### 3.4 Archetype 분포 정책

기본적으로 MonsterTable.Archetype을 따르되, 다음 두 경로로 **개체 단위 등급 승격**이 가능하다.

1. **SpawnPoolEntry.ArchetypeOverride**: 풀 내 특정 엔트리를 항상 특정 등급으로 (예: Rare 전용 슬롯).
2. **ZoneTable.EliteRate**: 일반 풀에서 뽑힌 개체를 확률적으로 Magic/Rare로 승격.

승격 시 적용 효과:
- `BALANCE_DESIGN.md` §3.3의 Archetype 배율 (HpMul/DmgMul) 자동 적용 (이미 `ApplyMonsterArchetypeScaling`에서 처리됨).
- DropRateBonus / DropRarityBonus 추가 (Archetype별 보너스 상수, 추후 정의).

---

## 4. Phase 1: 단순 랜덤 풀 (MVP)

목적: **테이블 추가 없이, 인스펙터만으로** 몬스터 종 다양성을 확보. 기존 코드 변경 최소.

### 4.1 변경 사항

`MonsterManager`의 단일 ID 필드를 **List**로 확장.

| 기존 | 변경 |
|---|---|
| `[SerializeField] private int _mainMonsterTableId;` | `[SerializeField] private List<int> _mainMonsterTablePool;` |
| `[SerializeField] private int _subMonsterTableId;` | `[SerializeField] private List<int> _subMonsterTablePool;` |

스폰 시점 변경:
- `SpawnInitialMonstersInChunk()`: 각 그룹 anchor마다 `_mainMonsterTablePool`에서 균등 랜덤 1개 선택, 그룹 내 모든 개체에 적용.
- `RespawnMonsterInChunk()`: 같은 풀에서 다시 랜덤 (원래 종 강제 X).

### 4.2 호출 흐름 (Phase 1)

```
SpawnInitialMonstersInChunk(chunkCoord)
  ├── ResolveSpawnParams() → Zone 파라미터 (현재와 동일)
  ├── mainAnchors = RejectionSample(...)
  ├── for (각 mainAnchor)
  │     int monsterId = _mainMonsterTablePool[Random.Range(0, Count)];
  │     SpawnGroup(monsterId, anchor, ...);
  └── (Sub도 동일)
```

### 4.3 코드 변경 위치 요약

| 파일 | 변경 |
|---|---|
| [MonsterManager.cs](../Assets/Scripts/Manager/MonsterManager.cs) | 필드 → List, `SpawnGroup`에 monsterId 인자, `SpawnInitialMonstersInChunk`/`RespawnMonsterInChunk`에서 풀 샘플링 |
| [System_MonsterSpawn.cs](../Assets/Scripts/Common/System/System_MonsterSpawn.cs) | 변경 없음 (MonsterManager API만 사용) |

### 4.4 한계 (Phase 1)

- 모든 청크가 같은 풀을 사용 → Zone 깊이에 따른 종 변화 없음.
- 가중치 없음 (균등 분포).
- 등급 승격 없음.

> **Phase 1만으로도** "고블린/오크/늑대 셋 중 랜덤"이 가능하므로 당장 몬스터 추가가 자연스러워진다.

---

## 5. Phase 2: Zone × 풀 매핑 + 가중치

### 5.1 도입

- `SpawnPoolTable` / `SpawnPoolEntry` 테이블 추가.
- `ZoneTable.MainPoolId` / `SubPoolId` 컬럼 추가.
- `DataManager`에 `GetSpawnPool(id)` / `GetSpawnPoolEntry(id)` 조회 메서드 추가.

### 5.2 가중치 샘플링 알고리즘

```csharp
public static int SamplePool(SpawnPoolTable pool, int zone)
{
    int totalWeight = 0;
    SparseSet<SpawnPoolEntry> filtered = new();
    for (int i = 0; i < pool.EntryIds.Length; i++)
    {
        SpawnPoolEntry entry = AR.s.Data.GetSpawnPoolEntry(pool.EntryIds[i]);
        if (entry.MinZone > 0 && zone < entry.MinZone) continue;
        if (entry.MaxZone > 0 && zone > entry.MaxZone) continue;
        totalWeight += entry.Weight;
        filtered.Add(entry);
    }
    if (totalWeight <= 0) return 0;

    int roll = Random.Range(0, totalWeight);
    int acc = 0;
    for (int i = 0; i < filtered.Count; i++)
    {
        acc += filtered[i].Weight;
        if (roll < acc) return filtered[i].MonsterTableId;
    }
    return filtered[0].MonsterTableId;
}
```

### 5.3 그룹 모드별 처리

```
GroupMode = Homogeneous (기본)
  → 그룹당 1회 샘플링, 그룹 내 모든 개체 같은 종

GroupMode = Mixed
  → 개체마다 샘플링, 그룹 내 다종 혼합
```

### 5.4 LevelOffset 적용

```
finalLevel = zone + entry.LevelOffset
EntityFactory.CreateMonster(monsterId, pos, parent, finalLevel)
```

LevelOffset의 의미:
- +1~+2: "이 풀의 강한 종" (예: 같은 Zone에서 오크는 고블린보다 강함).
- -1~-2: "약한 잡몹" (다수 출현용).

### 5.5 마이그레이션

| 단계 | 내용 |
|---|---|
| 1 | 구글 시트에 `SpawnPoolTable`, `SpawnPoolEntry` 시트 추가 |
| 2 | [DownloadTables.cs](../Assets/Scripts/Editor/DownloadTables.cs) Register + ParseXxxTable 추가 |
| 3 | ZoneTable에 PoolId 컬럼 추가, 기존 GID 범위 확장 |
| 4 | MonsterManager가 ZoneTable에서 PoolId 조회 → SamplePool로 종 결정 |
| 5 | Phase 1의 인스펙터 List는 **fallback**으로 남김 (ZoneTable에 PoolId가 없으면 사용) |

---

## 6. Phase 3: 등급 승격 / 엘리트 / 보스

### 6.1 엘리트 승격

`SubGroup` 또는 `MainGroup` 그룹에서 일부 개체를 Magic/Rare로 승격.

```
foreach (개체 in 그룹)
{
    Archetype archetype = entry.ArchetypeOverride;
    if (archetype == None && ZoneTable.EliteRate > 0)
    {
        if (Random.Range(0, 100) < ZoneTable.EliteRate)
            archetype = RollEliteTier(); // Magic 80%, Rare 20% 등
    }
    EntityFactory.CreateMonster(monsterId, pos, parent, level, archetype);
}
```

`EntityFactory.CreateMonster()`에 `Archetype` 파라미터 추가 (현재는 MonsterTable.Archetype을 그대로 씀). 승격 시:
- HpMul / DmgMul 적용 ([BALANCE_DESIGN.md](../combat/BALANCE_DESIGN.md) §3.3)
- DropRateBonus / DropRarityBonus 추가
- 시각적 식별자 (틴팅, 파티클 등 — 시각 디자인 별도)

### 6.2 보스 청크

`ZoneTable.BossChunkPeriod`가 N이면, **Zone 내 N개 청크마다 1개를 보스 청크로 마킹**.

판정 방식 (결정론적):
```csharp
public static bool IsBossChunk(Vector2Int chunkCoord, int zone)
{
    ZoneTable z = AR.s.Data.GetZone(zone);
    if (z.BossChunkPeriod <= 0) return false;
    int hash = (chunkCoord.x * 73856093) ^ (chunkCoord.y * 19349663);
    return (Mathf.Abs(hash) % z.BossChunkPeriod) == 0;
}
```

보스 청크 처리:
- `MainPoolId` 대신 `BossPoolId`에서 샘플링.
- MainGroup 1개만 생성 (보스는 단독).
- SubGroup은 일반 호위(adds)로 유지하거나 비활성화.

### 6.3 풀별 가중치 예시 (Zone 5)

| Pool | Entry | MonsterId | Weight | LevelOffset | ArchetypeOverride |
|---|---|---|---|---|---|
| Main(100) | 1001 | Goblin | 60 | 0 | None |
| Main(100) | 1002 | GoblinArcher | 30 | 0 | None |
| Main(100) | 1003 | GoblinShaman | 10 | +1 | Magic |
| Boss(200) | 2001 | GoblinKing | 100 | +2 | Boss |

→ 일반 청크는 고블린류 3종 중 가중치 분포로 등장, 6N 청크마다 GoblinKing 단독 등장.

---

## 7. Phase 4: 확장 (선택, 후순위)

### 7.1 시간대 / 이벤트 풀

- **TimeOfDayPoolOverride**: 밤이면 특정 풀로 교체 (언데드 등).
- **EventPool**: 침공/사냥 이벤트 발생 시 임시 풀 주입.

### 7.2 바이옴 / 테마

- 청크에 BiomeId 부여 (THEME_TILESET_REFACTOR.md 참조) → BiomePoolOverride.
- ZoneTable이 아니라 ChunkBiome별 PoolId.

#### 7.2.1 구현됨 — 바이옴 경량 풀 (2026-07-24)

Phase 2의 `SpawnPool*` 테이블을 도입하기 전에, **`MonsterTable.Biome`을 풀 키로 직접 쓰는 경량 버전**을 먼저 구현했다.
"종 다양성 + biome 게이팅"이라는 핵심 이득을 새 테이블 없이 확보하는 것이 목적이다.

**규칙**
- `MonsterTable.Biome` 기본값 = `BiomeType.Any`(공용). 시트 빈 셀/누락도 `Any`로 파싱한다.
- **공용(`Any`)** 몬스터 = 모든 biome 스폰 풀에 포함. **특정 biome** 몬스터 = 그 biome 풀에만 포함.
- 청크 biome의 풀 = `공용 ∪ 그 biome 전용`. 여기서 **그룹 anchor마다 균등 샘플링**(Phase 1의 그룹 단위 동종 규칙과 동일). 리스폰도 같은 풀에서 재샘플.
- 풀이 비면(그 biome 전용도 없고 공용도 없음) MonsterManager 인스펙터 `_mainMonsterTableId`/`_subMonsterTableId` fallback.

**코드 앵커**
- 풀 인덱스 구축: [`DataManager_Table.cs`](../../Assets/Scripts/Data/DataManager_Table.cs) — 로드 시 `_commonMonsterIds`(Any) / `_monsterIdsByBiome`(biome전용) 구축, 접근자 `GetCommonMonsterIds()` / `GetBiomeSpecificMonsterIds(biome)`.
- 샘플링 로직: [`BiomeMonsterMapping.SampleMonsterId(biome, fallback)`](../../Assets/Scripts/Map/Biome/BiomeMonsterMapping.cs) — 무상태, `공용∪전용`에서 균등 1개.
- 스폰 호출: [`MonsterManager`](../../Assets/Scripts/Manager/MonsterManager.cs) `SpawnInitialMonstersInChunk` / `RespawnMonsterInChunk` — 그룹마다 `SampleMonsterId` 호출.
- Biome 컬럼 의미는 역방향 위협 게이트(PLAYER_RECOGNITION_DESIGN.md)와 **공유**한다 — `Any`=범용, 특정 biome=국지.

**아직 없음(Phase 2~3로 이관)**: 가중치, `MinZone`/`MaxZone` 필터, Zone×풀 매핑, Archetype 승격, 보스 청크.
이 경량 버전은 위 확장이 들어올 때 `SamplePool`(가중치 버전)로 자연스럽게 교체 가능하다.

### 7.3 동적 가중치 조정

- 플레이어 빌드/진행도/물리 저항 등에 따라 풀 내 가중치 조정 (예: 원거리 빌드면 근접 몬스터 비율 ↑).
- 캐주얼/하드 모드 토글.

> Phase 4는 **선택**. Phase 1~3만 완료해도 기존 한계는 모두 해소된다.

---

## 8. 도입 로드맵

| Phase | 범위 | 예상 작업량 | 선행 조건 |
|---|---|---|---|
| **1** | MonsterManager 인스펙터 List 화 | **0.5일** | 없음 |
| **2** | SpawnPoolTable/Entry 도입, ZoneTable 확장, 가중치 샘플링 | 2~3일 | Phase 1, 구글 시트 작업 |
| **3** | Archetype 승격, 보스 청크 | 2~3일 | Phase 2 |
| **4** | 시간대/바이옴/동적 가중치 | (보류) | Phase 3 + 바이옴 시스템 |

**권장 시작점: Phase 1**. 즉시 적용 가능하고 후속 단계와 호환된다.

---

## 9. 기존 시스템과의 통합

### 9.1 드랍 시스템

변경 없음. 풀은 MonsterTableId만 반환하며, 이후 `EntityFactory.CreateMonster()` → `DropComponent` 부여 → `DropHelper.ProcessDrop()` 흐름은 그대로.

엘리트 승격 시 `DropRateBonus` / `DropRarityBonus`만 추가 가산.

### 9.2 AI 시스템

변경 없음. MonsterTable.AiTableId가 AI 행동을 결정. 같은 몬스터의 엘리트 변종은 동일 AI를 쓰되 스탯만 강화.

향후 확장 시 `SpawnPoolEntry.AiTableIdOverride` 컬럼으로 같은 몬스터 + 다른 AI 패턴(예: 광폭화 엘리트)도 가능.

### 9.3 Zone 시스템

- `MonsterLevel`은 여전히 Zone에서 계산 (`zoneLevel = (zone - 1) × k + base` 등 기존 정책 유지).
- `SpawnPoolEntry.LevelOffset`은 그 위에 더해지는 보정값.

### 9.4 청크 생명주기 / 리스폰

- 청크 진입 시 1회 스폰: 변경 없음.
- 60초 리스폰: **같은 풀에서 재샘플링** (Phase 1부터). 그룹 단위 일관성을 위해 죽은 그룹 단위로 재샘플 (선택).
- 청크 TTL(5분) 만료: 변경 없음.

---

## 10. 코드 인터페이스 요약

### 10.1 신규 / 변경 메서드

```csharp
// MonsterManager
public int SampleMonsterId(int poolId, int zone);            // 신규 (Phase 2)
public Archetype RollArchetype(int eliteRate);               // 신규 (Phase 3)
public void SpawnGroup(int monsterId, Vector3 anchor, ...);  // 변경: monsterId 인자 추가
public bool IsBossChunk(Vector2Int chunkCoord, int zone);    // 신규 (Phase 3)

// EntityFactory
public static UniTask<(int, EntityBase?)> CreateMonster(
    int monsterTableId,
    Vector3 position,
    Transform? parent = null,
    int level = 0,
    MonsterArchetype archetypeOverride = MonsterArchetype.None);  // 변경: archetypeOverride 추가

// DataManager
public SpawnPoolTable? GetSpawnPool(int id);          // 신규
public SpawnPoolEntry? GetSpawnPoolEntry(int id);     // 신규
```

### 10.2 새 테이블 다운로드

[DownloadTables.cs](../Assets/Scripts/Editor/DownloadTables.cs)에 등록:

```csharp
Register<SpawnPoolTable>(list, "<GID>&range=A:D");
Register<SpawnPoolEntry>(list, "<GID>&range=A:G");
```

ZoneTable GID 범위는 `A:M` → `A:S` 등으로 확장.

---

## 11. 검증 시나리오

| 시나리오 | 기대 결과 |
|---|---|
| **Phase 1**: 풀에 [Goblin, Orc, Wolf] 등록 | 청크마다 그룹 단위로 셋 중 랜덤 1종 등장 |
| **Phase 2**: Zone 1=숲풀, Zone 5=오크풀, Zone 10=언데드풀 | Zone 깊이에 따라 등장 종 자연 변이 |
| **Phase 2**: MinZone=3인 엔트리 | Zone 1~2에선 등장 X, Zone 3+부터 활성 |
| **Phase 3**: EliteRate=10% | 100마리 중 평균 10마리가 Magic/Rare로 승격 |
| **Phase 3**: BossChunkPeriod=8 | 평균 8청크마다 1개 보스 청크 등장 |
| **리스폰**: Phase 1 + 풀 3종 | 60초 후 같은 청크에 다른 종으로 보충될 수 있음 |

---

## 12. 관련 문서

- [BALANCE_DESIGN.md](../combat/BALANCE_DESIGN.md) — 몬스터 레벨 곡선, Archetype 배율, TTK/TTD
- [monsterAIPattern.md](monsterAIPattern.md) — AI 행동 패턴 (풀과 독립)
- [SKILL_DESIGN.md](../combat/SKILL_DESIGN.md) — 몬스터 스킬 (AiTable 연동)
- [THEME_TILESET_REFACTOR.md](../map/THEME_TILESET_REFACTOR.md) — 바이옴/테마 (Phase 4 선행)
