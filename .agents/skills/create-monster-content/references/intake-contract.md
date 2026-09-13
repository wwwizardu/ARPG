# Monster intake contract

이 문서는 쓰기 전에 근거리/원거리 역할 하나만 확정하기 위한 계약이다. 새 몬스터의 Animation은 자동 생성하고 AI·Skill·Projectile·Drop·Prefab은 프로젝트 기본 연결을 사용한다.

## 1. 먼저 인정할 수 있는 명시 정보

대화 전체에서 다음과 같이 전투 역할을 알 수 있으면 다시 묻지 않는다.

- "늪 두꺼비, 원거리, 독 공격, 모든 바이옴"
- "근거리 몬스터"
- "원거리 공격을 하는 몬스터"
- "AI 1 사용" 또는 "AI 2 사용"

"두꺼비니까 알아서", "전체 만들어줘", "보통 몬스터처럼"처럼 사거리를 알 수 없는 경우에만 근거리/원거리를 묻는다.

## 2. 질문 전 읽기 전용 추천 조사

1. 이름·전투 역할·크기·속성·바이옴이 가장 가까운 기존 Monster/Stat/Animation 행을 찾는다. Animation 비교 행은 프레임 시간 산정에만 사용하고 새 몬스터가 그 ID를 재사용하지 않는다.
2. live AI 1과 AI 2를 읽어 각각 Melee/Ranged인지, 참조 Skill과 Projectile이 모두 존재하는지 확인한다. 공유 행은 수정하지 않는다.
3. 새 Monster 행과 함께 전용 Animation 행과 ID를 자동 생성한다. `.spriteLib` 파일 경로와 별도로 등록할 Addressable key를 자동 계획에 표시하되 선택 질문으로 만들지 않는다. 기존 몬스터를 재개할 때만 그 몬스터의 올바른 Animation 행을 검증 후 보존한다.
4. Drop 1 행이 존재하고 참조가 유효한지 확인한다.
5. 현재 `EntityFactory.CreateMonster`가 공용 Addressable `Prefabs/Entity`를 생성하고 `MonsterTable.PrefabName`을 사용하지 않는지 코드로 다시 확인한다.

## 3. 자동 기본 연결

AnimationId는 사용자 결정이 아니다. 새 몬스터마다 비어 있는 새 ID를 자동 배정하고, 전용 SpriteLibrary Addressable key와 프레임 시간을 가진 Animation 행을 함께 만든다.

| 항목 | 기본값 | 불변 조건 |
| --- | --- | --- |
| Animation | 새 전용 ID/행 자동 생성 | `.spriteLib`가 `Animation/<MonsterName>` key로 등록되고 SpriteLibraryPath가 그 key와 같아야 한다. |
| 근거리 | AI 1 | AI 1의 BehaviorType은 Melee이고 참조 Skill/Projectile이 모두 존재해야 한다. |
| 원거리 | AI 2 | AI 2의 BehaviorType은 Ranged이고 참조 Skill/Projectile이 모두 존재해야 한다. |
| Skill | 선택된 AI의 SkillId1~3 그대로 사용 | 별도 Skill 행을 만들거나 공유 행을 수정하지 않는다. |
| Projectile | 선택된 Skill의 ProjectileId 그대로 사용 | 양수 ID이면 Projectile 행과 PrefabKey가 유효해야 한다. |
| Drop | Drop 1 | 별도 Drop 행을 만들거나 Drop 1을 수정하지 않는다. |
| Monster Prefab | 전용 없음, 공용 `Prefabs/Entity` 사용 | Monster.PrefabName은 호환 값 `temp`. 외형은 Animation SpriteLibrary로 구성한다. |

## 4. 질문 형식

전투 역할이 미확정일 때만 아래 한 문장을 묻는다.

```text
이 몬스터는 근거리형인가요, 원거리형인가요?
```

답을 받으면 선택된 기본 연결을 간단히 알리고 바로 실행한다. AI/Skill/Projectile/Drop/Prefab에 대한 별도 선택 질문을 이어서 하지 않는다.

## 5. 기본 연결이 깨진 경우

AI 1/2의 역할이 예상과 다르거나, Skill/Projectile 참조, Drop 1, 공용 Entity가 없으면 다른 ID를 임의 선택하거나 기본 공유 행을 수정하지 않는다. 이는 콘텐츠 선택 질문이 아니라 프로젝트 기준선 불일치이므로 구체적인 검증 결과와 함께 중단·보고한다. 동일 이름 에셋을 덮어써야 하는 경우처럼 안전상 승인이 필요한 상황은 별도로 질문할 수 있다.

## 6. 명시적 커스텀 요청

사용자가 새 AI, 새 공격 Skill, 새 Projectile, 다른 Drop 또는 전용 Prefab을 명시적으로 요청한 경우에는 그 요청을 우선한다. 하지만 아무 말이 없으면 항상 이 문서의 기본값을 사용하며 선택지를 추가로 묻지 않는다. 관련 없는 파일 변경, 기존 의미 있는 데이터 덮어쓰기, 계정 권한 확대는 기본 생성 범위에 포함되지 않는다.
