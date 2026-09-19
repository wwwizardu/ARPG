# 던전·저장 진단

반복해서 확인할 가치가 있는 무거운 검증은 Unity Editor 메뉴
`ARPG > Dungeon > Run Storage and Generation Diagnostics`에서 실행한다.
실제 검사 코드는 `Assets/Scripts/Editor/DungeonRuntimeDiagnostics.cs`에 있다.

진단 범위:

- 던전 테이블의 구조적 유효성
- 슬롯 및 방 생성의 결정성, 연결성, JSON 왕복
- SparseSet 월드 보관/복원과 MapContext load lease 수명
- 저장 DTO의 JSON 동등성과 가변 참조 분리
- Player/World/Dungeons 부분 저장과 SaveScope.All
- 연속 저장, 자동/수동 저장 병합, 호출자별 결과
- JSON·파트 파일·manifest 실패와 previous revision 복구
- 저장 정리와 다음 저장의 순서, 정리 실패 재시도
- Reset/Load 중 진행 중인 저장 처리
- 레거시 마이그레이션과 첫 후속 저장
- 던전 완료 rollback, 재접속, 보상 중복 방지
- 합성 NPC 2,400개를 사용한 저장 단계별 시간 측정

진단은 실제 사용자 저장 경로를 사용하지 않는다. 실행마다 프로젝트 `Temp` 아래에
고유 폴더를 만들고 성공·실패와 관계없이 종료 시 삭제한다. 결과는 Unity Console에만
출력하며 로그·보고서 파일을 남기지 않는다.

Runtime/Editor 컴파일은 현재 변경을 확인하는 별도 작업이다. 이 폴더에는 컴파일을
감싸는 `verify.*` 스크립트나 독립 .NET 호스트 프로젝트를 두지 않는다.

프로세스 강제 종료와 전원 차단은 Unity Editor 메뉴에서 안전하게 자동화하지 않는다.
메뉴 진단은 manifest 교체 전 실패, 실제 파일 잠금에 의한 교체 실패, 교체 후 정리 실패를
주입해 원자성과 복구 계약을 확인한다. 실제 플레이 전환·전투·UI 체감과 실사용 월드의
프레임 시간은 Play Mode와 Profiler로 별도 확인한다.
