# 전력 기반 드론 충전 계약

## 확정 정책

2026-10-30 확정한 1A/2A/3A를 적용한다. DroneConfig.json의 chargingSpeed=10은 드론당 초당 배터리 증가량이고 chargingPowerConsumptionPerDrone=5는 최대 소비 전력이다. 최대 배터리는 기존 100을 유지한다. 설정 로더는 명시적 finite 양수를 검증하고 유효한 기존 singleton을 보존한다. 누락/중복/무효 설정은 기존 공통 Fatal이며 부분 게시/기본값/자동 재시도는 없다.

전용/일반 슬롯을 구분하지 않고 유효한 활성 정박 드론을 충전한다. 별도 충전기 수나 우선순위를 추가하지 않는다. 이번 틱 충전량은 Drone Execution에서 확정하고 다음 틱 Decision부터 재배차/경유 재개/수납 판단에 사용한다. 실제 배터리와 관측 원본을 중복 생성하지 않는다.

## 준비와 수요

DroneChargingPreparationSystem이 정거장의 DroneChargingPreparationElement 버퍼를 매 틱 비우고 DockedDroneElement의 수행자별 DesiredCharge/RequestedEnergy를 작성한다. 현재 배터리 부족분과 chargingSpeed * min(DeltaTime, MaxSimulationDeltaTime) 중 작은 양만 준비한다. 비활성 수행자는 제외한다. 새로 정박한 수행자는 EndSimulation 다음 Preparation부터 참여하며 저장 아이템은 충전하지 않는다.

정박 관계는 Station Lifecycle 단독 Writer가 같은 EndSimulation에 양방향으로 게시한다. 정박 수행자는 이동 Job에서 제외되고 다음 Station StateApply까지 관계가 유지된다. 따라서 준비/실행에서 위치와 역참조를 매번 재검증하지 않는다. 정거장 비활성/철거 승인은 각 쿼리의 제외 조건을 따른다.

BuildingPowerDemandPreparationSystem만 PowerDemand/PowerConsumer enable을 작성한다. DroneStation/MainFacility에서는 준비 버퍼의 요청 에너지를 합산하며 고정 대기 전력을 만들지 않는다. 연구 수요는 후속이다. 충전 준비를 이 수요 작성보다 먼저 실행한다.

요청 에너지는 DesiredCharge * PowerPerDrone / ChargePerSecond다. 개별/합산 요청은 마지막 배터리 단위를 충전할 에너지가 부족해지지 않도록 위로 반올림한다. 기존 Power는 요청을 받아 배분/확정 소비하고 EndPower 공급 결과를 게시한다. 미사용 배정은 환급하거나 같은 틱 재배분하지 않는다.

## 실행과 소유자

DroneChargingExecutionSystem은 Drone Execution에서 이동 처리 뒤 실행한다. 정거장별 병렬 Burst Job은 준비된 드론만 방문하며 현재 배터리/활동만 갱신한다. 정거장끼리 같은 수행자를 공유하지 않는 정박 원본 계약에 따라 Lookup 병렬 쓰기가 분리된다. Movement는 비정박 수행자의 이동 소비, Charging은 정박 수행자의 증가량을 소유한다.

각 드론의 준비 요청에 같은 공급률을 적용하되, 합산 요청과 실제 AllocatedEnergy로 계산한 한도를 함께 적용한다. 정거장의 남은 배정 에너지를 넘지 않고 증가량은 DesiredCharge와 최대 배터리를 넘지 않는다. 배터리 가용값은 확보한 충전량을 초과하지 않게 아래로 반올림한다. 이미 충전됐으나 다음 float 단위에 못 미친 양은 같은 DroneBatteryState의 FractionalCharge로 보존한다. 이는 미사용 전력 예산이 아니라 이미 배터리에 저장된 충전량이며 환급/다른 드론 재배분/추가 전력 공급이 아니다. 다음 충전 정산은 이번 배정으로 증가한 양과 기존 잔량만 합쳐 가용값을 게시한다. 완충 시 잔량은 0이며 최대치를 넘는 사용 불가 배정은 기존대로 손실이다.

실제 양수 증가면 Charging, 공급 0/증가 없음이면 WaitingForCharge, 최대 배터리에 도달하면 Idle로 충전 의도를 종료한다. 무전력은 정상 대기다. 충전 결과 때문에 같은 틱 Decision을 다시 실행하지 않는다.

## 미세 충전 잔량

CurrentCharge는 경로/배차/수납이 사용하는 float 가용값이고 FractionalCharge는 그 값에 아직 표현하지 못한 이미 충전된 double 잔량이다. 두 값이 한 배터리의 저장량을 구성하며 최대치나 전력 예약을 복제하지 않는다. 충전만 잔량을 갱신하고 이동 및 역할 전환은 잔량을 보존한다. 신규 개체는 0으로 시작하며 기존 배터리 입력 경계는 두 값의 유한성/비음수/합산 최대치를 검증한다.

준비 수요는 기존 CurrentCharge 가용값 기준 부족분을 사용한다. 잔량만큼 요청을 계속 축소해 다시 정체하지 않도록 이 표현 단위 기준을 유지하며, 실행은 잔량을 포함한 실제 저장량으로 최대치를 제한한다. 이로 인해 완충 마지막에 사용하지 못한 배정분은 손실로 남긴다. 가용값 한 단계가 게시되는 틱에는 그 증가량이 이번 틱 증가보다 클 수 있지만 같은 배터리의 이전 잔량이 그만큼 줄어든다. 총 저장량 증가만 이번 배정 에너지로 제한한다.

