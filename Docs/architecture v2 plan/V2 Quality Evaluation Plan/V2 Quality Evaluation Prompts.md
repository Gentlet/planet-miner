# V2 품질 평가 단계별 명령 프롬프트

기준: 2026-09-28의 [평가 계획](V2%20Quality%20Evaluation%20Plan.md). 이 문서는 실행할 명령 모음이며 평가 결과가 아니다.

## 사용 방법

- 작성 시점에는 Q00이 완료되어 있다. 일반적인 다음 시작점은 **Q01a**다. 실제 실행 전에는 계획 문서의 최신 상태를 확인한다.
- 아래에서 원하는 단계의 코드 블록 하나를 복사해 요청한다. 각 블록은 같은 프로젝트의 새 대화에서도 단독으로 사용할 수 있다.
- 관련 단계는 같은 대화에서 한 단계씩 이어가고, 아래 영역 묶음이 바뀌면 새 대화를 사용하는 것을 권장한다. 대화를 새로 만드는 것은 선택 사항이며, 한 요청에 한 단계만 진행하는 원칙은 동일하다.
- 경로는 현재 프로젝트의 절대 경로다. 프로젝트를 옮기면 프롬프트의 경로도 바꾼다. 범위·정책은 최신 계획과 현재 사용자 지시를 우선하며, 명령 예시 자체를 새로운 설계 규칙으로 취급하지 않는다.
- 결과는 Results/<단계 ID>.md에 저장한다. 이미 결과가 있으면 기존 판정을 지우지 않고 미완료 부분을 이어가거나 재평가 기록을 추가한다.
- 이전 결과가 없다고 선행 평가를 자동 실행하지 않는다. 현재 단계의 판단에 필요한 소스를 직접 확인하고, 선행 근거 부족으로 확정할 수 없는 부분은 검증 공백으로 기록한다.
- 전체 EditMode·Play Mode, 코드·테스트·에셋 수정은 이 프롬프트의 실행 범위에 포함하지 않는다. 기존 관련 EditMode 테스트는 분석으로 필요성이 확인된 경우만 선택한다.

## 추천 모델 기준

추천 모델과 추론 수준은 각 단계 제목 아래에 적었다. 실행 전에 해당 설정을 선택하고 코드 블록을 복사한다. 프롬프트 본문에는 모델 변경 명령을 넣지 않았다.

| 작업 성격 | 추천 설정 |
| --- | --- |
| Q00 기준선·목록 확인 | GPT-6 Sol · High |
| 일반 도메인 평가 | GPT-6 Astra · High |
| 복잡한 경합·수명주기 경계·의존성 종합 판단 | GPT-6 Astra · XHigh |

