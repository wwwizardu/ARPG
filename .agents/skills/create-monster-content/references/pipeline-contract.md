# Monster content pipeline contract

이 문서는 근거리/원거리 역할이 확인된 뒤의 실행 순서, 참조 규칙, 완료 조건을 정의한다. 현재 코드와 live Sheet가 언제나 우선이며, 아래 스키마 요약은 검사용 기준선이다.

## 1. 정본과 선행 점검

- 런타임 필드와 참조: `Assets/Scripts/Common/Tables.cs`
- 다운로드 컬럼 순서와 파싱: `Assets/Scripts/Editor/DownloadTables.cs`
- 몬스터 생성과 Prefab 소비: `Assets/Scripts/Factory/EntityFactory.cs`
- 스폰 의미: `Wiki/monster_spawn_runtime.md`와 그 출처
- 전투 수치·Skill 의미: `Wiki/combat.md`와 그 출처
- live 데이터: 연결된 `ARPG Tables` Google Sheet
- 로컬 런타임 스냅샷: `Assets/_BinaryData/TableData/*Table.bytes`

쓰기 직전에 수정할 각 Sheet 탭의 헤더와 대상/비교 행을 다시 읽는다. Sheet 헤더와 파서가 다르면 쓰지 말고 불일치를 보고한다.

현재 기준 핵심 범위는 다음과 같다.

| 탭 | 기준 범위 | 핵심 참조 |
| --- | --- | --- |
| Animation | A:G | SpriteLibraryPath = SpriteLibraryAsset Addressable key |
| Projectile | A:G | PrefabKey |
| Skill | A:AQ | ProjectileId, AreaEffectId, AnimationName |
| AI | A:L | SkillId1~3와 Weight1~3 |
| Drop | A:K | Currency/Equipment pool과 가중치 |
| Stat | A:AF | Monster와 동일한 ID |
| Monster | A:Q | Stat, PrefabName, AnimationId, AI, Drop, Biome |

## 2. 의존성 그래프

```text
PNG 13개
  -> Unity Sprite import
  -> SpriteLibraryAsset
  -> Addressables entry (unique key)
  -> Animation row

combat role -> AI 1 (Melee) or AI 2 (Ranged)
            -> existing SkillId1~3
            -> existing ProjectileId when referenced
Drop 1
shared Addressable Prefabs/Entity

Animation + selected AI + Drop 1 + shared Entity + Stat
  -> Monster row
  -> Unity table download
  -> runtime CreateMonster verification
```

참조 대상이 먼저 존재하도록 위 순서를 유지한다. Sheet 쓰기는 관련 행을 한 번에 계획하되, 각 단계 직후 정확한 범위를 다시 읽어 값이 반영됐는지 확인한다.

## 3. 단계별 실행

### A. 이름과 ID 예약

1. 이름은 사용자 표기를 보존하고 파일/내부 키는 기존 PascalCase 규칙에 맞춘다.
2. 새 ID는 각 live 탭의 기존 ID 대역을 확인한 뒤 비어 있는 값을 선택한다. 단순히 로컬 bytes의 마지막 ID만 보고 배정하지 않는다.
3. Monster와 Stat은 같은 ID를 사용한다.
4. 새 Monster마다 전용 Animation ID를 별도로 자동 배정한다. 사용자에게 Animation ID 선택을 묻거나 다른 몬스터의 Animation 행을 재사용하지 않는다.
5. AI/Skill/Projectile/Drop은 새 ID를 예약하지 않는다. 사용자가 명시적으로 커스텀 생성을 요청하지 않은 한 기본 공유 행을 그대로 검증·사용한다.

### B. 스프라이트와 SpriteLibrary

1. `generate-monster-sprites` 계약으로 `Assets/Art/Sprites/Monster/<MonsterName>/`에 Idle 2, Move 4, Attack 4, Die 3의 투명 128×128 PNG를 만든다.
2. 파일명, 크기, 알파, ground contact, 프레임 간 시각 일관성을 검증한다.
3. Unity에서 Sprite import를 적용한다.
4. `create-sprite-library` 계약으로 `Assets/Art/Animation/<MonsterName>.spriteLib`를 만든다.
5. 카테고리 Idle/Move/Attack/Dead, 총 13개 label, `GetSprite` missing 0을 확인한다. `Die` 파일은 `Dead` 카테고리에 매핑한다.
6. `AssetDatabase.AssetPathToGUID`로 생성된 `.spriteLib`의 실제 GUID를 얻는다. `.meta` 문자열을 추측하거나 Addressable 그룹 YAML을 직접 편집하지 않는다.
7. Unity의 `AddressableAssetSettings` API로 그 GUID를 현재 프로젝트의 기본 로컬 그룹에 등록하고, 기본 key를 `Animation/<MonsterName>`으로 설정한다. 같은 key를 가진 다른 GUID가 있으면 의미를 비교하고 사용자 확인 없이 덮어쓰거나 이동하지 않는다.
8. 저장·Refresh 후 Addressables 설정을 다시 읽어 `entry.guid == spriteLib GUID`, `entry.address == planned key`, 동일 address의 다른 entry 0개를 확인한다.
9. `Addressables.LoadAssetAsync<SpriteLibraryAsset>(planned key)`가 성공하고 반환 에셋의 GUID가 방금 만든 `.spriteLib`와 일치하는지 확인한 뒤 handle을 release한다. 이 검증 전에는 Animation 행을 쓰지 않는다.

