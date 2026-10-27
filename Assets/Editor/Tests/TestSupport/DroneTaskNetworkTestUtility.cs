using Unity.Entities;

namespace PlanetMiner.Tests
{
    /// <summary>경로/배정 fixture가 직접 제공하던 수행자 관측과 함께 이번 틱의 정거장 판단 입력도 명시한다.</summary>
    public static class DroneTaskNetworkTestUtility
    {
        public static void PrepareWideNetwork(EntityManager manager)
        {
            Entity station = manager.CreateEntity(typeof(DroneStation), typeof(DroneStationNetworkDecision));
            manager.SetComponentData(station, new DroneStationNetworkDecision
            {
                RepresentativeStation = station,
                ActivityBounds = new DroneStationActivityBounds { MinX = -1024, MinY = -1024, MaxX = 1024, MaxY = 1024 }
            });
        }
    }
}
