---
name: create-monster-content
description: Create or finish one ARPG monster end to end by orchestrating monster sprites, Unity SpriteLibrary, Animation, built-in melee/ranged AI, Monster/Stat data, table sync, and runtime verification. Use when the user asks to make a complete monster in one pass, says "몬스터를 한 번에 만들어줘", "몬스터 전체 연결", "스프라이트부터 테이블까지", or invokes $create-monster-content. Automatically create a dedicated Animation row and AnimationId, use AI 1 for melee or AI 2 for ranged with their existing Skill and Projectile references, use Drop 1, and use the shared Entity prefab. If combat role is omitted, ask only whether the monster is melee or ranged. Do not use for a request explicitly limited to only sprites, SpriteLibrary, or Monster/Stat rows; use the narrower project skill instead.
---

# Create Monster Content

새 몬스터 한 종을 이미지 생성부터 런타임 참조 검증까지 하나의 재개 가능한 작업으로 완성한다. 기존 하위 스킬을 복제하지 말고 순서대로 조율한다.

## 시작 시 반드시 읽기

1. `references/intake-contract.md`를 끝까지 읽는다.
2. `references/pipeline-contract.md`를 끝까지 읽는다.
3. 프로젝트의 `AGENTS.md`와 몬스터 스폰·전투 관련 Wiki를 현재 코드와 함께 확인한다.
4. 실제로 사용할 때 아래 하위 스킬의 `SKILL.md`와 그 스킬이 요구하는 참조 파일을 각각 끝까지 읽는다.
   - `generate-monster-sprites`
   - `create-sprite-library`
   - `manage-monster-tables`
   - 연결된 Google Sheet의 Animation/AI/Skill/Projectile/Drop 탭을 직접 수정할 때 `google-drive:google-sheets`

하위 스킬이 보이지 않거나 읽히지 않으면 해당 단계의 대체 절차를 추측하지 말고, 완료 가능한 앞 단계까지만 수행한 뒤 정확한 차단 사유를 보고한다.

## 작업 경계

- 이 스킬 호출은 사용자가 지정한 몬스터 한 종과 그 종만을 위한 프로젝트 에셋, ARPG Tables 행, 필요한 런타임 연결을 생성·수정하는 요청이다.
- 기존의 관련 없는 행·에셋·코드는 변경하지 않는다.
- 같은 이름이나 ID의 기존 결과물이 있으면 먼저 소유 관계와 내용을 검사한다. 안전하게 이어갈 수 있으면 검증된 단계를 건너뛰고 재개한다. 의미가 다른 결과물을 덮어써야 하면 사용자 확인 전에는 덮어쓰지 않는다.
- 새 Monster 행을 만드는 작업은 같은 몬스터 전용 Animation 행과 새 AnimationId 생성을 항상 포함한다. AnimationId의 재사용/신규 여부를 사용자에게 묻지 않는다. 기존 몬스터를 재개할 때만, 그 몬스터 소유의 올바른 Animation 행과 Addressable 연결이 이미 검증되면 중복 생성하지 않고 보존한다.
- Animation의 `SpriteLibraryPath`는 파일 경로가 아니라 `SpriteLibraryAsset`의 Addressable key다. 생성된 `.spriteLib`를 Unity Addressables에 등록하고, 그 정확한 key를 Animation 행에 써야 한다.
- 몬스터별 Prefab은 만들지 않는다. 현행 `EntityFactory.CreateMonster`는 공용 Addressable `Prefabs/Entity`를 생성하고 Animation의 SpriteLibrary로 외형을 구성한다. `Monster.PrefabName`은 기존 호환 값 `temp`를 사용하되 런타임에서 소비되는 것처럼 표현하지 않는다.
- 사용자가 별도 커스텀 의존성을 명시하지 않았다면 AI/Skill/Projectile/Drop을 새로 만들지 않는다. 근거리면 AI 1, 원거리면 AI 2를 사용하고 해당 AI 행이 참조하는 Skill과 그 Skill이 참조하는 Projectile을 그대로 사용하며 Drop은 1을 사용한다.

## 1. 읽기 전용 사전 조사

아직 파일, Unity 에셋, Google Sheet를 수정하지 않는다.

1. 대화에서 이미 확정된 몬스터 이름, 외형, 역할, 속성, 레벨/등급, 바이옴, 체력·피해 성향, 충돌 크기를 추출한다.
2. 현재 Google Sheet와 로컬 `*Table.bytes`에서 가장 가까운 비교 몬스터를 찾아 Stat·충돌 반경·Animation 프레임 시간 산정에 사용한다.
3. 대상 이름의 스프라이트 폴더, `.spriteLib`, Animation/Monster/Stat 행, Addressable 키가 이미 있는지 검사한다.
4. 대화에서 근거리 또는 원거리 역할이 명시됐는지 확인한다. AnimationId는 자동 생성, DropId는 1, 전용 Prefab은 없음으로 확정한다.
5. live 데이터에서 AI 1이 Melee, AI 2가 Ranged인지 확인하고, 선택될 AI가 참조하는 모든 Skill과 각 Skill의 Projectile 참조가 유효한지 검증한다. Drop 1과 공용 `Prefabs/Entity`도 존재해야 한다.

사용자가 별도 커스텀 AI, Skill, Projectile, Drop 또는 전용 Prefab을 명시적으로 요청한 경우에만 그 범위를 별도로 처리한다. 단순한 외형·속성 콘셉트만으로 기본 연결을 커스텀 생성으로 확대하지 않는다.

## 2. 전투 역할 질문 게이트