### C. Animation

- 새 몬스터: live Animation 탭에서 해당 ID 대역의 비어 있는 새 ID를 자동 배정하고, 몬스터 전용 Animation 행을 반드시 생성한다. 사용자에게 재사용/신규를 묻지 않는다.
- 재개 작업: 같은 몬스터 이름, 전용 SpriteLibrary Addressable key, 4개 프레임 시간이 모두 맞는 기존 Animation 행이 있으면 그 ID를 보존해 중복 행을 만들지 않는다. 다른 몬스터의 행은 재사용하지 않는다.
- 앞 단계에서 등록·검증한 Addressable key를 `SpriteLibraryPath`에 문자 하나까지 동일하게 기록한다. `Assets/Art/Animation/<MonsterName>.spriteLib` 같은 AssetDatabase 파일 경로를 쓰지 않는다. Idle/Move/Attack/Dead 프레임 시간은 모션과 비교 몬스터에 맞춘다.
- Monster.AnimationId가 이 행을 참조하고, `EntityFactory.LoadAnimationAsync()`와 동일하게 `Addressables.LoadAssetAsync<SpriteLibraryAsset>(Animation.SpriteLibraryPath)`가 성공하는지 검증한다.

### D. 기본 AI, Skill, Projectile

- 근거리형이면 `Monster.AiTableId=1`, 원거리형이면 `Monster.AiTableId=2`로 정한다.
- live Sheet와 로컬 bytes에서 AI 1의 BehaviorType이 `Melee`, AI 2의 BehaviorType이 `Ranged`인지 확인한다. 기준이 다르면 임의 수정하거나 대체 ID를 고르지 말고 중단·보고한다.
- 선택한 AI의 SkillId1~3과 Weight1~3을 그대로 사용한다. 별도 Skill 행을 만들거나 AI 공유 행을 수정하지 않는다.
- 양수 SkillId는 모두 존재해야 하며 양수 Weight와 일관돼야 한다. `EntityFactory`가 공격 가능 거리를 `SkillId1.SkillRangeMax`에서 계산하므로 SkillId1이 유효한 주 공격인지 확인한다.
- 각 Skill의 ProjectileId를 그대로 사용한다. 양수이면 해당 Projectile 행과 PrefabKey Addressable이 존재해야 하고, 0이면 즉발/비투사체로 인정한다. 별도 Projectile 행이나 시각 프리팹을 만들지 않는다.
- 현재 기준선은 AI 1이 Skill 2와 5, AI 2가 Skill 4를 참조하고 Skill 4가 Projectile 1을 참조한다. 이 숫자는 쓰기 전 live 데이터로 다시 확인하며 AI 행 자체를 정본으로 삼는다.

### E. 기본 Drop

- `Monster.DropId=1`을 사용한다.
- live Sheet와 로컬 bytes에 Drop 1이 존재하고 Currency/Equipment pool 참조와 가중치가 유효한지 확인한다.
- Drop 1을 수정하거나 새 Drop 행을 만들지 않는다. 사용자가 다른 Drop 또는 고유 전리품을 명시적으로 요청했을 때만 별도 범위로 처리한다.

### F. 공용 Entity Prefab

- 몬스터별 전용 Prefab을 만들지 않는다.
- 현행 `EntityFactory.CreateMonster`가 공용 Addressable `Prefabs/Entity`를 생성하는지, 해당 키가 실제 에셋을 resolve하는지 확인한다.
- Monster.PrefabName은 기존 Monster 행과 같은 호환 값 `temp`를 기록한다. 현행 런타임은 이 필드를 소비하지 않으며 외형은 Animation.SpriteLibraryPath로 로드한 SpriteLibrary가 담당한다.
- 이 기본 흐름에서는 `EntityFactory`를 변경하지 않는다.

### G. Monster와 Stat

`manage-monster-tables` 계약을 그대로 사용한다.

