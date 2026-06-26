# Chronicle Sim Prototype

정착지 연대기 시뮬(설계 L1, Tier 2 = 정복-병합 + 과확장 페널티)을 **Unity 밖에서 빠르게 돌려보고 튜닝**하기 위한 독립 콘솔 도구.
[NPC_RELATION_HISTORY_PLAN.md](../../Docs/NPC_RELATION_HISTORY_PLAN.md) R1의 위험(역사가 재미있게 나오나 / 한 덩어리로 수렴하나)을 R0 구현 전에 검증한다.

## 왜 둘로 나눠져 있나 (하이브리드)

`RaceSettlementPlacer.ComputeAll`의 배치는 `BiomeNoise`(`Mathf.PerlinNoise`, UnityEngine)에 의존 → 콘솔로 포팅 불가.
그래서 **실제 배치는 Unity가 한 번 export, 시뮬은 콘솔이 반복**한다. 콘솔은 실제 좌표/종족/거리를 그대로 쓰므로 결과가 믿을 만하다 (mock 근사 아님).

```
[Unity 에디터]  ComputeAll 실제 배치  ──(JSON)──▶  [콘솔]  연대기 시뮬 + 통계/스윕
   1회 export                placements/             가중치 튜닝은 여기서 초 단위 반복
```

## 사용법

### 1) 실제 배치 export (Unity 에디터, 1회)
Unity 메뉴:
- `ARPG/Chronicle Sim/Export Default Seed (12345)` — 기본 시드 1개
- `ARPG/Chronicle Sim/Export Sweep (20 seeds)` — 시드 21개

→ `Tools/ChronicleSimPrototype/placements/seed_*.json` 생성.

### 2) 시뮬 실행 (콘솔)
```bash
cd Tools/ChronicleSimPrototype
dotnet run -- sweep placements          # 폴더 전체 집계 (★ 핵심)
dotnet run -- stats placements/seed_12345.json   # 한 시드: 왕국 목록 + 통계
dotnet run -- dump  placements/seed_12345.json   # 한 시드: 연대기 전문(긴 출력) + 위
dotnet run -- denscompare placements             # 밀도별 비교 (정착지 수↓ → 왕국 수↓)
```

선택 인자: `stats`/`sweep`/`villages`에 마지막 인자로 `density`(0~1) 추가 시 정착지를 결정론적으로 솎아 밀도 축소 실험 가능. 예: `sweep placements 0.25`.

### 2b) L2 — NPC 관계 데모 (연대기 → NPC/관계/sentiment)
```bash
# 마을 1개 호적부: NPC 명단 + 가족/직업/친구 관계 + 연대기 sentiment (★=핵심 NPC)
dotnet run -- dossier placements/seed_12345.json -1 0.12   # sid=-1 → 가장 극적인(정복당한) 마을 자동 선택
dotnet run -- dossier placements/seed_12345.json 1860 0.12 # 특정 정착지 id 지정

# 세계 전체: 마을 분류(피정복/전쟁/평시/외딴) + 생성될 NPC·관계·sentiment 총량
dotnet run -- villages placements/seed_12345.json 0.12
```
- 추상 인구(시뮬용 세력 가중치) → 스폰 NPC 수로 환산, **마을당 상한 50명** (Settlement 4~6 … City 40~50).
- 정복당한 타종족 마을 → 원주민 `ResentsKingdom`/유족 + 지배종족 점령층 `ProudOfKingdom` 분파.
- L2 생성 로직은 `NpcBackfill.cs` (설계 §5.3). 실제 R2 backfill로 이전될 원형.

### 3) 합성 데이터 (⚠️ Unity 없이 빠른 스모크 테스트)
실제 배치가 아니라 종족 클러스터를 흉내 낸 근사 분포. **결론용 아님** — 코드/파이프라인 확인이나 감 잡기용.
```bash
dotnet run -- gensweep                  # 합성 21개 → placements_synth/
dotnet run -- sweep placements_synth
```

## 출력 읽는 법

스윕 집계의 두 핵심 지표 (사용자 우려를 직접 측정):
- **단일 제국 수렴(최대 왕국 >40%) 발생 %** — *낮을수록* 좋음. 과확장 페널티가 단일 세계제국을 막는지.
- **다종족 제국 ≥1 발생 %** — *높을수록* 좋음. 정복-병합이 타종족을 편입해 다종족 제국을 만드는지.

`stats`/`dump`의 왕국 목록에서 `※다종족`은 정복으로 2개 이상 종족이 한 왕국에 든 경우.

## 튜닝 노브 — `SimConfig.cs`

값 바꾸고 `dotnet run` 하면 즉시 반영. 주요 항목:
| 노브 | 효과 |
|------|------|
| `SimYears` | 시뮬 기간(기본 150). 짧으면 통합 덜 됨 |
| `NeighborRegionDist` | 이웃 판정 거리. 크면 상호작용↑ 통합↑ |
| `OverextensionSlope` / `OverextensionMin` | **단일 제국 수렴 제동**. 작게 하면 큰 제국 허용 |
| `ConquerRatio` | 정복 발동 인구 우위 문턱. 낮을수록 정복↑ |
| `MergeRatio` / `PMerge` | 자발적 합병 빈도 |
| `KingdomMinCluster` / `JoinThreshold` | 왕국 형성/편입 난이도 |
| `RaceRelations.Base` (RaceData.cs) | 종족쌍 기본 호감도 — 누가 동맹/전쟁하는지의 근원 |

> 현재 기본값은 **과편화 경향**(왕국 수 많고 최대 왕국 작음)으로 관측됨. "여러 지역 제국" 쪽으로 가려면
> `OverextensionSlope`↓, `NeighborRegionDist`↑, `ConquerRatio`↓ 방향으로 튜닝. **실제 배치(placements/)로 튜닝할 것** — 합성은 분포가 다름.

## 파일

| 파일 | 역할 |
|------|------|
| `Program.cs` | CLI (dump/stats/sweep/gen/gensweep) + 연대기 텍스트 + 집계 |
| `ChronicleSimulator.cs` | 틱 루프 + 사건 9종(Conquer 포함) + 과확장 페널티 + 왕국 |
| `SimConfig.cs` | 모든 튜닝 상수 |
| `RaceData.cs` | 종족/biome enum, RaceRelations 행렬, biome 인구 동역학 |
| `Stats.cs` | 런별 지표 계산 |
| `Placement.cs` | export JSON DTO + 로더 |
| `DerivedSeed.cs` | Unity DerivedSeed 동일 믹서 + SplitMix64 스트림 |
| `Synthetic.cs` | ⚠️ 합성 placement 생성기 (테스트용) |
| `../../Assets/Scripts/Editor/ChronicleSimExport.cs` | Unity 측 export 메뉴 (ComputeAll 미러) |

## 실제 코드로의 이전 (R1)

여기서 검증·튜닝한 로직과 상수는 R1의 `SettlementChronicleSimulator`로 거의 그대로 옮겨간다.
`DerivedSeed`/SplitMix64 정수 연산이 동일하므로 결정론이 보존된다.

> ⚠️ `ChronicleSimExport.cs`는 `RaceSettlementPlacer.ComputeAll/TryPlace`를 미러링한다. 원본 로직이 바뀌면 미러도 동기화할 것.
