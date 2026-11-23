using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>고정된 필드 순서로 읽기, 쓰기 또는 참조 변환을 수행한다. 참조 변환에서는 값은 보존하고 Entity 필드만 방문한다.</summary>
internal sealed class GameSnapshotArchive
{
    internal delegate void Layout<T>(GameSnapshotArchive archive, ref T value);

    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private const int MaxStringBytes = 4096;
    private const int MaxEntries = 1_000_000;
    private readonly BinaryReader _reader;
    private readonly BinaryWriter _writer;
    private readonly Func<Entity, Entity> _remap;

    public GameSnapshotArchive(BinaryReader reader) => _reader = reader;
    public GameSnapshotArchive(BinaryWriter writer) => _writer = writer;
    public GameSnapshotArchive(Func<Entity, Entity> remap) => _remap = remap;

    public bool Reading => _reader != null;

    public void Value(ref byte value)
    {
        if (Reading)
        {
            value = _reader.ReadByte();
        }
        else
        {
            _writer?.Write(value);
        }
    }

    public void Value(ref int value)
    {
        if (Reading)
        {
            value = _reader.ReadInt32();
        }
        else
        {
            _writer?.Write(value);
        }
    }

    public void Value(ref uint value)
    {
        if (Reading)
        {
            value = _reader.ReadUInt32();
        }
        else
        {
            _writer?.Write(value);
        }
    }

    public void Value(ref ulong value)
    {
        if (Reading)
        {
            value = _reader.ReadUInt64();
        }
        else
        {
            _writer?.Write(value);
        }
    }

    public void Value(ref float value)
    {
        if (Reading)
        {
            value = _reader.ReadSingle();
        }
        else
        {
            _writer?.Write(value);
        }
    }

    public void Value(ref double value)
    {
        if (Reading)
        {
            value = _reader.ReadDouble();
        }
        else
        {
            _writer?.Write(value);
        }
    }


    public void Value(ref bool value)
    {
        byte flag = value ? (byte)1 : (byte)0;
        Value(ref flag);
        if (flag > 1)
        {
            throw new InvalidDataException("바이너리 저장의 플래그 값이 잘못되었습니다.");
        }
        value = flag != 0;
    }

    public void Value(ref string value)
    {
        if (Reading)
        {
            int length = _reader.ReadInt32();
            if (length == -1)
            {
                value = null;
                return;
            }
            if (length < 0 || length > MaxStringBytes)
            {
                throw new InvalidDataException("저장 문자열 길이가 잘못되었습니다.");
            }
            byte[] bytes = _reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new EndOfStreamException();
            }
            value = Utf8.GetString(bytes);
        }
        else if (_writer != null)
        {
            byte[] bytes = value == null ? null : Utf8.GetBytes(value);
            if (bytes != null && bytes.Length > MaxStringBytes)
            {
                throw new InvalidDataException("저장 문자열이 너무 깁니다.");
            }
            _writer.Write(bytes?.Length ?? -1);
            if (bytes != null)
            {
                _writer.Write(bytes);
            }
        }
    }

    public void Value(ref int2 value)
    {
        Value(ref value.x);
        Value(ref value.y);
    }

    public void Value(ref float2 value)
    {
        Value(ref value.x);
        Value(ref value.y);
    }

    public void Value(ref float3 value)
    {
        Value(ref value.x);
        Value(ref value.y);
        Value(ref value.z);
    }

    public void EntityReference(ref Entity value)
    {
        if (_remap != null)
        {
            value = _remap(value);
            return;
        }
        int reference = value.Index;
        Value(ref reference);
        if (reference < 0 || value.Version != 0)
        {
            throw new InvalidDataException("저장 참조 번호가 잘못되었습니다.");
        }
        value = new Entity { Index = reference };
    }

    public void EnumValue<T>(ref T value) where T : struct, Enum
    {
        if (_remap != null)
        {
            return;
        }
        int number = Reading ? 0 : Convert.ToInt32(value);
        Value(ref number);
        if (Reading)
        {
            value = (T)Enum.ToObject(typeof(T), number);
            if (Convert.ToInt32(value) != number || !Enum.IsDefined(typeof(T), value))
            {
                throw new InvalidDataException($"저장한 {typeof(T).Name} 값이 잘못되었습니다.");
            }
        }
    }

    public void Cell(ref int[] cell)
    {
        int2 coordinates = Reading ? default : GameWorldSnapshot.ReadCell(cell);
        Value(ref coordinates);
        if (Reading)
        {
            cell = GameWorldSnapshot.WriteCell(coordinates);
        }
    }

    public void Maybe<T>(ref T? value, Layout<T> layout) where T : struct
    {
        bool present = value.HasValue;
        Value(ref present);
        T entry = value.GetValueOrDefault();
        if (present)
        {
            layout(this, ref entry);
        }
        value = present ? entry : null;
    }

    public void Record<T>(ref T value, Layout<T> layout) where T : class, new()
    {
        if (Reading)
        {
            value = new T();
        }
        if (value == null)
        {
            throw new InvalidDataException($"필수 저장 기록이 없습니다. {typeof(T).Name}");
        }
        layout(this, ref value);
    }

    public void OptionalRecord<T>(ref T value, Layout<T> layout) where T : class, new()
    {
        bool present = value != null;
        Value(ref present);
        if (present)
        {
            Record(ref value, layout);
        }
        else
        {
            value = null;
        }
    }

    public void List<T>(ref List<T> values, Layout<T> layout)
    {
        if (!Reading && values == null)
        {
            throw new InvalidDataException("필수 저장 목록이 없습니다.");
        }
        int count = Reading ? 0 : values.Count;
        Count(ref count);
        if (Reading)
        {
            values = new List<T>(Math.Min(count, 256));
        }
        for (int index = 0; index < count; index++)
        {
            T value = Reading ? default : values[index];
            layout(this, ref value);
            if (Reading)
            {
                values.Add(value);
            }
        }
    }

    public void Count(ref int count)
    {
        Value(ref count);
        if (count < 0 || count > MaxEntries)
        {
            throw new InvalidDataException("저장 목록의 길이가 범위를 벗어났습니다.");
        }
    }
}
