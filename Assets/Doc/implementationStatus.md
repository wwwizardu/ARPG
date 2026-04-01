# 구현 현황 및 우선순위 (Implementation Status & Roadmap)

**Last Updated**: 2026-04-01
**Project**: ARPG with Village Simulation
**Engine**: Unity 6000.0.42f1 (LTS)

---

## 목차
1. [전체 구현 현황](#1-전체-구현-현황)
2. [시스템별 상세 현황](#2-시스템별-상세-현황)
3. [개발 로드맵](#3-개발-로드맵)
4. [우선순위 분석](#4-우선순위-분석)
5. [다음 작업 리스트](#5-다음-작업-리스트)

---

## 1. 전체 구현 현황

### 진행도 요약 (2026-04-01 기준)
```
전체 진행도: ████████████░░░░░░░░░░░░░░░░░░ 35%

핵심 시스템: ███████████████████░░░░░░░░░░░ 60%
콘텐츠:      ████████░░░░░░░░░░░░░░░░░░░░░░ 25%
UI/UX:       ██████████████░░░░░░░░░░░░░░░░ 45%
```

### 구현 상태 분류
| 상태 | 개수 | 비율 |
|------|------|------|
| ✅ 완료 | 12개 시스템 | 35% |
| 🚧 진행 중 | 8개 시스템 | 25% |
| ❌ 미구현 | 14개 시스템 | 40% |

---

## 2. 시스템별 상세 현황

### ✅ 완료된 시스템 (12개)

#### 1. ECS 아키텍처 ✅
- [X] Component (struct 기반)
- [X] System (ISystem 인터페이스)
- [X] SparseSet (O(1) 접근)
- [X] ComponentManager
- [X] SystemManager
- [X] EntityIdHelper (결정적 ID)
- **파일**: `ComponentManager.cs`, `SystemManager.cs`, `SparseSet.cs`
- **상태**: 완성, 프로덕션 레디

#### 2. 맵 시스템 ✅
- [X] 절차적 생성 (Perlin Noise)
- [X] 청크 로딩/언로딩
- [X] 고정 맵 오버레이 (BaseTown.bytes)
- [X] 몬스터 스폰 위치 생성
- [X] 타일맵 렌더링
- **파일**: `MapManager_Generator.cs`, `MapChunkData.cs`
- **상태**: 완성, 오픈월드 준비됨

#### 3. 입력 시스템 ✅
- [X] Unity Input System 통합
- [X] InputComponent
- [X] System_Input (키보드/마우스)
- [X] 이동/공격/상호작용 입력
- **파일**: `System_Input.cs`, `InputComponent.cs`, `InputSystem_Actions.inputactions`
- **상태**: 완성

#### 4. 이동 시스템 ✅
- [X] System_Move
- [X] MovementComponent
- [X] VelocityComponent
- [X] 8방향 이동
- **파일**: `System_Move.cs`
- **상태**: 완성

#### 5. 애니메이션 시스템 ✅
- [X] System_Animation
- [X] SpriteAnimationComponent
- [X] PlayableAnimator
- **파일**: `System_Animation.cs`, `PlayableAnimator.cs`
- **상태**: 완성

#### 6. 스킬 시스템 ✅
- [X] System_Skill
- [X] SkillComponent (결정적 ID)
- [X] SkillStateComponent (페이즈 관리)
- [X] SkillTimingComponent
- [X] SkillCommandComponent
- [X] WindUp → Active → Recovery 페이즈
- **파일**: `System_Skill.cs`, `SkillComponent.cs`
- **상태**: 완성, 전투 시스템 기반 완료

#### 7. 버프 시스템 ✅
- [X] System_BuffUpdate
- [X] BuffInstance (StackCount 지원)
- [X] StatModifierComponent
- [X] BuffSystem 유틸리티
- [X] BuffEntityIdHelper (결정적 ID)
- [X] 자동 만료, 중복 스택
- **파일**: `System_BuffUpdate.cs`, `BuffInstance.cs`, `BuffSystem.cs`
- **문서**: `BUFF_SYSTEM_IMPLEMENTATION.md`
- **상태**: 완성, v3.0 (StackCount 방식)

#### 8. 스탯 시스템 ✅
- [X] StatComponent
- [X] System_StatCalculation
- [X] StatModifierBuffer
- [X] HpDirtyTag, StatDirtyTag
- [X] 스탯 재계산 로직
- **파일**: `System_StatCalculation.cs`, `StatComponent.cs`
- **상태**: 완성

#### 9. 아이템 시스템 ✅
- [X] ItemData
- [X] ItemManager
- [X] WorldItemComponent (드랍 아이템)
- [X] DropComponent
- [X] 인벤토리 UI (UIInventory)
- [X] 장비 UI (UICharacter)
- **파일**: `ItemManager.cs`, `ItemData.cs`
- **상태**: 완성

#### 10. 시간 시스템 ✅
- [X] System_Time
- [X] TimeManager
- [X] 게임 내 시간 (시/분/초)
- [X] 시간 배속 조절
- **파일**: `System_Time.cs`, `TimeManager.cs`
- **상태**: 완성

#### 11. 렌더링 시스템 ✅
- [X] System_Render
- [X] URP 2D 통합
- [X] 스프라이트 렌더링
- **파일**: `System_Render.cs`
- **상태**: 완성

#### 12. 치트 시스템 ✅
- [X] UICheat
- [X] 치트 명령어
- **파일**: `UICheat.cs`
- **상태**: 완성 (개발용)

---

### 🚧 진행 중인 시스템 (8개)

#### 13. 전투 시스템 🚧
- [X] 스킬 시스템 (완료)
- [X] 스탯 시스템 (완료)
- [ ] 데미지 계산 로직
- [ ] 히트 판정 (Collider2D)
- [ ] HP 감소 및 사망 처리
- [ ] VFX/사운드 통합
- **현재 진행도**: 40%
- **다음 작업**: 데미지 계산 및 히트 판정 구현
- **문서**: `combatSystem.md`

#### 14. AI 시스템 🚧
- [X] AIComponent 정의
- [X] System_AI_Perception (기본 구조)
- [X] System_AI_Behavior (기본 구조)
- [X] AICanSeeTargetTag
- [ ] AI 상태 머신 (Idle/Chase/Attack)
- [ ] AI 타겟 감지 로직
- [ ] AI 스킬 사용 로직
- **현재 진행도**: 30%
- **다음 작업**: AI 상태 머신 구현

#### 15. NPC 시스템 🚧
- [X] NpcStatComponent (8가지 성격)
- [X] NpcJobComponent
- [X] NpcScheduleComponent
- [X] NpcVillageComponent
- [X] NpcTag
- [ ] NPC 일일 루틴 로직
- [ ] NPC 직업별 행동
- [ ] NPC 자율 의사결정
- **현재 진행도**: 35%
- **다음 작업**: NPC 스케줄 로직 구현
- **파일**: `System_NpcSchedule.cs` (TODO 상태)

#### 16. 관계 시스템 🚧
- [X] RelationshipComponent (4가지 관계)
- [X] System_Relationship (기본 구조)
- [ ] 관계 변동 로직
- [ ] NPC 간 관계 (N:N)
- [ ] 관계 기반 이벤트
- **현재 진행도**: 25%
- **다음 작업**: 관계 변동 로직 구현
- **파일**: `System_Relationship.cs` (TODO 상태)

#### 17. 마을 시스템 🚧
- [X] VillageData
- [X] VillageManager
- [X] System_VillageResource (기본 구조)
- [ ] 자원 생산-가공-소비 체인
- [ ] 건설 시스템
- [ ] 마을 발전 단계 로직
- **현재 진행도**: 30%
- **다음 작업**: 자원 시스템 로직 구현
- **문서**: `townDesign.md`

#### 18. 몬스터 시스템 🚧
- [X] MonsterManager
- [X] System_MonsterSpawn
- [X] MonsterTag
- [ ] 몬스터 종류별 AI
- [ ] 몬스터 드랍 테이블
- [ ] 몬스터 레벨 스케일링
- **현재 진행도**: 35%
- **다음 작업**: 몬스터 AI 패턴 구현

#### 19. UI 시스템 🚧
- [X] UIManager
- [X] UIInventory
- [X] UICharacter
- [X] UIMainMenu
- [X] UICheat
- [X] HpBarView
- [ ] 미니맵
- [ ] 퀘스트 UI
- [ ] 대화 UI
- [ ] 파티 UI (동료)
- **현재 진행도**: 50%
- **다음 작업**: 대화 UI 구현

#### 20. 데이터 시스템 🚧
- [X] DataManager
- [X] ItemData
- [X] BuffData
- [X] PlayerData
- [X] WorldData
- [ ] 스킬 데이터 테이블
- [ ] 몬스터 데이터 테이블
- [ ] NPC 데이터 테이블
- **현재 진행도**: 40%
- **다음 작업**: 데이터 테이블 확장

---

### ❌ 미구현 시스템 (14개)

#### 21. 던전 시스템 ❌
- [ ] 절차적 던전 생성
- [ ] Room + Corridor 알고리즘
- [ ] 보스룸 배치
- [ ] 던전 입장/퇴장
- [ ] 미니맵 & 안개
- **예상 소요 시간**: 3~4주
- **문서**: `dungeonSystem.md`
- **의존성**: 맵 시스템, 전투 시스템

#### 22. 동료 시스템 ❌
- [ ] CompanionComponent
- [ ] System_CompanionAI
- [ ] 동료 영입 로직
- [ ] 동료 명령 시스템
- [ ] 동료 성장/장비
- **예상 소요 시간**: 2~3주
- **문서**: `companionSystem.md`
- **의존성**: 관계 시스템, AI 시스템

#### 23. NPC 상호작용 시스템 ❌
- [ ] InteractionComponent
- [ ] System_Interaction
- [ ] 30가지 행동 구현
- [ ] 판정 시스템
- [ ] 상호작용 UI
- **예상 소요 시간**: 3~4주
- **문서**: `interactionSystem.md`
- **의존성**: NPC 시스템, 관계 시스템

#### 24. 퀘스트 시스템 ❌
- [ ] QuestComponent
- [ ] Quest 데이터 구조
- [ ] 퀘스트 발생 로직
- [ ] 퀘스트 진행 추적
- [ ] 퀘스트 UI
- **예상 소요 시간**: 2~3주
- **의존성**: NPC 시스템

#### 25. 대화 시스템 ❌
- [ ] DialogueComponent
- [ ] 대화 트리 구조
- [ ] 대화 UI
- [ ] 선택지 분기
- [ ] LLM 통합 (선택 사항)
- **예상 소요 시간**: 2주
- **의존성**: NPC 시스템
- **참고**: `LLMManager.cs`, `LLMClient.cs` 이미 존재

#### 26. 크래프팅 시스템 ❌
- [ ] 레시피 데이터
- [ ] 크래프팅 UI
- [ ] 재료 소비 로직
- [ ] 아이템 생성
- **예상 소요 시간**: 1~2주
- **의존성**: 아이템 시스템

#### 27. 건설 시스템 ❌
- [ ] 건물 데이터
- [ ] 건설 UI
- [ ] 자원 소비
- [ ] 건물 배치
- [ ] 건물 효과
- **예상 소요 시간**: 2~3주
- **의존성**: 마을 시스템

#### 28. 이벤트 시스템 ❌
- [ ] EventComponent
- [ ] 몬스터 습격
- [ ] 자연 재해
- [ ] 랜덤 이벤트
- [ ] 이벤트 UI
- **예상 소요 시간**: 2주
- **의존성**: 마을 시스템, 전투 시스템

#### 29. 세이브/로드 시스템 ❌
- [ ] 세이브 데이터 구조
- [ ] ECS 데이터 직렬화
- [ ] 월드 상태 저장
- [ ] NPC 상태 저장
- [ ] 로드 시스템
- **예상 소요 시간**: 2~3주
- **의존성**: 모든 시스템

#### 30. 사운드 시스템 ❌
- [ ] AudioManager
- [ ] BGM 재생
- [ ] SFX 재생
- [ ] 사운드 풀링
- **예상 소요 시간**: 1주
- **의존성**: 없음

#### 31. 카메라 시스템 ❌
- [ ] 카메라 추종
- [ ] 카메라 쉐이크
- [ ] 줌 인/아웃
- **예상 소요 시간**: 1주
- **의존성**: 없음

#### 32. 튜토리얼 시스템 ❌
- [ ] 튜토리얼 시퀀스
- [ ] 가이드 UI
- [ ] 입력 힌트
- **예상 소요 시간**: 1~2주
- **의존성**: UI 시스템

#### 33. 로컬라이제이션 ❌
- [ ] 다국어 데이터
- [ ] 언어 전환
- [ ] 텍스트 치환
- **예상 소요 시간**: 1주
- **의존성**: 없음

#### 34. 최적화 ❌
- [ ] 오브젝트 풀링
- [ ] 메모리 프로파일링
- [ ] 성능 최적화
- **예상 소요 시간**: 지속적
- **의존성**: 모든 시스템

---

## 3. 개발 로드맵

### Milestone 1: 핵심 게임플레이 (8주)
**목표**: 플레이어가 오픈월드를 탐험하고 전투할 수 있음

#### Week 1-2: 전투 시스템 완성
- [ ] 데미지 계산 로직 구현
- [ ] 히트 판정 (Collider2D)
- [ ] HP 감소 및 사망 처리
- [ ] 기본 VFX (히트, 사망)
- **결과**: 플레이어가 몬스터와 전투 가능

#### Week 3-4: AI 시스템 완성
- [ ] AI 상태 머신 (Idle/Chase/Attack)
- [ ] AI 타겟 감지 로직
- [ ] AI 스킬 사용
- [ ] 몬스터 3종 AI 구현
- **결과**: 몬스터가 플레이어를 추격하고 공격

#### Week 5-6: 던전 시스템 (1차)
- [ ] 기본 던전 생성 (Room + Corridor)
- [ ] 입구/출구 배치
- [ ] 보스룸 생성
- [ ] 던전 입장/퇴장
- **결과**: 절차적 생성 던전 클리어 가능

#### Week 7-8: 보스 & 보상
- [ ] 보스 1종 구현
- [ ] 보스 AI 패턴 (3페이즈)
- [ ] 보물 상자 배치
- [ ] 보상 시스템
- **결과**: 던전 클리어 → 보상 획득

---

### Milestone 2: NPC 시스템 (6주)
**목표**: NPC와 상호작용하고 마을이 발전함

#### Week 9-10: NPC 일일 루틴
- [ ] System_NpcSchedule 구현
- [ ] NPC 직업별 행동
- [ ] 시간대별 루틴
- [ ] NPC 자율 의사결정
- **결과**: NPC가 자율적으로 행동

#### Week 11-12: 관계 시스템
- [ ] 관계 변동 로직
- [ ] NPC 간 관계 (N:N)
- [ ] 관계 UI 표시
- **결과**: NPC와의 관계 형성 가능

#### Week 13-14: 상호작용 시스템 (1차)
- [ ] InteractionComponent
- [ ] 5가지 기본 행동 (대화, 정보, 칭찬, 선물, 영입)
- [ ] 판정 시스템
- [ ] 상호작용 UI
- **결과**: NPC와 기본 상호작용 가능

---

### Milestone 3: 마을 & 동료 (6주)
**목표**: 마을 발전 및 동료 시스템

#### Week 15-16: 마을 자원 시스템
- [ ] 자원 생산-가공-소비 체인
- [ ] NPC 직업별 자원 생산
- [ ] 자원 저장소
- [ ] 자원 UI
- **결과**: 마을 자원 순환 구현

#### Week 17-18: 동료 시스템
- [ ] CompanionComponent
- [ ] 동료 영입 로직
- [ ] System_CompanionAI
- [ ] 동료 명령 시스템
- **결과**: NPC를 동료로 영입 가능

#### Week 19-20: 건설 시스템
- [ ] 건물 데이터
- [ ] 건설 UI
- [ ] 건물 배치
- [ ] 건물 효과
- **결과**: 마을에 건물 건설 가능

---

### Milestone 4: 콘텐츠 확장 (6주)
**목표**: 퀘스트, 이벤트, 크래프팅

#### Week 21-22: 퀘스트 시스템
- [ ] Quest 데이터 구조
- [ ] 퀘스트 발생 로직
- [ ] 퀘스트 진행 추적
- [ ] 퀘스트 UI
- **결과**: 퀘스트 수주 및 완료 가능

#### Week 23-24: 대화 & 이벤트 시스템
- [ ] 대화 트리 구조
- [ ] 대화 UI
- [ ] 랜덤 이벤트
- [ ] 몬스터 습격 이벤트
- **결과**: NPC와 깊은 대화 및 마을 이벤트 발생

#### Week 25-26: 크래프팅 & 세이브
- [ ] 크래프팅 시스템
- [ ] 세이브/로드 시스템
- **결과**: 아이템 제작 및 진행도 저장 가능

---

### Milestone 5: 폴리싱 (4주)
**목표**: UI/UX 개선, 최적화, 밸런싱

#### Week 27-28: UI/UX 개선
- [ ] 미니맵
- [ ] 튜토리얼
- [ ] 사운드/VFX 통합
- [ ] 카메라 개선

#### Week 29-30: 최적화 & 밸런싱
- [ ] 성능 최적화
- [ ] 게임 밸런싱
- [ ] 버그 수정
- [ ] QA 테스트

**총 예상 기간**: 30주 (약 7.5개월)

---

## 4. 우선순위 분석

### Critical Path (최우선)
```
전투 시스템 → AI 시스템 → 던전 시스템
```
**이유**: 핵심 게임플레이. 이것 없이는 게임이 성립 안 됨.

### High Priority (높은 우선순위)
```
NPC 일일 루틴 → 관계 시스템 → 상호작용 시스템 → 동료 시스템
```
**이유**: 게임의 차별점. 림월드 스타일 NPC 시스템.

### Medium Priority (중간 우선순위)
```
마을 자원 시스템 → 퀘스트 시스템 → 대화 시스템
```
**이유**: 게임 깊이를 더하는 요소.

### Low Priority (낮은 우선순위)
```
크래프팅 → 건설 시스템 → 이벤트 시스템
```
**이유**: 나중에 추가 가능한 콘텐츠.

### Polish Phase (폴리싱 단계)
```
사운드 → 카메라 → 튜토리얼 → 로컬라이제이션 → 최적화
```
**이유**: 게임이 어느 정도 완성된 후 추가.

---

## 5. 다음 작업 리스트

### 🔥 이번 주 (Week 1-2)
**목표**: 전투 시스템 완성

#### Task 1: 데미지 계산 로직
- [ ] `DamageCalculator.cs` 생성
- [ ] 기본 데미지 공식 구현
- [ ] 치명타 판정
- [ ] 방어력 감소
- [ ] 회피/막기 판정
- **예상 시간**: 2일

#### Task 2: 히트 판정 시스템
- [ ] Collider2D 기반 히트박스
- [ ] `System_Skill`에 히트 체크 로직 추가
- [ ] 히트 시 데미지 적용
- **예상 시간**: 2일

#### Task 3: HP 감소 및 사망
- [ ] `System_HpCheck` 개선
- [ ] HP 감소 로직
- [ ] 사망 처리 (DestroyTag 추가)
- [ ] 플레이어 사망 시 리스폰
- **예상 시간**: 1일

#### Task 4: 기본 VFX
- [ ] 히트 이펙트 (Particle System)
- [ ] 사망 이펙트
- [ ] 스킬 이펙트 (기본 공격)
- **예상 시간**: 2일

#### Task 5: 테스트 & 밸런싱
- [ ] 테스트 씬 구성
- [ ] 전투 밸런싱 (데미지, HP)
- [ ] 버그 수정
- **예상 시간**: 1일

**총 예상**: 8일 (1주 조금 넘음)

---

### 📅 다음 주 (Week 3-4)
**목표**: AI 시스템 완성

#### Task 1: AI 상태 머신
- [ ] AIState enum 구현
- [ ] `System_AI_Behavior` 개선
- [ ] Idle → Chase → Attack 전환 로직
- **예상 시간**: 3일

#### Task 2: AI 타겟 감지
- [ ] `System_AI_Perception` 개선
- [ ] 시야 범위 내 타겟 감지
- [ ] 장애물 체크 (Raycast)
- **예상 시간**: 2일

#### Task 3: AI 스킬 사용
- [ ] AI가 스킬 사용하도록 수정
- [ ] 쿨타임 관리
- [ ] 스킬 선택 로직
- **예상 시간**: 2일

#### Task 4: 몬스터 AI 패턴
- [ ] 슬라임 (근접, 단순)
- [ ] 고블린 (근접, 그룹)
- [ ] 박쥐 (원거리, 빠름)
- **예상 시간**: 3일

---

### 🎯 이번 달 목표
1. ✅ 기획 문서 작성 (완료!)
2. 🚧 전투 시스템 완성 (진행 중)
3. ⏳ AI 시스템 완성
4. ⏳ 던전 생성 시스템 (기본)

---

## 6. 리스크 & 대응

### Risk 1: 던전 생성 알고리즘 복잡도
**리스크**: Room + Corridor 알고리즘 구현이 예상보다 복잡할 수 있음
**대응**:
- 1차: 간단한 그리드 기반 던전 생성
- 2차: 개선된 알고리즘 (BSP, Delaunay)

### Risk 2: NPC AI 성능
**리스크**: NPC 50명 이상 시 AI 업데이트 성능 저하
**대응**:
- UpdateInterval 증가 (0.5초마다)
- 시야 범위 내 NPC만 업데이트
- Job System으로 병렬화 (선택 사항)

### Risk 3: 세이브/로드 복잡도
**리스크**: ECS 데이터 직렬화가 복잡함
**대응**:
- 초기: JSON 기반 간단한 세이브
- 추후: 바이너리 직렬화로 최적화

### Risk 4: 밸런싱
**리스크**: 전투, 자원, 관계 등 밸런싱 어려움
**대응**:
- 플레이 테스트 반복
- 치트 시스템 활용
- 데이터 테이블로 쉽게 수정 가능하도록

---

## 7. 완료 조건 (Definition of Done)

### 시스템 완료 조건
- [ ] 코드 구현 완료
- [ ] 유닛 테스트 작성 (주요 로직)
- [ ] 테스트 씬에서 동작 확인
- [ ] 주요 버그 수정
- [ ] 코드 리뷰 (옵션)
- [ ] 문서 업데이트

### Milestone 완료 조건
- [ ] 모든 Task 완료
- [ ] 통합 테스트 통과
- [ ] 플레이 테스트 완료
- [ ] 치명적 버그 없음
- [ ] 다음 Milestone 준비 완료

---

## 8. 메트릭 & 추적

### 개발 속도
- **평균 시스템 구현 시간**: 1~3주
- **현재 완료 속도**: 12개 시스템 / 4개월 = 3개/월
- **예상 완료 시점**: 2026년 11월 (8개월 후)

### 코드 품질
- **총 스크립트 수**: 147개
- **평균 파일 크기**: ~200 라인
- **주요 시스템 테스트 커버리지**: 0% (TODO)

### 진행도 추적
```
2026-04-01: 35% 완료
2026-05-01: 예상 45%
2026-06-01: 예상 55%
2026-07-01: 예상 65%
...
2026-11-01: 예상 100%
```

---

## 9. 참고 자료

### 기획 문서
- [gameOverview.md](./gameOverview.md) - 전체 게임 개요
- [combatSystem.md](./combatSystem.md) - 전투 시스템
- [companionSystem.md](./companionSystem.md) - 동료 시스템
- [dungeonSystem.md](./dungeonSystem.md) - 던전 시스템
- [interactionSystem.md](./interactionSystem.md) - NPC 상호작용
- [townDesign.md](./townDesign.md) - 마을 시스템

### 기술 문서
- [CLAUDE.md](../CLAUDE.md) - 코딩 컨벤션
- [BUFF_SYSTEM_IMPLEMENTATION.md](../BUFF_SYSTEM_IMPLEMENTATION.md) - 버프 시스템 구현

---

**문서 관리**:
- 매주 금요일: 진행도 업데이트
- 새 시스템 완료 시: 상태 변경 (🚧 → ✅)
- Milestone 완료 시: 로드맵 업데이트

**마지막 업데이트**: 2026-04-01
**다음 업데이트**: 2026-04-08
