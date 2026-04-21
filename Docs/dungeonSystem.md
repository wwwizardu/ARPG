# 던전 시스템 설계 (Dungeon System)

## 개요
절차적 생성 던전 시스템. 코어 키퍼 스타일의 탐험 가능한 지하 던전, 보스룸, 보물, 함정으로 구성.

---

## 1. 던전 구조

### 던전 타입
```
Type 1: Cave (동굴)      - 유기적 형태, 좁은 통로
Type 2: Ruins (유적)     - 직사각형 방, 넓은 복도
Type 3: Crypt (묘지)     - 작은 방 + 미로, 언데드 다수
Type 4: Laboratory (연구소) - 정형화된 구조, 기계 적
Type 5: Abyss (심연)     - 혼돈의 구조, 최고 난이도
```

### 던전 난이도 (Tier)
| Tier | 이름 | 권장 레벨 | 방 개수 | 보스 난이도 |
|------|------|-----------|---------|-------------|
| 1 | 쉬움 | 1~5 | 3~5 | Easy |
| 2 | 보통 | 6~10 | 5~8 | Normal |
| 3 | 어려움 | 11~15 | 8~12 | Hard |
| 4 | 매우 어려움 | 16~20 | 12~18 | Very Hard |
| 5 | 악몽 | 21+ | 18~25 | Nightmare |

---

## 2. 절차적 생성 알고리즘

### 생성 단계
```
1. Seed 생성 (World Position 기반)
2. 방(Room) 생성
3. 복도(Corridor) 연결
4. 입구/출구 배치
5. 보스룸 배치
6. 보물방 배치
7. 몬스터 스폰
8. 함정 배치
9. 오브젝트 배치 (항아리, 상자 등)
```

### Room + Corridor 알고리즘
```csharp
public class DungeonGenerator
{
    private int _seed;
    private System.Random _random;
    private List<Room> _rooms;
    private List<Corridor> _corridors;

    public void Generate(int tier, DungeonType type)
    {
        _seed = GetSeedFromWorldPosition();
        _random = new System.Random(_seed);

        // 1. 방 생성
        int roomCount = GetRoomCount(tier);
        for (int i = 0; i < roomCount; i++)
        {
            Room room = GenerateRoom(type, tier);
            _rooms.Add(room);
        }

        // 2. 방 배치 (충돌 방지)
        PlaceRooms();

        // 3. 방 연결 (최소 스패닝 트리)
        ConnectRooms();

        // 4. 보스룸 배치 (가장 먼 방)
        Room bossRoom = GetFarthestRoom(_rooms[0]); // 입구에서 가장 먼 방
        bossRoom.Type = RoomType.Boss;

        // 5. 보물방 배치 (막다른 방)
        List<Room> deadEnds = GetDeadEndRooms();
        foreach (var room in deadEnds)
        {
            if (_random.NextDouble() < 0.3f) // 30% 확률
                room.Type = RoomType.Treasure;
        }

        // 6. 몬스터 & 함정 배치
        PopulateDungeon(tier);
    }

    private void PlaceRooms()
    {
        // Binary Space Partitioning (BSP) 또는
        // Grid-based placement
        // 충돌 감지 및 간격 조정
    }

    private void ConnectRooms()
    {
        // Delaunay Triangulation + Minimum Spanning Tree
        // 또는 간단한 최근접 연결
    }
}
```

---

## 3. 방(Room) 종류

### RoomType
```csharp
public enum RoomType
{
    Normal,         // 일반 방 (몬스터 + 아이템)
    Entrance,       // 입구 (안전 지대)
    Boss,           // 보스룸 (대형 방, 보스 1체)
    Treasure,       // 보물방 (상자 다수, 몬스터 없음)
    Elite,          // 엘리트 방 (강력한 적 1~2체)
    Trap,           // 함정 방 (함정 밀집, 몬스터 적음)
    Shrine,         // 성소 (버프, 힐, 상점 등)
    Secret          // 숨겨진 방 (특별 보상)
}
```

### Room 크기
```csharp
public struct Room
{
    public RoomType Type;
    public Vector2Int Position;  // 월드 좌표
    public int Width;            // 타일 단위
    public int Height;
    public List<Vector2Int> ConnectedRooms; // 연결된 방 인덱스
    public List<Vector2Int> MonsterSpawns;  // 몬스터 스폰 위치
    public List<Vector2Int> TrapPositions;  // 함정 위치
    public List<Vector2Int> ChestPositions; // 상자 위치
}

// 방 크기 예시
Normal:   8x8 ~ 12x12
Boss:     16x16 ~ 20x20
Treasure: 6x6 ~ 8x8
Elite:    10x10 ~ 14x14
```

