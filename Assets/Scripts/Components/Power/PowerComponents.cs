using Unity.Entities;

/// <summary>전신주·주 시설 가상 전신주의 참가 태그. 식별자는 Entity, 설치 순서는 PlacementStamp다.</summary>
public struct PowerPole : IComponentData { }

/// <summary>발전 참가자 태그. 능력은 자신의 BuildingType 종류·Tier로 BuildingConfig에서 조회한다.</summary>
public struct PowerGenerator : IComponentData { }

/// <summary>소비 참가자 태그. 정격 설정과 이번 틱 PowerDemand를 구분한다. 동적 충전 수요도 이 계약을 쓴다.</summary>
public struct PowerConsumer : IComponentData { }

/// <summary>전신주와 별도 Entity인 전력망 태그. 토폴로지가 생성/병합하며 제거/분리는 Task 8.3이다.</summary>
public struct PowerGrid : IComponentData { }

/// <summary>토폴로지 소유자가 게시하는 파생 연결. 생산·충전은 이 내부 연결 대신 공급 결과를 읽는다.</summary>
public struct PowerGridConnection : IComponentData
{
    public Entity PowerPole;
    public Entity Grid;
}

/// <summary>연료 종류·소비처에 독립적인 영속 에너지 잔량. 연료 처리 소유자가 관리하고 보관 연료 실물은 StoredItemElement가 소유한다.</summary>
public struct FuelEnergyState : IComponentData
{
    public float RemainingEnergy;
}

/// <summary>소비 도메인이 Decision에서 초기화/작성한다. 활성은 수요 준비 완료이며 수요 0도 준비 완료다.</summary>
public struct PowerDemand : IComponentData, IEnableableComponent
{
    public float RequestedEnergy;
}

/// <summary>전력 소유자가 매 틱 먼저 0으로 초기화하고 한 번 게시하는 공급 예산. AllocatedEnergy는 실제 사용량이 아니다.</summary>
public struct PowerSupplyResult : IComponentData
{
    public float SupplyRatio;
    public float AllocatedEnergy;
}

/// <summary>소비 도메인이 매 틱 0으로 초기화하고 자기 배정 한도 안에서 기록하는 실제 사용량. 정산이 읽는다.</summary>
public struct PowerUsageResult : IComponentData
{
    public float UsedEnergy;
}

/// <summary>Decision의 발전 가능량과 Reservation의 배정 계획. 활성은 가용량 준비 완료이며 실제 발전량은 아니다.</summary>
public struct PowerGeneratorSupplyPlan : IComponentData, IEnableableComponent
{
    public float AvailableEnergy;
    public float AllocatedEnergy;
    /// <summary>Reservation에서 예산을 배정한 이번 틱 대표. 게시 전에 연결 기준이 바뀌지 않았는지 확인한다.</summary>
    public Entity RepresentativePole;
}

/// <summary>
/// Reservation이 작성하는 소비자별 미게시 공급 예산. 활성은 배정 준비 완료다.
/// Execution이 최종 검사한 뒤 PowerSupplyResult에 게시한다. 망 집계/연결 원본을 복제하지 않는다.
/// </summary>
public struct PowerSupplyReservation : IComponentData, IEnableableComponent
{
    /// <summary>배정 기준 수요. 게시 전에 입력 수요가 바뀌지 않았는지 확인한다.</summary>
    public float RequestedEnergy;
    public float SupplyRatio;
    public float AllocatedEnergy;
    public Entity RepresentativePole;
}

