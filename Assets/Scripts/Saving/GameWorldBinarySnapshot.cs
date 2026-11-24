using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Entities;

/// <summary>완료 틱의 스냅샷을 받아 헤더와 섹션으로 구성된 바이너리 스트림을 읽고 쓴다.</summary>
internal static class GameWorldBinarySnapshot
{
    private const uint Magic = 0x56534d50; // PMSV
    internal const ushort CurrentSchemaVersion = 4;
    private const int MaxSectionBytes = 128 * 1024 * 1024;

    private enum Section : byte
    {
        World = 1,
        BuildingsAndItems = 2,
        ResearchAndConstruction = 3,
        Drones = 4
    }

    public static void Write(Stream destination, GameWorldSnapshot snapshot, DateTimeOffset savedAt)
    {
        ValidateSnapshot(snapshot);
        using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(CurrentSchemaVersion);
        writer.Write(savedAt.UtcDateTime.Ticks);
        TransferContentVersions(new GameSnapshotArchive(writer));
        WriteSection(writer, Section.World, snapshot, GameWorldBinaryLayout.World);
        WriteSection(writer, Section.BuildingsAndItems, snapshot, GameWorldBinaryLayout.BuildingsAndItems);
        WriteSection(writer, Section.ResearchAndConstruction, snapshot, GameWorldBinaryLayout.ResearchAndConstruction);
        WriteSection(writer, Section.Drones, snapshot, DroneFields);
    }