---

## 4. 복도(Corridor) 생성

### 복도 타입
- **직선 복도**: 두 방을 직선으로 연결
- **L자 복도**: 수평 + 수직 연결
- **Z자 복도**: 여러 번 꺾임

### 복도 규칙
- **최소 폭**: 2타일
- **최대 길이**: 20타일
- **겹침 방지**: 기존 방/복도와 충돌 시 우회

```csharp
public struct Corridor
{
    public List<Vector2Int> Path; // 복도 경로 (타일 좌표)
    public int Width;              // 복도 폭 (2~3)
}

private Corridor CreateCorridor(Room roomA, Room roomB)
{
    // A 방의 출구
    Vector2Int exitA = GetRandomExit(roomA);

    // B 방의 입구
    Vector2Int entranceB = GetRandomEntrance(roomB);

    // L자 경로 생성
    List<Vector2Int> path = new List<Vector2Int>();

    // 수평 이동
    Vector2Int current = exitA;
    while (current.x != entranceB.x)
    {
        current.x += (entranceB.x > current.x) ? 1 : -1;
        path.Add(current);
    }

    // 수직 이동
    while (current.y != entranceB.y)
    {
        current.y += (entranceB.y > current.y) ? 1 : -1;
        path.Add(current);
    }

    return new Corridor { Path = path, Width = 2 };
}
```

---

## 5. 보스 시스템

### 보스 특징
- **고유 패턴**: 페이즈별 공격 패턴
- **높은 HP**: 일반 몬스터의 10~20배
- **특수 메커니즘**: 무적 구간, 소환, 지형 변화
- **보상**: 고급 아이템 + 골드 + 경험치

### 보스 페이즈
```csharp
public enum BossPhase
{
    Phase1,     // 100% ~ 70% HP
    Phase2,     // 70% ~ 40% HP
    Phase3      // 40% ~ 0% HP (강화 패턴)
}
```

### 보스 예시

#### Tier 1 Boss: Giant Slime (거대 슬라임)
```
HP: 500
페이즈 1 (100~70%):
  - 근접 공격 (데미지 10)
  - 점프 공격 (범위 공격, 데미지 15)

페이즈 2 (70~40%):
  - 작은 슬라임 2마리 소환
  - 독 웅덩이 생성 (지속 데미지)

페이즈 3 (40~0%):
  - 광폭화 (이동/공격 속도 증가)
  - 전방 독 발사 (3방향)
```

#### Tier 3 Boss: Lich King (리치 왕)
```
HP: 2000
페이즈 1 (100~70%):
  - 마법 구체 발사 (3연사)
  - 텔레포트 (랜덤 위치 이동)
  - 언데드 소환 (스켈레톤 3마리)

페이즈 2 (70~40%):
  - 얼음 창 (직선 발사, 관통)
  - 바닥 얼리기 (이동 속도 감소)
  - 무적 구간 (2초, 언데드 5마리 소환)

페이즈 3 (40~0%):
  - 죽음의 낫 (광역 공격, 데미지 50)
  - 생명력 흡수 (플레이어 HP 10% 흡수)
  - 최후의 저주 (3초 후 대폭발, 피해야 함)
```

---

## 6. 몬스터 스폰

### 스폰 규칙
```csharp
// 방 크기에 비례한 몬스터 수
int monsterCount = (room.Width * room.Height) / 20; // 약 20타일당 1마리

// Tier에 따른 몬스터 레벨
int monsterLevel = dungeonTier * 5 + Random.Range(-2, 2);

// 몬스터 종류
MonsterType[] tierMonsters = GetMonstersForTier(dungeonTier);
MonsterType selected = tierMonsters[Random.Range(0, tierMonsters.Length)];
```

### 몬스터 풀 (Tier별)
```
Tier 1: Slime, Goblin, Bat
Tier 2: Orc, Wolf, Spider
Tier 3: Skeleton, Zombie, Ghost
Tier 4: Golem, Demon, Wraith
Tier 5: Dragon, Lich, Abomination
```

### 스폰 위치
- **방 중심 제외**: 플레이어 진입 시 즉시 전투 방지
- **벽 근처 우선**: 매복 느낌
- **그룹 스폰**: 2~3마리씩 뭉쳐서 배치