- `Monster.Stat == Stat.Id == Monster.Id`
- AnimationId는 이 몬스터와 함께 자동 생성·검증한 전용 Animation 행의 ID여야 하며 0일 수 없다. AiTableId는 역할에 따라 1 또는 2, DropId는 1이어야 한다.
- Biome `Any`는 모든 바이옴 공용 풀이고 특정 enum은 전용 풀이다.
- 체력·피해·이동속도·저항은 비교 몬스터와 사용자의 상대적 요구를 동시에 만족시킨다.
- MoveRadius, HitRadius, HitOffsetY는 실제 스프라이트 실루엣과 접지점에 맞춘다.
- 기존 런타임이 소비하지 않는 필드도 소비되는 것처럼 보고하지 않는다.
- `Monster.WeaponId`처럼 시트와 파서에는 있지만 현행 `CreateMonster`에서 소비되지 않는 필드는 기존 호환 값을 보존하고 비활성 상태를 보고한다. 사용자가 실제 무기 동작을 명시적으로 요구하면 별도 런타임 구현 범위로 처리한다.

### H. Unity 동기화

변경한 테이블만 아래 의존 순서로 다운로드한다.

1. Animation
2. Stat
3. Monster

Unity 메뉴의 현행 `ARPG/Download Table/<TableName>` 경로를 사용하고, 메뉴 이름은 코드에서 다시 확인한다. AI/Skill/Projectile/Drop은 Sheet 행을 바꾸지 않으므로 새로 다운로드하지 않되, 로컬 bytes에 선택된 기본 참조가 없거나 live Sheet와 어긋나면 해당 기존 테이블을 동기화하고 다시 검증한다. 다운로드 후 `.bytes`를 읽어 새 ID와 핵심 필드가 정확히 들어왔는지 확인한다.

## 4. 검증 체크리스트

### 에셋

- PNG 13/13, 각 128×128, 투명 배경, 요구된 파일명
- Sprite import 정상
- `.spriteLib` 카테고리/label 13개, missing 0
- `.spriteLib` GUID가 Addressables에 등록되고 key가 유일함
- Animation.SpriteLibraryPath와 Addressable key가 정확히 같으며 그 key의 `LoadAssetAsync<SpriteLibraryAsset>` 성공
- 모든 Addressable 키가 실제 에셋을 resolve

### 데이터 참조

- Monster -> Stat 존재, 동일 ID
- Monster -> Animation 존재
- Monster -> 역할에 맞는 AI 1 또는 2
- Monster -> Drop 1
- AI -> 각 양수 SkillId 존재
- Skill -> Projectile 존재 또는 명시적 0
- Projectile -> PrefabKey resolve
- 공용 `Prefabs/Entity` -> Addressable resolve, Monster.PrefabName은 호환 값 `temp`

### 동작

- Unity 컴파일 오류 0
- `CreateMonster`가 null/예외 없이 생성
- Idle/Move/Attack/Dead Sprite 조회 missing 0
- AI가 대상을 감지하고 선택된 기본 공격 Skill을 사용
- 공격 시 사거리, 애니메이션, 피해, Projectile/즉발 방식이 선택된 기본 AI/Skill과 일치
- 사망 시 Drop 1 정책이 적용
- Biome 스폰 풀이 Monster.Biome 의미와 일치

PlayMode를 안전하게 실행할 수 없으면 에셋·데이터·컴파일 검증까지만 완료로 표시하고 동작 검증은 미실행으로 명시한다.

## 5. 원자성, 재개, 충돌

- 각 단계는 `조사 -> 변경 -> read-back -> 검증`으로 닫는다.
- Sheet 행 쓰기 성공 후 로컬 동기화가 실패하면 Sheet와 로컬 상태가 다름을 명시하고 같은 행을 중복 생성하지 않는다.
- 같은 이름의 PNG/Library나 같은 ID 행이 이미 있고 완료 조건을 만족하면 재사용한다.
- 일부만 존재하면 올바른 참조를 보존한 채 빠진 부분만 보충한다.
- 의미가 다른 기존 자산을 삭제하거나 덮어써서 해결하지 않는다.

## 6. 완료 기준

다음이 모두 충족돼야 한 종의 몬스터가 완성된 것이다.

1. AnimationId/행은 자동 생성됐고 근거리/원거리 역할이 명시됐거나 한 번의 질문으로 확정됐다.
2. 필요한 PNG와 SpriteLibrary가 검증됐고, `.spriteLib`가 고유 Addressable key로 등록되어 새 전용 Animation.SpriteLibraryPath와 정확히 연결됐다.
3. 자동 생성한 Animation, 역할에 따른 AI 1/2와 그 Skill/Projectile, Drop 1, 공용 Entity가 실제 런타임 참조로 연결됐다.
4. Monster/Stat과 신규 Animation 행이 live Sheet에 있고 정확히 read-back 됐다.
5. 변경된 테이블이 로컬 bytes에 동기화됐다.
6. 컴파일과 가능한 범위의 생성·공격 검증 결과가 보고됐다.
