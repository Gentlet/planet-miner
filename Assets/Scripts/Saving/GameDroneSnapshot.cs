using System;
using System.Collections.Generic;

internal enum SavedDroneKindEnum
{
    Worker = 0,
    Task = 1,
    Assignment = 2,
    Action = 3,
    StationRequest = 4,
    TaskRequest = 5,
    Route = 6,
    DemolitionCommand = 7,
    Building = 8,
    Item = 9,
    Scheduling = 10
}

// 컴포넌트 복사본에서 0이 아닌 참조 번호는 References의 1부터 시작하는 위치를 가리킨다.
internal sealed class SavedDroneWorld
{
    public List<string> References = new();
    public List<SavedDroneEntity> Entities = new();
    public ulong? NextActionReceipt;
    public ulong? NextStationRequest;
    public ulong? NextTaskRequest;
}

internal sealed class SavedDroneEntity
{
    public string Id;
    public SavedDroneKindEnum Kind;
    public float[] Position;
    public bool Disabled;
    public readonly Dictionary<Type, object> Fields = new();

    public bool Has<T>() => Fields.ContainsKey(typeof(T));

    public bool TryGet<T>(out T value)
    {
        if (Fields.TryGetValue(typeof(T), out object field))
        {
            value = (T)field;
            return true;
        }
        value = default;
        return false;
    }
}
