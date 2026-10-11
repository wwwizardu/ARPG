---
name: create-npc-content
description: 'ARPG에서 NPC를 만들어줘, 엔피씨 생성, NPC 전체 연결 요청 시 도트 애니메이션부터 Unity SpriteLibrary·Addressables·Npc/Stat/Animation/NpcEquip 테이블까지 완성한다. 승인된 ElfScout·DesertScorpion 수준의 품질을 기본으로 사용한다. NPC 아이디어 추천이나 기존 테이블 수치만 수정하는 요청에는 사용하지 않는다.'
---

# Create NPC Content

한 종류의 NPC를 몬스터와 같은 이미지·애니메이션 파이프라인으로 제작하고, 캐릭터 데이터는 **Npc 테이블**에 연결한다. 기본 결과는 128×128 투명 PNG 13장과 게임에서 로드되는 SpriteLibrary, 연결된 테이블이다.

## 범위와 기준

- 현재 대화의 외형·품질·성별·종족·역할·장비·프레임 수 선택을 우선한다. 기본 품질은 승인된 `Assets/Art/Sprites/Npc/ElfScout/ElfScout_Idle1.png`와 `Assets/Art/Sprites/Monster/DesertScorpion/DesertScorpion_Idle1.png`의 도트 밀도다. 고해상도 일러스트 수준으로 올리지 않는다.
- 이름과 역할로 외형·공격 방식을 합리적으로 정한다. 활 정찰꾼이면 원거리로 진행한다. 전투 방식을 결정할 근거가 없을 때만 필요한 역할을 묻는다.
- 사용자가 “대기 이미지 한 장만”, “퀄리티 체크”를 요청하면 **Idle1 한 장만** 생성·정규화해 보여준다. 전체 제작 요청이나 승인 후에는 그 이미지를 정체성 기준으로 재사용한다.
- 같은 NPC의 기존 결과는 검사 후 이어서 사용한다. 다른 의미의 에셋·행을 덮어쓰거나 기존 세이브의 NPC들을 교체하지 않는다.
- 몬스터 스킬의 Monster 폴더·Monster 행·Drop 1 기본값을 NPC에 적용하지 않는다. 스프라이트 경로는 `Assets/Art/Sprites/Npc/{NpcName}/`다.

## 이미지 제작

이미지 작업 전에 현재 카탈로그의 `imagegen` 스킬과 프로젝트 [generate-monster-sprites](../generate-monster-sprites/SKILL.md), 그 스킬의 sprite-spec을 읽는다. 생성·배경 제거·정규화 도구를 재사용하되 이 스킬의 NPC 경로·도트 품질·PPU 기본값을 적용한다.

1. PascalCase 내부 이름과 목적 폴더를 정하고, 승인된 Idle1 또는 위 기준 이미지를 이미지 뷰어로 확인한다. ElfScout는 품질·크기 기준이며 다른 NPC에 엘프 외형을 강제하지 않는다.
2. 개별 파일 `{NpcName}_Idle1..2.png`, `Move1..4.png`, `Attack1..4.png`, `Die1..3.png`를 만든다. 한 파일당 한 이미지 생성 호출을 사용한다. 기준 Idle1과 적절한 기존 동작 이미지를 참조해 인물·의상·팔레트·크기를 유지한다.
3. 좌측을 보는 고정 측면/3/4 시점, 도트 덩어리가 읽히는 간단한 명암, 잘리지 않은 전신을 유지한다. 공격은 캐릭터의 무기·역할에 맞춘 준비→공격→회복 흐름으로 만든다.
4. 제거 가능한 단색 배경을 사용한다. 녹색 의상이 있으면 마젠타 `#ff00ff`, 그 외에는 피사체와 겹치지 않는 키 색을 고른다. 바닥·그림자·문자·잔상은 넣지 않는다.
5. 원본과 알파 중간 파일은 `tmp/imagegen/{npc-kebab}/`에 보관한다. imagegen 배경 제거 도구를 사용하고, 희미한 배경 잔여물이 bbox를 늘리지 않는지 확인한 다음 세트 전체를 같은 배율로 정규화한다.
6. 최종 128×128 RGBA, 안정된 발 위치(행 125–126), 일관된 캐릭터 크기와 카메라, 투명 배경을 확인한다. 기존 정규화·검증 스크립트에 NPC 폴더와 base-name을 명시한다. 기본 인간형의 기준 Idle 높이는 68px이며 체형·요청에 맞게 조정할 수 있다.
7. 애니메이션 미리보기로 인물 일관성·걷기 교차·공격 순서·죽음 포즈를 확인하고 불량 프레임만 다시 만든다. 요청된 검토용 미리보기와 실제 사용한 프롬프트는 `output/imagegen/`에 보존할 수 있다.