DroneBatteryUtility.AddFundedCharge는 기존 잔량과 새 배정으로 충전한 양이 다음 float 단위를 실제로 확보했을 때만 가용값을 올린다. double 덧셈의 경계 반올림으로도 미확보 단위를 올리지 않으며 epsilon 완충이나 임의 배터리 가산은 하지 않는다. 실제로 잔량이 증가한 충전도 Charging 활동이고, 공급이 없으면 저장량을 늘리지 않고 대기한다.

누산은 double의 유한 정밀도를 사용한다. 검증 범위는 기본 10/5/100과 검증한 실용 저속 설정/지속 부하 조합이다. 1e-30 같은 극단 설정과 외부에서 지정한 기존 잔량까지 모든 미세량의 무제한 누적 정확도를 보장하지 않는다. 임의 완충/설정 하한/전력 보상은 추가하지 않는다.

## 재배차와 수납

정거장 자동 요청은 실제 위치와 정박 관계의 Station이 일치하면 Charging 또는 WaitingForCharge에서 기존 Activate를 허용한다. 명시적 Dock은 이동 목적지가 없어도 정박할 수 있으므로 정박 관계를 근거로 사용한다. 비정박 도착 대기는 기존 ChargingStation 일치를 요구한다. 외부 명시 요청의 Idle 자격은 유지한다. 완충 전 실행 가능한 기존/신규 배차는 허용하며, EndSimulation에서 정박을 해제한 다음 틱부터 이동한다. 정박 중 무효화된 적재 배정의 Retargeting 재개도 기존 귀환 계약을 따른다.

수납은 완충/무배정/무동작/빈 화물/공간을 모두 만족해야 한다. 이번 Execution에서 완충됐어도 자동 Store 판단은 다음 Decision부터 한다. 이미 공개된 DropCargo 배정의 충전 경유 이동 재개는 연결하며 실제 Drop 행동 신호 Producer는 후속이다.

## 검증과 후속

실제 Power Decision/Reservation/Execution/EndPower와 드론 페이즈를 연결한 EditMode로 무전력/부분 공급/공급 복구, 전용과 일반 슬롯, 새 정박/완충의 틱 경계, 비이진 충전율, dt 상한, 운반/귀환/충전/재배차/수납 루프를 검사한다. 2026-10-30 초기 구현의 C# 컴파일 completed/failed false/errors 없음/compilationFailed false, runtime/Editor DLL 최신성과 Editor ready/Play stopped를 확인했다. 관련 선택 EditMode 8개 fixture 177/177이 통과했고 실패/생략/불확정은 0이다. 충전 13/설정 41/전력 토폴로지와 생산 51/귀환 15/이동 26/정거장 전환 20/생성 6/공통 건물 구성 5개를 포함한다. 이전 실행 통계와 합산하지 않는다.

명시적 Dock이 ChargingStation 없이 정박한 뒤 자동 재출고되지 않는 경계를 보완했다. 무전력/정상 공급 회귀 2개는 수정 전 모두 정박 해제 대기에서 실패했고 수정 후 통과했다. 정박 관계 Station과 실제 위치를 재개 자격으로 확인하며 비정박 도착 대기의 목적지 확인은 유지한다. 독립 검수의 설정/준비/실행/공개 경계와 가독성/주석 보완도 확인했다.

최종 콘솔 스냅샷에는 오류 로그 70건/경고 818건이 남는다. 오류 로그에는 의도된 잘못된 설정/공통 Fatal/연료 및 망 실패 테스트의 기대 로그가 포함된다. 현재 컴파일 실패는 없고 선택 테스트의 예상하지 않은 로그 실패도 없다. 이를 콘솔 오류가 전혀 없었다는 결과로 보고하지 않는다.

Play Mode/Player/성능/전체 Burst AOT는 별도 검증이다. 제품 충전 시각 표현은 Task 10B.4, 자동 방출은 Task 9.16, 정거장 철거 재배치는 Task 9.19의 후속 범위를 유지한다.


## 미세 잔량 개선 검증

지속 제작 부하가 있는 부분 공급 망과 실용 저속(1e-5) 설정에서 완충/수납에 도달하는 회귀 2개를 추가했다. 수정 전 두 사례는 99.9999695에서 멈춰 실패했고 수정 후 통과했다. 실제 제작 준비/배분/진행과 드론 처리 체인을 실행하며, 매 틱 CurrentCharge와 FractionalCharge의 합산 증가 및 누적 증가가 배정 에너지로 확보한 충전량을 넘지 않는지 검사한다. 완충 후 잔량 0, 동일 Entity 수납, 무전력 대기/정박 해제/이동의 잔량 보존과 잘못된 기존 잔량 입력 거부도 확인했다.

2026-10-30 보완 버전의 C# 컴파일은 completed/failed false/errors 없음/compilationFailed false이며 runtime/Editor DLL 최신성과 Editor ready/Play stopped를 확인했다. 관련 선택 EditMode 8개 fixture 183/183(충전 19개, 나머지 164개)이 통과했고 실패/생략/불확정은 0이다. 이전 177개 실행과 합산하지 않는다. 콘솔 스냅샷은 오류 로그 70건/경고 1121건이며 의도된 잘못된 설정/실패 경로 테스트의 기대 로그 등을 포함한다. 현재 컴파일 실패나 선택 테스트의 예상하지 않은 로그 실패는 없다. Play Mode/Player/성능/전체 Burst AOT는 미수행이다.

독립 검수에서 에너지 정산/단일 배터리 상태/생성 및 역할 수명/공개 경계를 확인했고 지역명과 Reader/Writer 주석을 보완했다. 모든 finite 설정과 외부 기존 상태에서 무제한 누적 정밀도를 보장하지 않는다는 한계는 위 정밀도 범위를 따른다.
