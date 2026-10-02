# F-004 구현 및 검증 기록

- 날짜: 2026-10-02
- [Task](../V2%20Quality%20Improvement%20Tasks.md#f-004--같은-프레임-입고철거의-렌더-태그-순서-의존) · 최초 평가 [Q02](Q02.md) · 철거 평가 [Q28](Q28.md)
- 상태: 승인된 정책 구현·컴파일 완료. 테스트 및 실제 그룹 실행은 미수행.

## 확정 정책과 범위

정책의 기준은 [AGENTS.md](../../../../AGENTS.md)의 F-004 계약이다. 같은 틱 입고·철거는 입고 후 건물 자리로 반환한다. 철거 버퍼에 남은 실물의 공급을 거부하며, 유효한 Destroy를 유지하고 충돌 인계를 거부한다. 각 기존 시스템은 자신의 적용 대상만 변경한다.

F-005의 Command 승인, 완료 생산물 폐기, Storage/Product Spawn 거부, 선소비 미보상, 기존 실물 반환·건축 비용 환급·World Spawn 유지 계약은 바꾸지 않았다. 공급 거부는 기존과 같이 요청을 소비하고 예약량을 보존한다. 요청에 예약 식별 정보가 없으므로 임의의 예약 차감·재시도 경로를 추가하지 않았다. 일반 공급의 중복·기존 소유자 인계·예약 정산(F-003)은 별도 범위다.

## 변경한 코드

- `Common/DemolishBuildingRequestLookup.cs`: Item Lifecycle 파일에 있던 기존 조회를 이동하고, 승인된 철거 요청의 Stored/Product 버퍼에 실물이 남아 있는지 확인하는 조회를 추가했다. 별도 승인·반환 목록이나 영속 상태는 생성하지 않는다.
- `BuildingItemStorageApplySystem`: 활성 Destroy 대상은 입출고 결정만 소비하고 버퍼·위치·이동·Transfer를 변경하지 않는다. 그 외의 기존 입출고와 벨트 Fence는 유지한다.
- `ItemOwnershipApplySystem`: 활성 Destroy 대상과 철거 버퍼의 실물은 Transfer만 소비한다. 해당 아이템의 Owner·렌더 태그를 쓰지 않으며, 일반 이전의 기존 분기는 유지한다.
- `ConstructionLifecycleApplySystem`: Storage Apply 이후의 소유 버퍼를 읽도록 순서를 명시했다. Destroy/철거 반환 실물의 공급은 도착량·예약량·현장 버퍼·Owner·태그 변경 전에 거부한다. 취소 반환에서도 Destroy 대상은 제외한다. 사용하지 않던 Owner 조회 대신 실제 버퍼 소속을 읽는다.
- `BuildingLifecycleApplySystem`: 기존 반환 위치·Owner·태그 처리를 유지하고 Destroy 대상의 반환을 제외한다. 같은 삭제 실물에 벨트 정지 ECB가 적용되는 것을 막기 위해 철거 벨트 Cleanup도 Destroy 대상을 제외한다.
- `ItemLifecycleApplySystem`: 기존 조회 클래스의 파일 이동만 반영했다. 생성·완료 생산물·Destroy의 적용 정책은 유지한다.
- `ItemRequests`, `ConstructionRequests`: 충돌 시 요청 소비와 적용 책임을 주석에 반영했다.

## 코드로 확인한 실행 경계

Storage Apply의 Input→Output Job과 기존 Fence/Dependency를 유지한다. Ownership은 기존처럼 Storage/Route 이후 실행하며, 공사 수령은 새로 Storage 이후의 버퍼 가시성을 요구한다. Construction→Building Lifecycle은 기존 순서다. Ownership과 Building Lifecycle의 상대 순서 및 Building/Item Lifecycle 간 승인 전달 순서는 추가하지 않았다.

Ownership과 Construction의 철거 요청 임시 복사본은 `ToComponentDataListAsync` 핸들 뒤에 소비 Job을 연결하고 사용 후 해제한다. 마지막 핸들은 `state.Dependency`와 EndStateApply의 producer에 전달한다. 전체 World 완료, 중간 Playback, 새로운 Sync Point는 추가하지 않았다.

다음 결과는 소스 경로 분석이며 이번 실행 관측이 아니다.

1. **입고+철거:** Storage가 실물을 버퍼에 추가한다. Ownership은 버퍼 소속으로 철거 반환임을 확인해 Transfer만 소비한다. 철거 ECB만 최종 World Owner·건물 위치·태그 제거를 기록하므로 기존 두 ECB의 Add/Remove 경합이 제거된다. 새 입고품의 이동은 Storage Apply가 이미 비활성화한다. 건물과 소유 버퍼는 EndStateApply에서 삭제된다.
2. **출고+철거:** 정상 출고는 원본 버퍼에서 실물을 제거하므로 조회상 철거 반환 대상이 아니다. 일반 Transfer가 World Owner·태그 제거를 적용하고 기존 출고 위치·이동을 유지한다. 출력 벨트도 철거되면 기존 Cleanup이 정지시킨다.
3. **공급+철거:** 입출고 이후에도 철거 Stored/Product 버퍼에 남은 실물은 수령 승인 전에 거부한다. 도착량·예약량·현장 버퍼·렌더 명령을 기록하지 않고 요청만 소비한다. 현재 Owner가 월드인 이번 틱 입고품도 버퍼 소속으로 식별한다.
4. **Destroy+인계/반환:** 대상의 활성 Destroy를 각 변경 경계에서 확인하여 충돌한 입출고·Transfer·Supply·반환/벨트 정지 명령을 제외한다. Item Lifecycle의 삭제는 유지한다. 유효한 Destroy는 Producer의 선행 소유 버퍼 제거를 전제로 하며, 잘못된 잔류 버퍼의 전역 복구나 공사 도착량 재산정은 구현하지 않았다.

## 새로 수행한 검증

- `Verify-Unity.ps1 -CompileOnly`는 모드 선택 오류로 컴파일 시작 전에 실패했다. 래퍼 자체는 수정하지 않았으며 직접 Unity CLI로 전환했다.
- Unity 6000.4.11f1의 연결된 Editor에서 `recompile --focus false` 후 `recompile_status`를 확인했다.
- 결과: `completed`, `failed:false`, `errors:[]`, `compilationFailed:false`.
- Editor: `ready`, `compiling:false`, `domainReloadInProgress:false`, `playMode:stopped`.
- `Assembly-CSharp.dll`은 최신 프로젝트 C# 소스보다 새 파일이며 freshness 확인은 true다.
- `git diff --check`: 오류 없음. 기존 파일의 줄바꿈 변환 경고는 저장소 설정에 따른 것이며 컴파일 오류가 아니다.

원본 로그: [컴파일 시작](../../../../Logs/QualityImprovement/F004/recompile-start.json), [최종 상태](../../../../Logs/QualityImprovement/F004/recompile-status.json), [Editor 상태](../../../../Logs/QualityImprovement/F004/editor-status.json), [어셈블리 최신성](../../../../Logs/QualityImprovement/F004/assembly-freshness.json). 로그는 로컬 검증 자료다.

## 실행 미검증과 남은 범위

새 테스트 작성·assertion 보강, 기존 EditMode 실행, 실제 정렬 그룹의 양쪽 ECB 순서 재현은 수행하지 않았다. 컴파일 성공을 최종 Owner·소유 버퍼·위치·DisableRendering의 런타임 일치 증거로 확대하지 않는다. Play Mode, 실제 렌더링·베이킹·성능도 검증하지 않았다.

F-003의 일반 중복 공급 및 소유 버퍼 인계, 거부 시 예약 정산, 잘못된 외부 요청의 전역 보정은 이번 완료 범위가 아니다. Q02/Q28의 원본 평가 기록은 수정하지 않았다.