이 배분은 이번 프로젝트의 평가 범위에 대한 권장안이며, 단계별 성능을 비교 측정한 결과는 아니다. 공식 안내는 Astra를 가장 높은 역량의 모델로, Sol을 복잡한 작업에 강한 추론 모델로 설명한다. [OpenAI 모델 안내](https://developers.openai.com/api/docs/guides/latest-model) — 2026-09-28 확인.

모델과 추론 수준을 자주 바꾸지 않으려면 전체를 **Astra · High**로 진행해도 된다. 중요한 결론을 내리기 어렵거나 반대 근거가 남는 단계만 XHigh로 높인다. Q00도 Astra · High로 진행할 수 있다. 설정만으로 평가의 정확성이 보장되지는 않으며, 소스 근거와 필요한 테스트 확인을 유지한다.

## 0. 기준선 재평가 — 필요한 경우만

### Q00 — 현재 기준선과 대상 확정

**추천: GPT-6 Sol · High** — 기준선과 평가 대상의 사실 확인 중심. Astra · High도 사용 가능.

이미 완료된 단계다. 기준선 갱신이나 재평가가 필요할 때만 아래 명령을 사용한다.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
계획과 기존 Results/Q00.md를 읽고, Q00 기준선만 재평가해줘. 이전 기준선·발견 사항은 보존하고 새 평가일의 변경 사항을 구분해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: 현재 기준선과 대상 확정
중점: HEAD·작업 트리 변경·Unity/패키지 버전, 실제 구현 목록과 문서 차이, 평가 제외 범위 기록
Git HEAD·작업 트리·Unity/패키지 버전·실제 구현 목록·문서 차이·포함/제외 범위만 확인해줘. 상세 도메인 평가, 컴파일과 테스트는 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q00.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

## 1. 공통 구조·아이템 — 같은 대화에서 단계별 진행

### Q01a — Assets/Scripts/Phases/, 두 End ECB 시스템

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q01a 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Assets/Scripts/Phases/, 두 End ECB 시스템
중점: 6단계와 실제 정렬, 구조 변경 기록·재생 경계 표
Q00의 F-001·F-002를 확인하되 실제 그룹 정렬과 ECB 선택은 현재 코드로 검토해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q01a.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q01b — Phase/ECB 공통 의존 계약과 평가 기준선

**추천: GPT-6 Astra · XHigh** — 공개 실행 계약과 숨은 순서 의존을 구분하는 단계.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q01b 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Phase/ECB 공통 의존 계약과 평가 기준선
중점: 공개된 실행·데이터 가시성 계약과 내부 순서 가정 구분, 의존 관계표 시작. 도메인별 관계는 후속 단계에서 누적
Q01a 결과의 Phase·ECB 경계를 참고하고, 도메인 전체 의존성 조사는 후속 단계에 남겨줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q01b.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q02 — Item 요청·상태 컴포넌트와 ItemOwnershipApplySystem

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q02 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Item 요청·상태 컴포넌트와 ItemOwnershipApplySystem
중점: 상태 Owner, 소유권·렌더 태그·버퍼 변경의 역할 분담과 요청 소비 추적
Q01a·Q01b의 공통 계약을 참고하고 소유 버퍼와 렌더 상태의 작성 경로를 함께 확인해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q02.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q03 — ItemLifecycleApplySystem

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q03 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: ItemLifecycleApplySystem
중점: 생성·삭제·생산 결과 소비, 중복/무효 요청, 저장품 삭제 전 참조 정리
Q02의 소유권 계약을 참고하고 생성·삭제가 저장 버퍼와 생산 결과에 미치는 영향을 확인해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q03.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

## 2. 설정 — 새 대화 권장

### Q04 — ItemConfigInitSystem, Item Config·Registry

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q04 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: ItemConfigInitSystem, Item Config·Registry
중점: 설정 입력→검증→게시, 기본값·수명·MaxStack 계약
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q04.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q05 — BuildingConfigLoader, 설정 게시 시스템·컴포넌트

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q05 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: BuildingConfigLoader, 설정 게시 시스템·컴포넌트
중점: 건물/자재/런타임 설정의 일관성, 검증 실패와 게시 범위
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q05.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q06 — RecipeConfigLoader, RecipeInitSystem, 레시피 Config

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q06 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: RecipeConfigLoader, RecipeInitSystem, 레시피 Config
중점: 재료·주생산품·부산물·Blob 게시와 해제
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q06.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q07 — WorldGenerationConfigLoader와 게시 시스템

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q07 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: WorldGenerationConfigLoader와 게시 시스템
중점: 자원·바닥 통합 검증, Sprite 참조, 부분 게시 방지
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q07.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

## 3. Authoring·Baker·프리팹 연결 — 새 대화 권장

### Q08 — Item Authoring·DB·Baker와 직접 관련 Inspector

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q08 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Item Authoring·DB·Baker와 직접 관련 Inspector
중점: 정적 식별 정보와 런타임 초기화, 중복 등록·누락 정책
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q08.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q09 — Building DB Authoring·Baker와 직접 관련 Inspector

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q09 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Building DB Authoring·Baker와 직접 관련 Inspector
중점: 타입·크기·방향·DB 참조와 Spawn 계약
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q09.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q10 — Resource DB Authoring·Baker

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q10 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Resource DB Authoring·Baker
중점: 자원 프리팹 계약, 준비 대기와 생성 경계
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q10.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q11 — 위 DB의 실제 프리팹·SubScene·장면 연결

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q11 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: 위 DB의 실제 프리팹·SubScene·장면 연결
중점: 필요한 직렬화 참조만 추적, 테스트용 프리팹과 실제 연결 차이 기록
Q08~Q10의 프리팹 계약을 참고해 실제 직렬화 참조를 추적하고, 테스트용 ECS 프리팹 검증과 실제 SubScene 베이킹 증거를 구분해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q11.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

## 4. 공간 인덱스·물류 — 새 대화 권장

### Q12 — *SpatialIndex, *SpatialSyncSystem

**추천: GPT-6 Astra · XHigh** — 공유 NativeContainer의 Reader/Writer·Fence·가시성을 함께 추적하는 단계.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q12 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: *SpatialIndex, *SpatialSyncSystem
중점: 원본/인덱스, Fence, Clear·재등록·용량·Dispose, 동일 프레임 가시성. 필요 시 인덱스별 분할
Q01a·Q01b의 ECB·데이터 가시성 계약을 참고해 인덱스별 Reader/Writer와 Fence를 확인해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q12.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q13 — Belt Decision→Execution

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q13 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Belt Decision→Execution
중점: 간격·수용량·경계 이동, enable 상태, 이동 계획 소비
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q13.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q14 — Building Input→Storage Reservation→Apply

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q14 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Building Input→Storage Reservation→Apply
중점: 필터·슬롯·MaxStack·동시 입고와 소유권 인계
Q02·Q04의 소유권·MaxStack 계약과 Q12·Q13의 공간·벨트 경계를 필요한 부분만 참고해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q14.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q15 — Storage/Product Output→예약→Apply

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q15 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Storage/Product Output→예약→Apply
중점: 출고 선택, 목적지 용량, 거부 시 상태 보존, 생산품 슬롯 우선순위
Q14의 저장 반영 경로와 Q12·Q13의 공간·벨트 경계를 필요한 부분만 참고해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q15.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q16 — Splitter/Merger→목적지 예약→Routing Apply

**추천: GPT-6 Astra · XHigh** — 분배·합류·출고 사이의 목적지 경합과 예약 경계를 평가하는 단계.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q16 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Splitter/Merger→목적지 예약→Routing Apply
중점: 포트·회전·경합·커서·설치 우선순위, 일반 벨트 이동과 예약 범위 구분
Q13~Q15의 벨트 이동·입출고 계약을 참고해 공통 목적지 예약이 실제로 중재하는 범위를 구분해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q16.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

## 5. 월드 생성·채굴 — 새 대화 권장

### Q17 — 초기 청크 Bootstrap→Load Command

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q17 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: 초기 청크 Bootstrap→Load Command
중점: 중복 요청, Pending/완료 추적, 다음 프레임 완료 알림 소비
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q17.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q18 — Resource Generation→ECB→Spatial Sync

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q18 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Resource Generation→ECB→Spatial Sync
중점: 결정론·청크 경계·프리팹 준비 대기·빈 청크 완료
Q07·Q10·Q17의 설정·프리팹 준비·청크 수명주기 계약과 Q00의 F-002를 참고해 실제 ECB 경계를 확인해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q18.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q19 — FloorBiomeSampler, ChunkUtility, V2FloorBiomePreview

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q19 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: FloorBiomeSampler, ChunkUtility, V2FloorBiomePreview
중점: 좌표·바이옴·전이·Sprite 선택, 진단 표시와 게임 상태 책임
Q07의 바닥 설정 계약을 참고하고, 순수 샘플링 결과와 진단용 시각 표시의 보장 범위를 구분해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q19.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q20 — Miner Decision→Execution→ProductResult

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q20 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Miner Decision→Execution→ProductResult
중점: 자원 선택, 출력 여유, 진행도·고갈 처리, 아이템 생성 인계
Q03·Q15·Q18의 아이템 생성·출력·자원 계약을 필요한 부분만 참고해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q20.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

## 6. 제작 — 새 대화 권장

### Q21 — Crafter Decision→Execution→State Apply

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q21 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Crafter Decision→Execution→State Apply
중점: 재료 선소비, 진행 상태, 전체 출력 슬롯 용량, 작업 중단 경로
Q03·Q06·Q14·Q15의 아이템 생성·레시피·입출고 계약을 필요한 부분만 참고해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q21.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q22 — CrafterRecipeCommandSystem

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q22 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: CrafterRecipeCommandSystem
중점: 레시피 변경·진행 초기화·잔여물 이동·입고/제작 재개 조건
Q21의 제작 상태·재료 소비 계약과 잔여물 출력 경로를 연결해 확인해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q22.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

## 7. 배치·공사·철거 — 새 대화 권장

### Q23 — Placement Request→검증→현장 생성

**추천: GPT-6 Astra · XHigh** — 회전·점유·묶음 정책과 여러 배치 요청의 경합을 평가하는 단계.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q23 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Placement Request→검증→현장 생성
중점: footprint 회전, 점유·해금·채굴 자원, 묶음 정책·여러 요청 경합
Q05·Q09·Q12의 건물 설정·프리팹·공간 계약을 필요한 부분만 참고해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q23.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q24 — 공사 자재 수령 Job

**추천: GPT-6 Astra · XHigh** — 자재 수령·예약량·아이템 소유권의 동시 변경을 추적하는 단계.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q24 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: 공사 자재 수령 Job
중점: 잔여 요구량·중복 공급·예약량·보관 버퍼·소유권의 일관성
Q02·Q23의 소유권·현장 생성 계약을 참고하고, 자재 수령 책임은 현재 Job과 호출 관계로 확인해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q24.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q25 — 공사 완료 Job→BuildingLifecycleUtility

**추천: GPT-6 Astra · XHigh** — 자재 소비·건물 생성·현장 제거의 성공 및 실패 경계를 평가하는 단계.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q25 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: 공사 완료 Job→BuildingLifecycleUtility
중점: 완료 조건, 자재 소비와 스폰 성공/실패, 현장 제거의 일관성
Q05·Q09·Q23·Q24의 설정·프리팹·현장·자재 계약을 참고하고, 스폰 실패 시 자재와 현장 상태를 함께 확인해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q25.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q26 — 공사 취소 Job

**추천: GPT-6 Astra · XHigh** — 취소·공급·완료의 같은 프레임 경합을 평가하는 단계.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q26 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: 공사 취소 Job
중점: 취소/공급/완료 경합, 도착 자재 반환, 중복 취소와 참조 정리
Q24·Q25의 자재 수령·완료 경로와 취소 처리의 동일 프레임 관계를 확인해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q26.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q27 — 완공 건물 직접 Spawn 경로

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q27 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: 완공 건물 직접 Spawn 경로
중점: 타입별 초기화, DB/Config 누락 정책, 공사 완료 경로와 공통 계약
Q09·Q25의 프리팹·공사 완료 경로를 참고해 직접 스폰과의 공통 계약 및 실패 처리를 확인해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q27.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q28 — 건물 철거 Job

**추천: GPT-6 Astra · XHigh** — 물류 반영·철거·아이템 반환·참조 정리의 순서 의존을 평가하는 단계.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q28 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: 건물 철거 Job
중점: 저장품·생산품·환급, 벨트 아이템 보존, 물류 반영 순서와 중복 요청
Q02·Q14~Q16·Q27의 소유권·물류·스폰 계약을 필요한 부분만 참고해 철거 시점의 참조 정리와 반환을 확인해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q28.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

## 8. 검증 체계·종합 평가 — 새 대화 권장

### Q29 — Validation 및 테스트 지원 코드

**추천: GPT-6 Astra · High** — 해당 도메인의 코드·계약·직접 의존성을 평가.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q29 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: Validation 및 테스트 지원 코드
중점: 실제 불변식 검사 범위, Factory와 Spawn/Baker 차이, 수동 호출과 그룹 정렬 차이
이전 단계에서 지적한 검증 공백을 참고해 실제 Validator 및 테스트 지원 코드가 보장하는 범위를 확인해줘.
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q29.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q30a — 도메인 연결부 최종 확인

**추천: GPT-6 Astra · XHigh** — 여러 도메인 사이에 남은 연결부 문제를 추적하는 단계.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q30a 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: 도메인 연결부 최종 확인
중점: 앞 단계에서 남은 경계 문제만 추적; 통합 검증 질문별로 분할 가능
안정성·유지보수성·V2 적합성을 평가하고, 특히 다른 시스템의 내부 구현이나 세부 순서를 얼마나 알아야 하는지 확인해줘. 상태 Reader/Writer와 의존 관계, 공개 계약을 유지한 내부 변경의 영향 사례를 기록하고, 필요한 동기화와 불필요한 결합을 구분해줘.
코드 분석 후 실행으로 확인할 질문이 있을 때만 계획의 후보에서 관련 기존 EditMode 테스트를 좁혀 실행해줘. Unity CLI 스킬과 AGENTS.md의 검증 절차를 따르고 최종 상태를 확인해줘. 읽기 전용 분석만이면 새 컴파일을 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q30a.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q30b — 시스템 간 의존성 종합 평가

**추천: GPT-6 Astra · XHigh** — 전체 의존 관계와 변경 영향의 적합성을 종합 판단하는 단계.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q30b 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: 시스템 간 의존성 종합 평가
중점: 누적 관계표·필요 시 의존도 그림, 숨은 순서 의존・상태 교차 쓰기・순환 결합・책임 집중, 변경 영향과 분리 후보 정리
완료된 단계 결과의 의존 관계와 변경 영향 기록을 종합해줘. 직접 호출·상태 교차 쓰기·숨은 순서 의존·순환 결합·책임 집중을 구분하고, 핵심 결론은 현재 소스로 확인해줘. 누락된 평가 영역은 그대로 표시해줘.
의존성 개수나 시스템 통합 여부만으로 품질을 판정하지 마. 필요한 공개 계약·Job/Fence/ECB 동기화와 개선할 결합을 구분하고, 과거 비교 근거가 없으면 V1 대비 감소를 확정하지 마.
기존 검증 근거를 재사용하고, 미확인 의존성의 동작 확인이 꼭 필요할 때만 관련 기존 EditMode 테스트를 선택해줘.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q30b.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

### Q31 — 종합 보고

**추천: GPT-6 Astra · XHigh** — 서로 다른 단계의 근거·미확인 사항을 비교해 최종 판단하는 단계.

```text
평가 계획: C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\V2 Quality Evaluation Plan.md
위 계획과 Results/Q00.md의 기준선, 이번 단계에 직접 관련된 이전 결과만 읽고 Q31 하나만 진행해줘.
최신 사용자 지시와 현재 소스를 우선하고, 관련 코드의 기준선 이후 변경을 확인해 기록해줘. 기존 작업 트리 변경은 보존해줘.
대상: 종합 보고
중점: 세 축별 결과와 의존성 목표 적합성, 우선순위, 검증 공백, 별도 수정 후보와 사용자 결정 사항 정리
종합에 필요한 기존 단계 결과와 Q30b 의존성 평가를 읽고, 완료/미완료 범위·세 축별 판단·주요 문제·수정 우선순위·미확인 사항을 출처 링크와 함께 정리해줘. 과거 기준선의 결과를 현재 코드의 검증으로 간주하지 마.
누락된 단계를 자동 수행하거나 전체 재평가하지 말고, 결론에 꼭 필요한 현재 소스만 확인해줘. 새 실행 검증은 미해결 질문이 있을 때 관련 기존 EditMode만 선택하고, 그 외에는 실행하지 마.
전체 EditMode·Play Mode는 실행하지 말고 코드·테스트·Config·에셋을 수정하지 마. 테스트 보강이나 설계 결정이 필요하면 제안과 질문으로 기록해줘.
결과는 C:\Projects\unity\PlanetMiner\planet miner\Docs\architecture v2 plan\V2 Quality Evaluation Plan\Results\Q31.md 에 계획의 양식대로 기록해줘. 문제 ID는 기존 결과에서 중복 여부를 확인하고, 같은 문제는 최초 발견 문서를 링크해줘.
계획 문서에는 해당 단계의 상태·결과 링크와 현재 진행 상황·다음 단계만 갱신해줘. 코드 확인·실행 확인·추정·미확인을 구분하고, 필요한 검증이 막히면 완료로 표시하지 마.
완료한 범위·주요 발견·검증 결과 또는 미실행 사유·남은 질문·결과 문서 링크를 보고한 뒤 멈춰줘. 다음 단계나 수정 작업은 자동 진행하지 마.
```