## Unity 연결

이 단계에서는 [create-sprite-library](../create-sprite-library/SKILL.md)를 읽고 에디터 안에서 실행한다.

- 기본 import는 Single Sprite, **64 PPU**, bottom-center pivot, FullRect, mipmaps off, alpha-is-transparency on, uncompressed다. 기존 승인된 NPC의 필터 설정을 비교하고, 새 요청이 없으면 ElfScout의 Bilinear를 따른다.
- 완성 폴더에는 최종 프레임만 둔다. 품질 비교·기존 버전은 폴더 밖에 보관해 재귀 스캔의 중복 라벨을 막는다.
- `Assets/Art/Animation/{NpcName}.spriteLib`를 생성한다. Idle/Move/Attack/Dead 카테고리를 사용하고 Die 파일을 Dead 라벨로 매핑한다.
- 실제 임포트된 Sprite의 GUID/fileID로 참조를 만들고 전체 `GetSprite`가 missing 0인지 확인한다.
- 고유 Addressable key `Animation/{NpcName}`를 등록하고 해당 key로 SpriteLibraryAsset을 실제 로드한다. 공용 `Prefabs/Entity`를 사용한다.
- AnimatorController·AnimationClip·캐릭터별 Entity prefab은 이 ECS 애니메이션 방식에 필요하지 않으므로 별도 요청 없이 추가하지 않는다.

## NPC 테이블과 게임 연결

쓰기 전에 [references/npc-pipeline.md](references/npc-pipeline.md)를 읽고 현행 코드·live 헤더와 대조한다. `AGENTS.md`, `Wiki/npc.md`를 확인하고 마을 생성 목록을 다룰 때는 `Wiki/village.md`도 읽는다. 연결 시트 작업에는 현재 카탈로그의 `google-drive:google-sheets` 스킬을 사용한다.

1. live Npc·Stat·Animation·NpcEquip과 선택된 AI/Skill/Projectile/Item/BasicItem 참조를 조사한다. 기존 NPC 중 같은 역할을 비교 기준으로 삼고 신규 ID를 쓰기 직전에 다시 확인한다.
2. 새 Npc와 전용 Animation·Stat 행을 연결한다. Npc ID와 Stat ID는 같은 새 ID를 사용하고 Stat.Type은 Npc다. Animation.SpriteLibraryPath는 검증한 Addressable key다.
3. 기존 근거리 AI 1 또는 원거리 AI 2와 그 AI가 참조하는 Skill/Projectile을 검증해 사용한다. 요청 없이 새 AI·Skill·Projectile을 만들지 않는다. NPC DropId는 기존 NPC 기준인 0을 사용한다.
4. 캐릭터의 장비를 NpcEquip에 연결한다. 맞는 기존 세트를 재사용할 수 있고, 별도 구성이라면 새 세트를 만든다. 일반 NPC 시작 장비는 상점용 BasicItem 변형이 있는 기존 Item을 사용한다.
5. 게임 내 등장까지 요청된 범위라면 대상 마을의 DefaultNpcList/현재 생성 경로를 확인하고 새 NPC를 중복 없이 추가한다. 모든 마을 목록을 일괄 수정하거나 이미 저장된 NPC 인구를 재작성하지 않는다.
6. Sheet 형식·검증을 보존하고 정확한 관련 범위만 순차적으로 쓴 뒤 읽어 확인한다. 변경한 테이블만 Unity로 다운로드하고 실제 bytes 값까지 확인한다.

## 완료 조건과 재개

- PNG·SpriteLibrary·Addressable·테이블·컴파일·실제 NPC 생성/공격을 각각 검증한다. 기존 NPC 장비 진단도 실행한다. 저장 경로를 격리한 기존 진단 경로를 우선하고, 별도 Unity 프로젝트는 만들지 않는다.
- 사망 프레임 등록과 전체 사망 애니메이션 재생을 구분한다. 현재 즉시 제거 흐름으로 재생되지 않으면 그 제한을 보고하고, 에셋 제작만으로 런타임이 수정됐다고 말하지 않는다.
- Unity 또는 Sheet 연결이 끊기면 가능한 단계까지 수행하고 미완료 단계와 재개에 필요한 파일을 보고한다. 기존 대화의 승인을 다시 요구하지 않는다.
- 완료 후 이번 작업의 임시 파일·검증 코드·테스트 세이브만 정리한다. 미리보기·최종 프레임·프롬프트는 산출물로 보존한다.
- 최종 보고는 NPC 이름/ID, 프레임 수, SpriteLibrary와 Addressable key, 수정·동기화한 탭, 실제 검증 결과와 남은 제한을 간결하게 제시한다.
