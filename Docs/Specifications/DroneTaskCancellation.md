# 드론 작업 취소와 실패 정산 계약

## 확정 범위

2026-10-30 확정한 1A/2A/3A를 적용한다. 기존 SubmitCancel은 root 단위다. Open root뿐 아니라 실제 실행 중인 비종료 배정이 남은 Closed root도 취소한다. 회수 root는 픽업 후 Closed여도 납품 전일 수 있다. 완전히 종료된 Closed root와 활성 배정 없이 일반 운반 목표량을 이미 달성한 작업은 거부한다.

지원 종류는 기존 ConstructionSupply/WorldItemRecovery/StorageImport/StorageExport다. 수동 우선순위와 일시 중단/재개는 추가하지 않는다. 실제 이동 실패/대상 무효/인계 성공 및 부족량은 기존 결과와 정산 경로를 재사용하며 새 추정 타임아웃이나 범용 실패 신고 API를 추가하지 않는다. 추가 작업 유형의 실제 Producer는 해당 후속 구현에서 연결한다.

## 같은 틱 우선순위

요청은 기존 World 접수 Sequence 순서로 판단한다. Decision이 승인한 취소는 이번 틱 아직 수행하지 않은 해당 root의 이동/자동 및 외부 픽업/납품보다 우선한다. 이미 확정한 성공 실물과 완료량, 소비한 이동 에너지/배터리는 되돌리지 않는다. 취소 접수 결과와 화물 복구 완료를 구분한다.

DroneTaskRequestDecisionUtility가 요청 제어 의도를 작성하고 Task Decision은 비종료 배정에 CancelAssignment를 작성한다. 일반 InvalidateAssignment와 구분해 원래 작업이 여전히 유효해도 사용자 취소로 종료한다. 취소 예정 root의 신규 배정/적재품 재배정 후보를 만들지 않는다. 승인한 틱에는 Station 자동 Activate도 만들지 않는다.

Reservation은 같은 의도로 남은 현장 예약/일반 운반 확보량을 한 번 반환한다. 고아/미인계 확보량도 기존 반환 경로를 거치며 새 후보는 취소 root를 확보하지 않는다. Movement는 같은 수행자의 현재 연결을 기준으로 이동을 멈추고 이전 이동 의도/결과를 정리한다. 이 연결/revision은 Decision 이후 해당 Execution까지 새로 공개되지 않는다.

자동 행동 Producer는 CancelAssignment로 새 행동을 만들지 않는다. 이미 접수된 외부 행동도 Lifecycle의 최종 인계에서 같은 배정/revision의 취소를 읽어 Rejected로 공개한다. 이 경우 실물 인계/완료량/행동 번호를 진행하지 않는다. 단순 경로 평가/계획된 충전 경유/기존 보관 공간 대기를 실제 실패로 오인하지 않는다.

## Lifecycle과 공개

Lifecycle은 취소 의도의 배정 revision을 확인한다. 일반 실패는 최신 무효 상태를 다시 검사하고, 사용자 취소는 Decision 승인 자체를 종료 근거로 삼는다. 빈 수행자는 Cancelled/연결 해제, 적재품이 있으면 기존 배정과 실물을 보존한 Retargeting으로 전환한다. 취소 배정의 NextAction은 None으로 만들어 새 revision의 복구 배정이 공개되기 전 과거 신호를 사용하지 않는다.

Task 요청 적용은 자체 정산으로 참조가 줄어든 것을 Busy로 재판정하지 않는다. 승인 이후 새 계획/이동/인계를 차단하므로 승인 전 조건으로 되돌리면 Closed 회수 등의 정리가 취소 성공을 거부할 수 있다. root를 Cancelled로 만들고 동일 키 자동 생성 차단을 기존 EndSimulation에 기록한다. 마지막 참조 정리에서만 root 삭제를 기록하며 살아 있는 배정/확보량이 있으면 삭제를 보류한다. 취소 결과/요청 소비/차단과 삭제의 외부 공개 경계는 EndSimulation이고, 내부 Cancelled/배정 상태는 기존 StateApply 책임에 따라 반영한다.

