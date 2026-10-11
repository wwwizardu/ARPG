# NPC pipeline contract

이 문서는 2026-10-11 ElfScout 제작에서 확인한 연결 기준이다. 현재 코드와 live 시트가 우선이며 예시 ID를 다음 NPC에 그대로 배정하지 않는다.

## 원본 위치

- 테이블 클래스: `Assets/Scripts/Common/Tables.cs`
- 시트 등록·파서·다운로드 메뉴: `Assets/Scripts/Editor/DownloadTables.cs`
- 생성·AI 변환: `Assets/Scripts/Factory/EntityFactory.cs`
- 시작 장비: `Assets/Scripts/Npc/NpcEquipHelper.cs`
- NPC 저장 모델: `Assets/Scripts/Npc/NpcSaveData.cs`
- 생성 목록 소비: `Assets/Scripts/Manager/NpcManager.cs`
- 장비 진단: `Assets/Scripts/Editor/NpcEquipmentSetDiagnostics.cs`

## 시트와 행

현재 ARPG Tables spreadsheet ID는 `13j0_AI_6nSHHEkAHK2w9oRd-98xYYiUoP5spAv0U4TA`다. 변경 시트는 코드의 등록 정보와 실제 탭 헤더를 읽어 접지한다.

| 탭 | 현재 sheet ID | 주요 계약 |
| --- | --- | --- |
| Npc | 1460299278 | A:O, 캐릭터 종류의 정본 |
| Stat | 318209064 | A:AJ, 기존 행의 전체 스탯과 신규 MaxResist 열 유무 확인 |
| Animation | 747631090 | A:G, 고유 AnimationId와 SpriteLibrary Addressable key |
| NpcEquip | 1566085089 | A:Z, 슬롯별 ItemId/Chance 쌍 |
| Village | 441028134 | A:F, DefaultNpcList는 NpcTableId CSV |

Npc 현재 컬럼:
`Id, Name, Description, StatId, MoveRadius, HitRadius, HitOffset, PrefabName, AnimationId, JobType, EquipTableId, Ai, DropId, DropRateBonus, DropRarityBonus`.

- Npc 시트의 StatId는 JSON `Stat`로 직렬화되어 코드 `CreatureTable.StatId`에 들어간다. `CreatureTable.Stat`는 이미 해석된 StatTable이다.
- 시트 Ai는 코드 `NpcTable.AiTableId`로 들어간다.
- Npc.PrefabName은 기존 호환 값 0을 따른다. factory는 공용 `Prefabs/Entity`를 사용하므로 이 값을 캐릭터별 prefab 경로로 해석하지 않는다.
- 충돌 반경·HitOffset·Stat은 비슷한 기존 NPC를 비교해 정한다. 사용자가 밸런스를 요청하지 않았다면 임의로 강화하지 않는다.
- NpcTable에 캐릭터 성별·종족 전용 필드는 현재 없다. 외형과 설명으로 표현하고, 새 런타임 성별·종족 기능을 구현한 것처럼 보고하지 않는다.
- JobType은 현행 enum 중 선택한다. Hunter 같은 모험가 역할을 마을의 생산·건설 직업 배정으로 해석하지 않는다.

Animation 현재 컬럼:
`Id, Name, SpriteLibraryPath, IdleFrame, MoveFrame, AttackFrame, DeadFrame`.
프레임 필드는 **개수 아닌 초 단위 프레임당 시간**이다. SpriteLibrary가 프레임 목록을 가진다.

NpcEquip 슬롯은 WeaponRight, WeaponLeft, Helmet, Armor, Gloves, Boots, Necklace, RingRight, RingLeft, Belt, EarringRight, EarringLeft와 각 Chance다.
ItemId 0은 빈 슬롯이고 Chance는 0–100이다. 중요한 무기는 100%로 보장할 수 있다. 장비 슬롯 호환성과 해당 Item의 BasicItem 변형 존재를 확인한다.

live ID 전역 중복과 대상 범위의 기존 값을 확인한다. append 위치를 이름이나 마지막 행 번호만으로 가정하지 않는다. 같은 spreadsheet의 writes/downloads는 순차 실행한다. 기존 유사 행의 서식·검증을 복사한 뒤 필요한 값만 갱신하는 방식이 적합하다.

## 기본 AI·장비

- 현재 AI 1은 근거리, AI 2는 원거리다. live AI→Skill→Projectile 연결을 읽고 유효성을 검증한다.
- `EntityFactory.CreateNpc`는 NpcTag를 먼저 붙이고 중립 진영을 명시하며 NPC 행동을 Patrol/PatrolRanged로 변환한다.
- 예시 ElfScout는 AI 2→Skill 4→Projectile 1을 사용했다. Projectile prefab은 기존 `Projectile/Projectile_Test`였으며 새로운 화살 외형을 만들지는 않았다. 이미지 속 무기만 바뀌었다고 발사체 외형이 자동 변경된다고 말하지 않는다.
- `NpcSaveData(int npcTableId, Vector2 position)`로 저장 데이터를 만들고 `NpcEquipHelper.SeedInitialEquipment(saveData, equipTableId)` 후 factory에 넘기면 초기 장비 modifier가 적용된다. 매번 장비를 다시 뽑거나 기존 저장 장비를 교체하지 않는다.
- `CreateNpc(int npcTableId, Vector3 position, Transform parent = null, int savedEntityId = -1, NpcSaveData saveData = null)`는 `UniTask<(entityId, entity)>`를 반환한다. 테이블 초기화와 SpriteAnimation LoadState.Loaded까지 기다린 후 검사한다.
- 실제 생성에서 NpcTag, Neutral 진영, 예상 AI, AnimationId, SpriteLibrary 로드, 중요한 시작 무기를 확인한다. 전투 캐릭터는 실제 공격 상태/발사체/대미지를 확인한다.

