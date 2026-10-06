using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Up→Right→Down→Left의 4방향 번호를 정의하여 회전·포트 계산과 직렬화 기준을 제공한다.
/// 부착 엔티티: 열거형 자체는 부착하지 않으며 Direction·요청·경로 값에 포함한다. Count는 실제 방향이 아니다.
/// 생성·이용: 생성/배치 경계가 방향을 선택하고 DirectionExtensions·RoutingDirectionUtility 및 벨트/공간 시스템이 값을 해석한다.
/// 제거: 값을 포함한 컴포넌트·요청의 수명을 따른다. 기존 번호·순서를 바꾸면 회전과 직렬화 의미가 달라진다.
/// </summary>
public enum DirectionEnum : int
{
    Up,
    Right,
    Down,
    Left,
    Count
}

/// <summary>
/// 역할·목적: 엔티티의 격자 좌표 원본이다. 건물·현장은 footprint의 좌하단 기준점, 월드 아이템·자원은 점유 셀을 뜻한다.
/// 부착 엔티티: 완공 건물·공사 현장·자원 노드·아이템 실물에 부착한다.
/// 생성: BuildingPlacementCommandSystem(Command), ResourceGenerationCommandSystem(Command), BuildingLifecycleUtility/ItemLifecycleUtility의 ECB 초기화에서 설정한다.
/// 이용: 배치·채굴·벨트·드론·완공 판단이 읽고 BeltMovementExecutionSystem(Execution), RoutingApplySystem/BuildingItemStorageApplySystem/ItemOwnershipApplySystem(StateApply)이 실물 이동에 갱신한다.
/// 각 SpatialSyncSystem(Synchronization)이 이 좌표를 인덱스에 등록한다. LocalTransform만 바꾼 것으로 격자 이동을 확정하지 않는다.
/// 제거: 실물·건물·현장 엔티티 삭제 시 함께 제거한다. 수납 아이템의 월드 점유 여부는 좌표가 아닌 ItemOwnership으로 구분한다.
/// </summary>
public struct GridPosition : IComponentData
{
    public int2 Value;

    public GridPosition(int2 position)
    {
        Value = position;
    }

    public GridPosition(int x, int y)
    {
        Value = new int2(x, y);
    }

    public static implicit operator int2(GridPosition pos) => pos.Value;
    public static implicit operator GridPosition(int2 pos) => new GridPosition(pos);
}

/// <summary>
/// 역할·목적: 건물 회전과 벨트·아이템 진행 방향의 원본을 제공한다.
/// 부착 엔티티: 방향이 필요한 완공 건물·공사 현장 및 벨트 인계가 설정한 아이템 엔티티다.
/// 생성: BuildingPlacementCommandSystem(Command)과 BuildingLifecycleUtility가 건물/현장 생성에 붙인다. 아이템 생성 Utility는 Direction을 추가하지 않으며 인계는 이미 존재하는 경우에만 갱신한다.
/// 이용: 공간/footprint·벨트·입출고·라우팅 판단이 읽는다. BeltMovementExecutionSystem(Execution)과 RoutingApplySystem/BuildingItemStorageApplySystem(StateApply)이 벨트 이동 방향을 갱신한다.
/// 제거: 엔티티 삭제 시 함께 제거한다. 방향 변경이 Synchronization 이전 공간 인덱스의 즉시 변경을 뜻하지 않는다.
/// </summary>
public struct Direction : IComponentData
{
    public DirectionEnum dir;

    public Direction(DirectionEnum direction)
    {
        dir = direction;
    }
}
