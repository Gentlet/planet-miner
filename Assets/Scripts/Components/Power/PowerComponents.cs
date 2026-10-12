using Unity.Entities;

/// <summary>전신주·주 시설 가상 전신주의 등록 상태. StableId는 토폴로지 소유자가 발급하며 0은 미등록이다.</summary>
public struct PowerPole : IComponentData
{
    public ulong StableId;
}

/// <summary>발전 참가자 태그. 능력은 자신의 BuildingType 종류·Tier로 BuildingConfig에서 조회한다.</summary>
public struct PowerGenerator : IComponentData { }

/// <summary>소비 참가자 태그. 정격 설정과 이번 틱 PowerDemand를 구분한다. 동적 충전 수요도 이 계약을 쓴다.</summary>
public struct PowerConsumer : IComponentData { }

/// <summary>토폴로지 소유자가 관리하는 망 식별자. ID 규칙과 실제 망 생성은 Task 8.2~8.3에서 구현한다.</summary>
public struct PowerGrid : IComponentData
{
    public ulong StableId;
}

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

/// <summary>소비 도메인이 매 틱 초기화하고 전력 외 작업 조건으로 작성하는 수요. 같은 틱에 다시 배분하지 않는다.</summary>
public struct PowerDemand : IComponentData
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

/// <summary>전력 소유자가 매 틱 초기화하고 작성하는 발전 가능량·배정 계획. 실제 연료 실물은 Execution에서 보관 버퍼를 재검사한다.</summary>
public struct PowerGeneratorSupplyPlan : IComponentData
{
    public float AvailableEnergy;
    public float AllocatedEnergy;
}

