using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

/// <summary>압축 길이와 헤더/압축 본문의 해시를 확인한 뒤 바이너리 스키마에 전달한다.</summary>
internal static class GameSaveContainer
{
    private const uint Magic = 0x5a474d50; // PMGZ
    private const ushort Version = 1;
    private const int HashLength = 32;
    private const int HashOffset = sizeof(uint) + sizeof(ushort) + sizeof(long);
    private const int HeaderSize = HashOffset + HashLength;
    // 내부 스키마는 섹션 4개에 각각 128 MiB를 허용한다. 압축 형식의 오버헤드도 포함한다.
    private const long MaxCompressedBytes = 520L * 1024 * 1024;

    public static void Write(Stream file, GameWorldSnapshot snapshot, DateTimeOffset savedAt)
    {
        file.Position = HeaderSize;
        using (var compressed = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true))
        {
            GameWorldBinarySnapshot.Write(compressed, snapshot, savedAt);
        }
        long length = file.Length - HeaderSize;
        file.Position = 0;
        using var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(length);
        byte[] checksum = HashContent(file);
        file.Position = HashOffset;
        writer.Write(checksum);
    }

    public static GameWorldSnapshot Read(Stream file, out DateTimeOffset savedAt, out string identity)
    {
        using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("저장 파일 식별자가 올바르지 않습니다.");
        }
        ushort version = reader.ReadUInt16();
        long length = reader.ReadInt64();
        if (length <= 0 || length > MaxCompressedBytes || length != file.Length - HeaderSize)
        {
            throw new InvalidDataException("압축 저장 길이가 올바르지 않습니다.");
        }
        byte[] expected = reader.ReadBytes(HashLength);
        byte[] actual = HashContent(file);
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            throw new InvalidDataException("압축 저장 바이트가 손상되었습니다.");
        }
        if (version != Version)
        {
            throw new NotSupportedException("지원하지 않는 압축 저장 버전입니다.");
        }
        file.Position = HeaderSize;
        using var compressed = new GZipStream(file, CompressionMode.Decompress, leaveOpen: true);
        identity = Convert.ToBase64String(actual);
        return GameWorldBinarySnapshot.Read(compressed, out savedAt);
    }

    // 이후 컨테이너 버전도 이 헤더 검증 방식으로 손상과 미지원 버전을 구분할 수 있게 한다.
    private static byte[] HashContent(Stream file)
    {
        file.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
        hash.AppendData(reader.ReadBytes(HashOffset));
        file.Position = HeaderSize;
        var buffer = new byte[8192];
        int count;
        while ((count = file.Read(buffer, 0, buffer.Length)) != 0)
        {
            hash.AppendData(buffer, 0, count);
        }
        return hash.GetHashAndReset();
    }
}
