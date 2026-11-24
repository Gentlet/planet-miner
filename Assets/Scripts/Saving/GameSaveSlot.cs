using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if UNITY_EDITOR
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor")]
#endif

public enum GameSaveStatusEnum
{
    Missing,
    Success,
    Corrupt,
    Incompatible,
    IoError,
    AlreadyExists,
    ConfirmOverwrite
}

internal readonly struct GameWorldReadResult
{
    public readonly GameSaveStatusEnum Status;
    public readonly GameWorldSnapshot Snapshot;
    public readonly DateTimeOffset SavedAtUtc;
    public readonly string Identity;

    public GameWorldReadResult(GameSaveStatusEnum status, GameWorldSnapshot snapshot = null,
        DateTimeOffset savedAt = default, string identity = null)
    {
        Status = status;
        Snapshot = snapshot;
        SavedAtUtc = savedAt;
        Identity = identity;
    }
}

/// <summary>슬롯의 압축 저장 파일을 검증한 뒤 단일 본 파일을 원자적으로 게시한다.</summary>
public sealed class GameSaveSlot
{
    public const string ContentVersion = "v2-dev-1";
    public const int WorldGeneratorVersion = 1;
    internal const string DirectoryName = "Saves";
    internal const string PrimaryName = "slot-01.sav";

    private static readonly SemaphoreSlim SlotAccess = new(1, 1);
    private readonly string _directory;
    private readonly string _primaryPath;
    private readonly string _temporaryPath;

    // 이어하기 또는 직전 저장으로 확인한 본 파일과 다른 기존 파일을 교체하려면 별도 사용자 선택이 필요하다.
    private volatile string _trustedPrimary;
    private volatile string _pendingOverwriteIdentity;

    public GameSaveSlot(string persistentDataPath) : this(new DirectoryInfo(Path.Combine(persistentDataPath, DirectoryName)))
    {
    }

    internal GameSaveSlot(DirectoryInfo directory)
    {
        _directory = directory.FullName;
        _primaryPath = Path.Combine(_directory, PrimaryName);
        _temporaryPath = _primaryPath + ".tmp";
    }

    internal Task<GameWorldReadResult> ReadWorldAsync() => AccessOnWorkerAsync(() => ReadFile(_primaryPath));

    internal Task<GameSaveStatusEnum> WriteAsync(GameWorldSnapshot snapshot) =>
        AccessOnWorkerAsync(() => Publish(snapshot, DateTimeOffset.UtcNow));

    internal Task<GameSaveStatusEnum> WriteConfirmedAsync(GameWorldSnapshot snapshot)
    {
        string confirmed = _pendingOverwriteIdentity;
        return AccessOnWorkerAsync(() => Publish(snapshot, DateTimeOffset.UtcNow, confirmedIdentity: confirmed));
    }

    internal void ConfirmRestored(GameWorldReadResult result)
    {
        if (result.Status == GameSaveStatusEnum.Success)
        {
            _trustedPrimary = result.Identity;
        }
    }

#if UNITY_EDITOR
    internal Task<GameSaveStatusEnum> ImportAsync(GameWorldReadResult converted) =>
        AccessOnWorkerAsync(() => Publish(converted.Snapshot, converted.SavedAtUtc, createOnly: true));
#endif

    private static async Task<TResult> AccessOnWorkerAsync<TResult>(Func<TResult> operation)
    {
        await SlotAccess.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(operation).ConfigureAwait(false);
        }
        finally
        {
            SlotAccess.Release();
        }
    }

    private GameSaveStatusEnum Publish(GameWorldSnapshot snapshot, DateTimeOffset savedAt, bool createOnly = false,
        string confirmedIdentity = null)
    {
        try
        {
            using var writeLock = OpenWriteLock();
            var existing = ReadFile(_primaryPath);
            if (existing.Status == GameSaveStatusEnum.IoError ||
                (existing.Status == GameSaveStatusEnum.Incompatible && existing.Identity == null))
            {
                return existing.Status;
            }
            if (createOnly && existing.Status != GameSaveStatusEnum.Missing)
            {
                return GameSaveStatusEnum.AlreadyExists;
            }
            string identity = existing.Identity;
            if (existing.Status == GameSaveStatusEnum.Corrupt)
            {
                // 읽을 수 없는 파일도 사용자 동의는 확인 당시의 바이트에만 적용한다.
                using var file = File.OpenRead(_primaryPath);
                using var hash = SHA256.Create();
                identity = "corrupt:" + Convert.ToBase64String(hash.ComputeHash(file));
            }
            if (identity != null && identity != _trustedPrimary && identity != confirmedIdentity)
            {
                _pendingOverwriteIdentity = identity;
                return GameSaveStatusEnum.ConfirmOverwrite;
            }

            GameWorldReadResult pending = WriteAndVerify(_temporaryPath, snapshot, savedAt);
            if (pending.Status != GameSaveStatusEnum.Success)
            {
                return pending.Status;
            }
            if (existing.Status == GameSaveStatusEnum.Missing)
            {
                File.Move(_temporaryPath, _primaryPath);
            }
            else
            {
                File.Replace(_temporaryPath, _primaryPath, null);
            }
            _trustedPrimary = pending.Identity;
            _pendingOverwriteIdentity = null;
            return GameSaveStatusEnum.Success;
        }
        catch (Exception error) when (TryFileStatus(error, out var status))
        {
            return status;
        }
    }

    private FileStream OpenWriteLock()
    {
        Directory.CreateDirectory(_directory);
        return new FileStream(_primaryPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static GameWorldReadResult WriteAndVerify(string path, GameWorldSnapshot snapshot, DateTimeOffset savedAt)
    {
        using (var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            GameSaveContainer.Write(file, snapshot, savedAt);
            file.Flush(flushToDisk: true);
        }
        return ReadFile(path);
    }

    private static GameWorldReadResult ReadFile(string path)
    {
        string identity = null;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            GameWorldSnapshot snapshot = GameSaveContainer.Read(file, out DateTimeOffset savedAt, out identity);
            return new GameWorldReadResult(GameSaveStatusEnum.Success, snapshot, savedAt, identity);
        }
        catch (RetiredSaveSchemaException)
        {
            // 확인된 이전 파일의 해시를 기존 덮어쓰기 확인에 사용한다.
            return new GameWorldReadResult(GameSaveStatusEnum.Incompatible, identity: identity);
        }
        catch (Exception error) when (TryFileStatus(error, out var status))
        {
            return new GameWorldReadResult(status);
        }
    }

    private static bool TryFileStatus(Exception error, out GameSaveStatusEnum status)
    {
        switch (error)
        {
            case FileNotFoundException:
            case DirectoryNotFoundException:
                status = GameSaveStatusEnum.Missing;
                return true;
            case InvalidDataException:
            case EndOfStreamException:
            case DecoderFallbackException:
            case EncoderFallbackException:
                status = GameSaveStatusEnum.Corrupt;
                return true;
            case NotSupportedException:
                status = GameSaveStatusEnum.Incompatible;
                return true;
            case IOException:
            case UnauthorizedAccessException:
                status = GameSaveStatusEnum.IoError;
                return true;
            default:
                status = default;
                return false;
        }
    }
}
