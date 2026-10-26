# 드론 작업 생성/취소 요청

2026-10-24 Task 9.4. 현재 공급/회수 관리와 EndSimulation 공개를 재사용한다. 수동 숫자 우선순위와 일시 중단/재개는 기획에서 제외하며 진행 중 취소 정산은 Task 9.15에 남긴다.

## 지원 범위와 입력

- `DroneTaskRequestUtility.SubmitCreate`는 공사 현장/품목별 ConstructionSupply, 현장 방해물 또는 DroneRecoveryPending 방출품의 WorldItemRecovery만 지원한다. 공급은 현재 미충족 요구가 있어야 하고 회수는 현재 월드 Owner/품목/회수 사유가 유효해야 한다.
- 일반 바닥 전체 회수/범위 회수/일반 반입과 반출/드론 직접 철거의 실제 지원은 후속 Task다. 지원하지 않는 Kind/Operation은 Unsupported, 대상/요구/회수 자격이 맞지 않으면 Rejected로 소비한다. 미지원 요청을 빈 작업이나 성공 결과로 남기지 않는다.
- `SubmitCancel`은 현재 Open root를 대상으로 한다. 종류/대상/품목 변경은 취소 후 새 생성 요청이며 공급 작업 취소는 공사 현장 취소와 다르다. 현장/요구량/아이템/배터리/화물 원본을 취소 요청이 변경하지 않는다.
- 외부 접수는 시뮬레이션 전 메인 스레드다. 요청용 World Sequence는 실제 작업 CreationSequence와 독립이며 0/소진을 접수 전에 거부한다. 실제 새 작업을 기록할 때만 기존 Execution 소유자가 CreationSequence를 사용한다.

## 접수 순서와 중복

같은 틱의 요청은 Sequence 순서로 판단한다. 같은 키의 기존 유효 작업이 있거나 앞선 생성 요청이 새 작업을 기록하면 Existing으로 같은 작업 Entity를 반환한다. 외부 생성 요청을 먼저 실행하고 자동 생성 의도와 같은 로컬 기록으로 중복을 막는다. 자동 작업의 적재품/PlacementStamp/CreationSequence 선택 규칙은 변경하지 않는다.

취소 후 같은 키의 생성 요청이 오면 재생성을 허용한다. 반대로 기존 작업 생성 조회 뒤 취소가 오면 먼저 Existing 결과를 반환하고 그 작업을 취소한다. 결과는 각 명령의 처리 결과이며 결과의 Task가 이후 명령에서도 계속 살아 있다는 보장이 아니다. 취소된 같은 Task Entity를 다시 취소/변경하면 Rejected다. 새 작업은 새 Entity/CreationSequence를 받는다.

## 취소와 자동 생성 차단

현재 Task 9.4의 취소는 연결 배정/미공개 배정 기록/현장 미정산 예약이 없는 작업만 승인한다. 연결 배정은 terminal 상태여도 Lifecycle의 삭제/정산이 끝날 때까지 참조로 취급한다. 미공개 Quantity/CommittedQuantity/PublicationQueued와 현장 ReservedQuantity가 남으면 Busy로 거부한다. 최종 Lifecycle에서도 현재 참조를 다시 검사하며 Busy는 성공이나 지연 취소 접수가 아니다.

승인된 취소만 root 삭제와 자동 생성 차단을 EndSimulation에 함께 기록한다. 공급 차단 키는 `(현장 Entity, ItemType)`, 회수 차단 키는 `(아이템 Entity)`다. 차단은 명시적인 유효 생성 요청의 Execution 기록 성공으로 해제한다. 거부된 생성/Busy 취소는 기존 차단 원본을 변경하지 않는다. 대상 Entity가 소멸하면 다음 Lifecycle에서 차단을 회수하며 Entity Version이 다른 새 대상에 승계하지 않는다.

진행 중 배정/화물을 끊거나 예약을 해제하는 수동 취소는 여기서 수행하지 않는다. 기존 자동 무효화의 정산은 유지하고, 수동 활성 취소/실제 수행 실패 원인의 연결은 Task 9.15에서 구현한다.

## Writer와 공개 경계