    public static GameWorldSnapshot Read(Stream source, out DateTimeOffset savedAt)
    {
        using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("바이너리 스냅샷 식별자가 올바르지 않습니다.");
        }
        ushort version = reader.ReadUInt16();
        if (version >= 1 && version < CurrentSchemaVersion)
        {
            throw new RetiredSaveSchemaException();
        }
        if (version != CurrentSchemaVersion)
        {
            throw new NotSupportedException("지원하지 않는 바이너리 저장 버전입니다.");
        }
        long utcTicks = reader.ReadInt64();
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
        {
            throw new InvalidDataException("바이너리 저장 시각이 올바르지 않습니다.");
        }
        savedAt = new DateTimeOffset(utcTicks, TimeSpan.Zero);
        TransferContentVersions(new GameSnapshotArchive(reader));
        var snapshot = new GameWorldSnapshot();
        ReadSection(reader, Section.World, snapshot, GameWorldBinaryLayout.World);
        ReadSection(reader, Section.BuildingsAndItems, snapshot, GameWorldBinaryLayout.BuildingsAndItems);
        ReadSection(reader, Section.ResearchAndConstruction, snapshot, GameWorldBinaryLayout.ResearchAndConstruction);
        ReadSection(reader, Section.Drones, snapshot, DroneFields);
        if (source.ReadByte() != -1)
        {
            throw new InvalidDataException("저장 섹션 뒤에 사용하지 않는 데이터가 있습니다.");
        }
        ValidateSnapshot(snapshot);
        return snapshot;
    }

    private static void TransferContentVersions(GameSnapshotArchive archive)
    {
        string content = GameSaveSlot.ContentVersion;
        int generator = GameSaveSlot.WorldGeneratorVersion;
        archive.Value(ref content);
        archive.Value(ref generator);
        if (content != GameSaveSlot.ContentVersion || generator != GameSaveSlot.WorldGeneratorVersion)
        {
            throw new NotSupportedException("지원하지 않는 콘텐츠 또는 월드 생성 버전입니다.");
        }
    }

    private static void WriteSection(BinaryWriter writer, Section section, GameWorldSnapshot snapshot,
        GameSnapshotArchive.Layout<GameWorldSnapshot> layout)
    {
        using var payload = new MemoryStream();
        using (var payloadWriter = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            layout(new GameSnapshotArchive(payloadWriter), ref snapshot);
        }
        if (payload.Length > MaxSectionBytes)
        {
            throw new InvalidDataException("저장 섹션이 너무 큽니다.");
        }
        writer.Write((byte)section);
        writer.Write((int)payload.Length);
        payload.WriteTo(writer.BaseStream);
    }

    private static void ReadSection(BinaryReader reader, Section expected, GameWorldSnapshot snapshot,
        GameSnapshotArchive.Layout<GameWorldSnapshot> layout)
    {
        if (reader.ReadByte() != (byte)expected)
        {
            throw new InvalidDataException($"필수 저장 섹션이 빠졌거나 순서가 잘못되었습니다. {expected}");
        }
        int length = reader.ReadInt32();
        if (length <= 0 || length > MaxSectionBytes)
        {
            throw new InvalidDataException("저장 섹션의 길이가 잘못되었습니다.");
        }
        Stream source = reader.BaseStream;
        if (source.CanSeek && length > source.Length - source.Position)
        {
            throw new EndOfStreamException();
        }
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException();
        }
        using var payload = new MemoryStream(bytes, writable: false);
        using var payloadReader = new BinaryReader(payload, Encoding.UTF8);
        layout(new GameSnapshotArchive(payloadReader), ref snapshot);
        if (payload.Position != payload.Length)
        {
            throw new InvalidDataException($"저장 섹션의 내용과 길이가 일치하지 않습니다. {expected}");
        }
    }

    private static void DroneFields(GameSnapshotArchive archive, ref GameWorldSnapshot snapshot)
    {
        archive.Record(ref snapshot.DroneLogistics, (GameSnapshotArchive data, ref SavedDroneWorld drones) =>
        {
            data.Maybe(ref drones.NextActionReceipt, TransferSequence);
            data.Maybe(ref drones.NextStationRequest, TransferSequence);
            data.Maybe(ref drones.NextTaskRequest, TransferSequence);
            data.List(ref drones.References, (GameSnapshotArchive item, ref string id) => item.Value(ref id));
            data.List(ref drones.Entities, ReadOrWriteDrone);
        });
    }

    private static void TransferSequence(GameSnapshotArchive archive, ref ulong sequence) => archive.Value(ref sequence);

    private static void ReadOrWriteDrone(GameSnapshotArchive archive, ref SavedDroneEntity entity)
    {
        if (archive.Reading)
        {
            entity = new SavedDroneEntity();
        }
        if (entity == null)
        {
            throw new InvalidDataException("드론 저장 기록이 없습니다.");
        }
        archive.Value(ref entity.Id);
        archive.EnumValue(ref entity.Kind);
        archive.Value(ref entity.Disabled);
        if (entity.Kind == SavedDroneKindEnum.Worker)
        {
            if (archive.Reading)
            {
                entity.Position = new float[3];
            }
            for (int axis = 0; axis < 3; axis++)
            {
                archive.Value(ref entity.Position[axis]);
            }
        }
        GameDroneSaveSchema.Transfer(archive, entity);
    }

    private static void ValidateSnapshot(GameWorldSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }
        if (snapshot.IsLegacyFormat || snapshot.PassiveDroneTasks != null)
        {
            throw new NotSupportedException("바이너리 저장에는 현재 월드에서 캡처한 스냅샷이 필요합니다.");
        }
        var drones = snapshot.DroneLogistics;
        if (drones == null)
        {
            throw new InvalidDataException("드론 상태가 없습니다.");
        }
        GameDroneSave.ValidateFileStructure(drones);
        var worldIds = new HashSet<string>(StringComparer.Ordinal);
        var buildings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var building in snapshot.Buildings)
        {
            AddId(worldIds, building?.Id);
            buildings.Add(building.Id);
        }
        foreach (var site in snapshot.ConstructionSites)
        {
            AddId(worldIds, site?.Id);
            buildings.Add(site.Id);
        }
        foreach (var item in snapshot.WorldItems)
        {
            AddId(worldIds, item?.Id);
        }

        var knownIds = new HashSet<string>(worldIds, StringComparer.Ordinal);
        foreach (var row in drones.Entities)
        {
            switch (row.Kind)
            {
                case SavedDroneKindEnum.Building:
                    if (!buildings.Contains(row.Id))
                    {
                        throw new InvalidDataException("드론 상태의 건물 기록이 없습니다.");
                    }
                    break;
                case SavedDroneKindEnum.Item:
                    if (!worldIds.Contains(row.Id) || buildings.Contains(row.Id))
                    {
                        throw new InvalidDataException("드론 상태의 아이템 기록이 없습니다.");
                    }
                    break;
                default:
                    AddId(knownIds, row.Id);
                    break;
            }
        }
        var referenceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in drones.References)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException("드론 저장 참조가 비어 있습니다.");
            }
            if (!referenceIds.Add(id))
            {
                throw new InvalidDataException("드론 저장 참조가 중복됩니다.");
            }
            if (!knownIds.Contains(id) && !GameDroneSave.IsRemovedReference(id))
            {
                throw new InvalidDataException("드론 상태가 파일에 없는 대상을 참조합니다.");
            }
        }
        var references = new GameSnapshotArchive(reference =>
        {
            GameDroneSave.ReadReferenceId(drones, reference);
            return reference;
        });
        foreach (var row in drones.Entities)
        {
            GameDroneSaveSchema.ValidateReferences(row, references);
        }
    }

    private static void AddId(HashSet<string> ids, string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new InvalidDataException("월드 저장 ID가 비어 있습니다.");
        }
        if (GameDroneSave.IsRemovedReference(id))
        {
            throw new InvalidDataException("월드 저장 ID가 예약 이름을 사용합니다.");
        }
        if (!ids.Add(id))
        {
            throw new InvalidDataException("월드 저장 ID가 중복됩니다.");
        }
    }
}

internal sealed class RetiredSaveSchemaException : NotSupportedException
{
    public RetiredSaveSchemaException() : base("이전 바이너리 저장 버전입니다.") { }
}