## ElfScout 기준 예시

현재 생성된 예시는 Npc/Stat 3006, Animation 13, NpcEquip 6이다. **새 ID의 기본값이 아니다.**

- Npc: Name ElfScout, Description “활을 사용하는 여성 엘프 정찰꾼”, StatId 3006, MoveRadius 0.22, HitRadius 0.3, HitOffset 0.5, PrefabName 0, AnimationId 13, JobType Hunter, EquipTableId 6, Ai 2, DropId/bonus 0.
- Animation: key `Animation/ElfScout`, IdleFrame 0.2, MoveFrame 0.1, AttackFrame 0.1, DeadFrame 0.15.
- Stat: Type Npc, Class ElfScout, 기본 수치는 NPC 3003을 비교해 복사했다.
- NpcEquip: WeaponRight Item 22(목제 단궁) 100%, Armor 12·Gloves 13·Boots 14·Belt 15 각각 70%, 나머지는 비웠다.
- Village의 대상 기존 목록에 3006을 추가했다. 이미 저장된 인물의 종류를 교체하지 않았다.
- production은 `Assets/Art/Sprites/Npc/ElfScout/`, 예전 품질 참고는 별도 `Assets/Art/Sprites/Npc/ElfScoutReferences/`에 있다.

## 이미지 도구와 알파 검증

프로젝트 기존 스크립트를 재사용한다:

```text
python <imagegen-skill>/scripts/remove_chroma_key.py --input <raw.png> --out <alpha.png> --auto-key border --soft-matte --transparent-threshold 12 --opaque-threshold 220 --despill
python <repo>/.agents/skills/generate-monster-sprites/scripts/normalize_sprite_set.py --input-dir <alpha-dir> --output-dir <repo>/Assets/Art/Sprites/Npc/<NpcName> --base-name <NpcName>
python <repo>/.agents/skills/generate-monster-sprites/scripts/validate_sprite_set.py --input-dir <repo>/Assets/Art/Sprites/Npc/<NpcName> --base-name <NpcName> --key-color <used-key-color>
```

installed imagegen 위치와 Pillow가 있는 Python은 현재 환경에서 찾는다. 특정 사용자 홈이나 드라이브를 고정하지 않는다.

ElfScout 생성 중 제거된 배경의 미세 알파가 이미지 끝에 남아 정규화 bbox가 크게 잡힌 사례가 있었다. 해당 조건이면 알파 중간 파일에서 alpha ≤ 32인 배경 잔여물을 제거하고 세트 전체를 다시 정규화한다. 이 기준을 반투명 효과가 필요한 새 캐릭터에 무조건 적용하지 않는다. 키 색 누출·발 위치·사망 포즈 크기는 최종 이미지로 확인한다.

## 동기화와 런타임 검증

- 메뉴는 `ARPG/Download Table/{TableName}`이고 출력은 `Assets/_BinaryData/TableData/{TableName}.bytes`다. AnimationTable, StatTable, NpcEquipTable, NpcTable과 실제로 변경한 VillageTable만 다운로드한다.
- 다운로드 메뉴가 비동기이므로 “호출 성공”만으로 완료를 판단하지 않는다. 각 파일의 목표 ID와 실제 필드/목록이 새 값으로 바뀐 것을 확인한 후 다음 다운로드를 진행한다.
- 장비 진단 메뉴: `ARPG/Npc/Run Equipment Set Diagnostics`.
- 실제 플레이 검증은 기존 진단 도구와 테스트 저장 경로를 사용한다. `DataManager.DiagnosticSavePath`를 사용할 경우 DataManager가 실제 세이브를 읽기 **전에** 경로를 설정해야 한다. 저장되지 않은 Scene이나 사용자 세이브를 덮어쓰지 않는다.
- 중지/cleanup 전에 비동기 저장 완료를 기다린다. 이번 작업의 임시 C#·meta·테스트 저장 폴더는 검사 후 제거한다.
- 현행 `System_HpCheck`는 사망 때 DestroyTag를 추가하고 `System_EntityDestroy`가 즉시 제거한다. 2026-10-11 검증에서는 NPC의 Dead 전체 재생이 관찰되지 않았다. 등록된 Dead 프레임 검증과 실제 Dead 재생 검증을 분리하고 현재 코드가 그대로면 제한을 보고한다.