| 자료 | Writer / 소비자 / 수명 |
| --- | --- |
| DroneTaskRequest / DroneTaskRequestSequence | Submit의 불변 입력/접수 순번. 요청은 EndSimulation에 제거하고 순번은 World 동안 유지한다. |
| DroneTaskRequestDecision | DroneTaskDecisionSystem만 자격/기존 Task/앞선 생성 요청/회수 이유를 작성한다. 원본 작업을 변경하지 않으며 EndSimulation에 소비한다. |
| DroneTaskGenerationDecisionElement | Decision만 후보 singleton에 매 틱 Clear/재작성한다. 이번 틱 생성/후보/재배정 대상 판단의 제어 의도이며 영속 차단과 구분한다. |
| DroneTaskRequestExecution | Execution만 CreationRecorded를 작성한다. 생성/기존 결과의 ECB 기록 완료이며 실제 공개 완료가 아니다. Lifecycle이 차단 해제에 소비한다. |
| DroneTaskGenerationBlockElement | 같은 singleton의 영속 사용자 제어 원본. GetOrCreateCandidates가 빈 버퍼를 구성하고 Lifecycle만 EndSimulation에 게시/해제/회수한다. |
| DroneTaskRequestResult | 생성/기존/일반 거부는 Execution, 취소 결과는 Lifecycle이 요청 Entity에 EndSimulation 기록한다. 외부 TryConsumeResult가 공개된 결과를 한 번 읽고 Entity를 삭제한다. |

Decision은 요청을 순서대로 판단한 제어 의도로 취소 예정 root를 이번 틱 신규 후보/적재품 재배정 대상에서 제외한다. Reservation의 수량 원본을 직접 변경하지 않는다. 기존 DroneTaskExecutionSystem이 공통 생성 helper와 한 작업 순번을 사용하고, 새 작업 Entity를 참조하는 Created/Existing 결과는 반드시 생성과 같은 ECB 안에 기록하여 deferred Entity를 remap한다. deferred Entity를 다른 ECB나 ECS scratch에 공개하지 않는다.

Lifecycle은 기존 행동/자동 무효화/Closed 정리 뒤 요청을 적용한다. 현재 작업/연결을 확인해 승인한 유휴 root만 EndSimulation에 삭제하고, 성공한 요청 순서로 차단 원본을 갱신한다. 입력 요청이 남아 있는 동안 TryConsumeResult는 실패한다. 요청/판단/Execution 기록 제거와 결과 추가는 같은 EndSimulation이며 새 작업은 다음 틱 Decision부터 사용한다.

요청 처리는 기존 Task Decision/Execution/Lifecycle의 메인 스레드 경로에 통합했다. 기존 root의 중복은 Decision이 검사하며 중간 Reservation이 root를 변경하지 않으므로 Execution에서 같은 원본을 재조회하지 않는다. Execution의 로컬 recorded 목록은 아직 공개되지 않은 같은 ECB의 새 생성 중복을 막는다. 임시 NativeList는 각 호출에서 해제하고 별도 그룹/공유 NativeContainer/Fence를 추가하지 않는다. Task 9.3의 정거장 Job 체인과 결과 수명도 유지한다.

## API 예

```csharp
Entity request = DroneTaskRequestUtility.SubmitCreate(manager,
    DroneLogisticsTaskKindEnum.ConstructionSupply, site, ItemTypeEnum.Iron);
// EndSimulation 공개 뒤 외부가 소비한다.
if (DroneTaskRequestUtility.TryConsumeResult(manager, request, out var result))
{
    // Created/Existing일 때 result.Task가 이 요청에 대응하는 작업이다.
}
Entity cancel = DroneTaskRequestUtility.SubmitCancel(manager, task);
```

## 검증

관련 선택 EditMode 155/155(실패/생략/불확정 0): DroneTaskRequestTests 18, DroneTaskSchedulingTests 39, DroneRetargetingTests 17, DroneItemTransferTests 33, DroneLifecycleIntegrationTests 6, DroneStationLifecycleTests 18, DroneConfigurationTests 24. 요청 자체의 생성/중복 remap/순번, EndSimulation 전 원본 보존, 취소/재생성/동일 틱 순서, 기존/늦은 Busy 참조, 제한된 회수, 거부/미지원/차단 수명을 확인한다. 취소의 후보 제외는 별도 단계 관측으로, 전체 요청 흐름은 GameSimulationGroup과 실제 Drone/Commit 그룹 정렬로 검사한다.

최종 Unity 컴파일은 completed/failed false/errors 없음/compilationFailed false이고 Editor ready/비컴파일/비리로드를 확인했다. Decision/Execution-StateApply/컴포넌트/guard/경계의 독립 5개 역할 v1 검수에서 동작 결함 지적은 없었다. P3 두 건(취소와 기존 Task 분기의 미사용 판단 필드 쓰기, Execution의 반복 기존-root 조회)을 정리하고 컴포넌트/guard/경계의 v2 집중 재검수를 완료했다. 같은 ECB의 deferred 중복 검사는 유지한다. 보완 후 관련 7개 클래스 155/155도 다시 통과했다.

이 수치는 이번 변경의 선택 실행이며 과거 340/340, 119/119와 합산하지 않는다. 실제 이동/충전/입력 UI/Play Mode/Player 빌드/성능은 검증하지 않는다.