근거리/원거리가 이미 명시됐으면 질문 없이 진행한다. 명시되지 않았으면 어떤 생성이나 쓰기도 시작하기 전에 다음 한 가지만 묻는다.

> 이 몬스터는 근거리형인가요, 원거리형인가요?

그 외 AnimationId, AI ID, Skill, Projectile, Drop, Prefab 선택은 묻지 않는다. 답변이 근거리면 AI 1, 원거리면 AI 2로 확정하고 `references/intake-contract.md`의 기본 연결을 적용한다. 기존 에셋을 덮어써야 하는 충돌이나 외부 권한 문제 같은 안전·권한 질문은 이 제한과 별개다.

## 3. 실행 계획 고정

답변 후 쓰기 전에 메모리 안에 한 종짜리 실행 매니페스트를 만든다.

- 몬스터 내부 이름과 표시 이름
- 모든 기존/신규 ID와 Addressable 키
- 각 단계의 예상 출력 경로 또는 Sheet 행
- 비교 기준 몬스터와 자동 산정한 수치
- 자동 생성할 AnimationId/행, 근거리/원거리 역할, 그에 따른 기본 AI/Skill/Projectile, Drop 1, 공용 Entity 연결

ID는 실제 쓰기 직전에 관련 탭 전체를 다시 읽어 중복을 확인한다. 프로젝트의 기존 ID 대역을 따르는 비어 있는 ID를 선택하고, `Monster.Id == Stat.Id` 계약을 유지한다. 사용자가 지정한 ID가 이미 다른 의미로 사용 중이면 자동 덮어쓰지 않는다.

## 4. 전체 파이프라인 실행

`references/pipeline-contract.md`의 순서와 완료 조건을 따른다.

1. `generate-monster-sprites`로 13개 PNG를 생성·검증한다.
2. Unity import 설정을 검증하고 `create-sprite-library`로 `.spriteLib`를 생성해 `GetSprite` missing 0을 확인한다. 이어서 해당 에셋을 Unity Addressables에 고유 key로 등록하고 그 key로 실제 로드되는지 검증한다.
3. 새 AnimationId를 자동 배정하고, Animation 행의 `SpriteLibraryPath`에는 앞 단계에서 검증한 Addressable key를 정확히 기록한다.
4. 근거리면 AI 1, 원거리면 AI 2를 선택한다. AI가 가진 Skill과 Skill이 가진 Projectile 참조를 그대로 검증·사용하고 DropId는 1, PrefabName은 호환 값 `temp`로 확정한다. 별도 AI/Skill/Projectile/Drop/Prefab 에셋이나 행은 만들지 않는다.
5. `manage-monster-tables`로 Monster와 Stat 행을 만들거나 갱신한다. 모든 외래 키는 앞 단계에서 검증한 기본 연결을 사용한다.
6. 변경된 테이블만 Unity로 다운로드하고 컴파일·참조·에셋 로드를 검증한다.
7. 안전한 기존 진단 경로가 있으면 몬스터 생성과 공격까지 실행 검증한다. 저장되지 않은 Scene 작업을 덮어쓰거나 새 테스트용 Unity 프로젝트를 만들지 않는다.
8. 모든 필수 산출물과 참조 검증이 완료되면 해당 몬스터 작업에서 만든 `tmp/imagegen/<monster-kebab>/`와 기타 작업 전용 임시 파일을 정리한다. 삭제 전에 정확한 대상이 현재 저장소 안의 해당 작업 경로인지 검증하고, 다른 몬스터나 기존 사용자 파일은 삭제하지 않는다.

각 단계가 이미 존재하더라도 이름만 보고 건너뛰지 않는다. 그 단계의 완료 조건을 다시 만족할 때만 완료로 표시한다.

## 5. 중단과 재개

- 이미지 생성 도구가 없으면 PNG 생성 전에 중단한다.
- Unity 연결이 없으면 로컬에서 안전하게 검증 가능한 작업까지만 수행하고 SpriteLibrary, Prefab, import, 런타임 검증을 완료했다고 표시하지 않는다.
- Google Sheet 연결이 없으면 로컬 에셋 단계까지만 수행하고 행과 로컬 bytes가 동기화됐다고 표시하지 않는다.
- 중단 후 재실행할 때는 완료 조건을 만족한 결과를 재사용하고 첫 미완료 단계부터 계속한다.
- 중단되거나 완료 검증에 실패하면 재개에 필요한 작업 전용 임시 파일만 보존하고 그 경로를 보고한다. 완료로 판정한 작업의 임시 파일은 남기지 않는다.
- 일부 단계 실패를 전체 완료로 표현하지 않는다. 생성됨, 연결됨, 검증됨을 구분한다.

## 6. 완료 보고

결과는 간결하게 다음을 보고한다.

- 몬스터 이름과 Monster/Stat ID
- Animation ID, 근거리/원거리 역할, 선택된 AI와 그 AI의 Skill/Projectile ID, Drop ID
- 생성한 스프라이트 폴더, `.spriteLib` 경로와 SpriteLibrary Addressable key, 공용 `Prefabs/Entity` 사용 여부
- Google Sheet 갱신 탭과 Unity로 동기화한 테이블
- 검증 결과: PNG 세트, SpriteLibrary missing 수, 참조 무결성, 컴파일, 런타임 생성/공격
- 작업 전용 임시 파일 정리 결과(미완료로 보존했다면 정확한 경로와 사유)
- 자동 산정한 중요한 수치와 비교 기준
- 실행하지 못한 검증과 정확한 이유

모든 필수 산출물과 참조 검증이 끝난 경우에만 완료라고 말한다.
