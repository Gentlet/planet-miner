using Unity.Entities;

/// <summary>전신주·주 시설 가상 전신주의 참가 태그. 식별자는 Entity, 설치 순서는 PlacementStamp다.</summary>
public struct PowerPole : IComponentData { }

/// <summary>발전 역할. 활성은 이번 틱 양수 가용량이며 능력은 자신의 종류·Tier 설정에서 조회한다.</summary>
public struct PowerGenerator : IComponentData, IEnableableComponent { }

/// <summary>소비 역할. 활성은 이번 틱 양수 요청이며 공급 부족으로 비활성화하지 않는다.</summary>
public struct PowerConsumer : IComponentData, IEnableableComponent { }

/// <summary>전신주와 별도 Entity인 전력망 태그. 토폴로지가 생성·병합·분리·빈 망 삭제를 기록한다.</summary>
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

/// <summary>Preparation이 초기화/작성한다. 활성은 수요 준비 완료이며 수요 0도 준비 완료다.</summary>
public struct PowerDemand : IComponentData, IEnableableComponent
{
    public float RequestedEnergy;
}

/// <summary>Power에서 소비로 확정한 이번 틱 에너지. 후속 작업이 실행되지 않아도 환급하지 않는다.</summary>
public struct PowerSupplyResult : IComponentData
{
    public float SupplyRatio;
    public float AllocatedEnergy;
}

/// <summary>Decision의 가용량과 Reservation의 확정 소비 계획. 활성은 준비 완료이며 정상 0도 준비 완료다.</summary>
public struct PowerGeneratorSupplyPlan : IComponentData, IEnableableComponent
{
    public float AvailableEnergy;
    public float AllocatedEnergy;
    public float ExpectedFuelEnergy;
    public int FuelItemCount;
    public float RemainingEnergyAfterConsumption;
}


/// <summary>
/// Reservation이 작성하는 소비자별 미게시 공급 예산. 활성은 배정 준비 완료다.
/// Execution이 준비된 값을 PowerSupplyResult에 게시한다. 망 집계/연결 원본을 복제하지 않는다.
/// </summary>
public struct PowerSupplyReservation : IComponentData, IEnableableComponent
{
    public float SupplyRatio;
    public float AllocatedEnergy;
}