---

## 7. 함정 시스템

### 함정 종류
```
Spike Trap (가시 함정):
  - 발동: 밟으면 즉시
  - 데미지: 20 (고정)
  - 재사용: 3초 후

Arrow Trap (화살 함정):
  - 발동: 밟으면 화살 발사
  - 데미지: 15
  - 재사용: 없음 (1회용)

Fire Trap (불 함정):
  - 발동: 밟으면 화염 분출
  - 데미지: 10/초, 3초 지속
  - 재사용: 5초 후

Pit Trap (구덩이 함정):
  - 발동: 밟으면 추락
  - 데미지: 30 + 2초 경직
  - 재사용: 없음

Magic Trap (마법 함정):
  - 발동: 범위 진입 시
  - 효과: 랜덤 디버프 (속도 감소, 독, 저주)
  - 재사용: 10초 후
```

### 함정 배치
- **복도**: 화살 함정, 구덩이 함정
- **방 입구**: 불 함정, 가시 함정
- **보물 근처**: 마법 함정, 구덩이 함정

### 함정 탐지
- **시각적 힌트**: 미묘한 색 차이, 균열
- **동료 스킬**: 사냥꾼 = 함정 탐지 범위 증가
- **아이템**: "함정 탐지 포션" 사용 시 5분간 표시

---

## 8. 보물 & 보상

### 보물 상자 종류
```
Wooden Chest (나무 상자):
  - 드랍: 골드 10~30, 일반 아이템
  - 확률: 70%

Iron Chest (철 상자):
  - 드랍: 골드 30~70, 레어 아이템
  - 확률: 25%

Golden Chest (황금 상자):
  - 드랍: 골드 100~200, 에픽 아이템
  - 확률: 5%

Mimic Chest (미믹):
  - 드랍: 없음 (몬스터!)
  - 확률: 10% (일반 상자가 변환)
```

### 보상 테이블 (Tier별)
```csharp
// Tier 1 보상
Gold: 10~50
Items: Common (70%), Uncommon (25%), Rare (5%)
Equipment: Lv 1~5

// Tier 3 보상
Gold: 50~150
Items: Uncommon (40%), Rare (40%), Epic (15%), Legendary (5%)
Equipment: Lv 11~15

// Tier 5 보상
Gold: 200~500
Items: Rare (30%), Epic (40%), Legendary (25%), Mythic (5%)
Equipment: Lv 21~25
```

### 보스 보상
```
보스 처치 시:
  - 골드: 일반 보상의 3배
  - 아이템: 등급 +1 (Rare → Epic)
  - 보스 고유 아이템: 10% 확률
  - 던전 완료 경험치: +50%
```

---

## 9. 던전 입장/퇴장

### 입장 방법
1. **오픈월드에서 던전 입구 발견**
   - 동굴 입구, 유적 문, 균열 등
   - 상호작용 키(E) 누르면 입장

2. **던전 난이도 선택**
   ```
   ┌─────────────────────────────┐
   │  던전: 고대 유적             │
   │  난이도:                    │
   │    [Tier 1] (권장 Lv 1~5)   │
   │    [Tier 2] (권장 Lv 6~10)  │
   │    [Tier 3] (권장 Lv 11~15) │
   │  ───────────────────────── │
   │  [입장]  [취소]             │
   └─────────────────────────────┘
   ```

3. **던전 로딩**
   - 씬 전환 또는 청크 로딩
   - 던전 생성 (절차적)

### 퇴장 방법
1. **보스 처치 후 포탈 생성**
   - 보스룸에 출구 포탈 등장
   - 포탈로 들어가면 던전 밖으로

2. **귀환 두루마리 사용**
   - 아이템: "귀환 두루마리"
   - 사용 시 5초 채널링 → 마을로 귀환
   - 전투 중 사용 불가

3. **사망 시**
   - 마을 부활 지점으로 강제 귀환
   - 던전 진행도 초기화
   - 아이템/골드 일부 손실 (옵션)

---

## 10. 던전 진행도 & 재도전

### 진행도 저장
- **던전 내 저장 불가**: 한 번 들어가면 끝까지
- **보스 처치 시 완료**: 던전 클리어 기록

### 재도전
- **즉시 재도전 가능**: 쿨타임 없음
- **새로운 레이아웃**: 동일 Seed 사용 시 동일, 아니면 랜덤
- **보상 재획득 가능**: 보스 보상 포함