Cancelled는 원래 작업 중단 승인과 정산을 뜻한다. 화물이 이미 보관/방출되어 복구를 마쳤다는 뜻이 아니다. 취소 뒤 빈 수행자의 연속 배차/귀환과 적재품 재배정은 다음 Decision부터 기존 흐름으로 이어진다. root의 Cancelled는 승인된 명시 취소를 보존하는 상태이며 일반 Closed와 구분한다. 복구 배정의 새 revision이 공개되거나 새 생성으로 동일 키 차단을 해제해도 같은 root의 재취소는 Rejected다. 이 상태는 새 목표/진척 Writer나 별도 취소 이력 캐시를 만들지 않는다. 중복 취소/과거 요청은 화물 복구를 다시 멈추거나 차단을 되살리지 않는다. 같은 틱 유효한 새 생성 요청은 기존 Sequence 순서로 차단을 해제하고 별도 root를 만들 수 있다.

## 화물과 실패

화물의 기존 목적지가 여전히 유효한 일반 보관처라면 복구 대상으로 다시 선택할 수 있다. 취소된 일반 운반의 원래 목표량/실제 완료량은 변경하지 않으며 남은 확보만 반환한다. 기존 재배정 Publish가 0 확보 기록을 제거해 새 보관 이동의 실제 성공을 취소된 목표에 기록하지 않는다. 취소된 공사 공급 키의 차단은 유지하며 유효한 명시 생성이 해제한다.

목표 소멸/이동 에너지 실패는 기존 일반 무효화 경로를 유지한다. 실패 자체는 사용자 취소가 아니므로 영속 사용자 생성 차단을 만들지 않는다. 충전/귀환/미세 잔량은 기존 충전 및 Lifecycle 소유자를 따르며 취소 요청이 배터리를 환급하거나 복제하지 않는다.

실제 자동 안전 방출/표시 기반 회수는 [화물 복구 계약](DroneCargoRecovery.md)에 연결했다. 추가 직접 작업은 해당 Producer, 공사 취소 통합은 Task 9.20에 남긴다. 기존 보관 복구는 재사용한다.

## 검증

실제 배차/경로/이동/인계/정거장과 EndSimulation을 실행해 배차 전/이동 중/픽업 직전/픽업 후/전달 직전/정박 중 취소, Closed 회수 root, 완료 후 늦은 취소, 취소와 새 생성 순서, 외부 지연 행동과 revision을 검사한다. Unity C# 컴파일 completed/failed false/errors 없음/compilationFailed false와 최신 runtime/Editor DLL을 확인했다. 관련 선택 EditMode 12개 fixture 253/253(실패/생략/불확정 0), 이 중 새 취소 fixture 17개가 통과했다. 정거장 범위는 fixture 입력이며 전체 장면/Power 충전의 통합 실행 증거로 확대하지 않는다. 관련 기존 Charging/Return/Movement 테스트를 별도로 포함했다. Play Mode/Player/성능/전체 Burst AOT는 미수행이다.

검수에서 적재 취소 후 반복 요청이 복구를 다시 멈추는 경계를 찾아 root Cancelled로 구분했다. 다음 틱 Retargeting과 복구 revision 공개 뒤의 재취소/새 생성으로 해제한 차단 유지까지 확인한다. 수정 전 두 사례는 Rejected 대신 Cancelled로 실패했다. 회귀 조건의 이동 검사는 실제 기존 단거리 경로를 사용한다. 테스트 러너가 장면 복원 오류로 중단된 실행은 통과 통계에서 제외했고, 실제 Editor API로 EditMode를 확인한 뒤 지원 cancel 명령으로 상태를 정리하고 재실행했다.