### 던전 리셋
- **매일 0시**: 던전 Seed 변경 (옵션)
- **수동 리셋**: 던전 입구에서 "리셋" 선택

---

## 11. 미니맵 & 안개

### 미니맵
- **탐험한 방만 표시**: 안개로 가려진 미탐험 구역
- **현재 위치**: 플레이어 아이콘
- **방 타입 표시**: 보스룸 = 빨강, 보물방 = 노랑
- **몬스터 표시**: 근처 적 = 작은 점

### 안개 시스템 (Fog of War)
```csharp
// 탐험한 타일 기록
HashSet<Vector2Int> _exploredTiles = new HashSet<Vector2Int>();

// 플레이어 시야 범위 내 타일 공개
void UpdateFogOfWar(Vector2 playerPosition)
{
    int sightRange = 10; // 시야 범위
    for (int x = -sightRange; x <= sightRange; x++)
    {
        for (int y = -sightRange; y <= sightRange; y++)
        {
            Vector2Int tile = new Vector2Int(
                Mathf.FloorToInt(playerPosition.x) + x,
                Mathf.FloorToInt(playerPosition.y) + y
            );

            float distance = Vector2.Distance(playerPosition, new Vector2(tile.x, tile.y));
            if (distance <= sightRange)
            {
                _exploredTiles.Add(tile);
            }
        }
    }
}
```

---

## 12. 던전 이벤트

### 랜덤 이벤트
```
상인 조우 (5% 확률):
  - 방 하나에 떠돌이 상인 등장
  - 비싼 가격에 아이템 판매/구매

성소 발견 (10% 확률):
  - 방 하나에 성소 등장
  - 선택: HP 회복 OR 버프 OR 저주 (리스크)

함정 방 (15% 확률):
  - 방 전체가 함정으로 가득
  - 보상: 함정 돌파 시 보물 상자

엘리트 몬스터 (20% 확률):
  - 방 하나에 강력한 엘리트 1체
  - 보상: 보스급 아이템
```

---

## 13. 구현 우선순위

### Phase 1: 기본 던전 생성
- [ ] DungeonGenerator 클래스
- [ ] Room + Corridor 알고리즘
- [ ] 타일맵 렌더링
- [ ] 입구/출구 배치

### Phase 2: 몬스터 & 전투
- [ ] 방별 몬스터 스폰
- [ ] 보스룸 생성
- [ ] 보스 AI 패턴

### Phase 3: 보물 & 보상
- [ ] 보물 상자 배치
- [ ] 보상 테이블
- [ ] 보스 드랍

### Phase 4: 함정 & 이벤트
- [ ] 함정 시스템
- [ ] 랜덤 이벤트
- [ ] 성소/상인 조우

### Phase 5: UI & 피드백
- [ ] 미니맵
- [ ] 안개 시스템
- [ ] 던전 완료 UI
- [ ] 보상 획득 연출

---

## 14. 테스트 시나리오

### 테스트 1: 던전 생성
1. 던전 입구에서 입장
2. Tier 1 선택
3. 던전 생성 확인 (3~5개 방)
4. 방 연결 확인 (복도)
5. 보스룸 존재 확인

### 테스트 2: 던전 탐험
1. 플레이어 이동
2. 몬스터 조우 및 전투
3. 보물 상자 발견 및 획득
4. 함정 밟기 및 데미지 확인
5. 보스룸 도달

### 테스트 3: 보스 전투
1. 보스 전투 시작
2. 페이즈 전환 확인
3. 보스 처치
4. 출구 포탈 생성
5. 보상 획득 후 퇴장

---

## 15. 밸런스 고려사항

### 난이도 곡선
- **Tier 1**: 튜토리얼 수준, 쉬운 적, 명확한 경로
- **Tier 3**: 중급 플레이어, 복잡한 구조, 함정 증가
- **Tier 5**: 엔드게임, 미로 같은 구조, 엘리트 다수

### 보상 밸런스
- **시간당 골드**: Tier 1 = 100G/10분, Tier 5 = 1000G/20분
- **아이템 드랍률**: Tier 증가 시 레어도 상승
- **경험치**: 던전 완료 시 큰 보너스 (동일 레벨 몬스터 10마리 상당)

---

**Last Updated**: 2026-04-01
**Status**: 설계 완료, 구현 대기 중
**Dependencies**: 맵 생성 시스템, 전투 시스템, AI 시스템
