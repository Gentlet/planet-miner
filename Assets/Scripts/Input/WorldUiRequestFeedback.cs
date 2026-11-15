using System;
using System.Collections.Generic;
using Unity.Entities;

/// <summary>소비한 요청의 값만 화면에 전달한다. ECS 결과나 작업 진행의 원본을 보관하지 않는다.</summary>
public readonly struct WorldUiRequestFeedback
{
    public readonly Entity Request;
    public readonly string Message;
    public WorldUiRequestFeedback(Entity request, string message)
    {
        Request = request;
        Message = message;
    }
}

/// <summary>World/토큰별 화면 등록과 요청 표시 권한을 소유한다. 실제 결과 Entity 소비는 Presentation 시스템이 수행한다.</summary>
public static class WorldUiRequestFeedbackRelay
{
    private sealed class Receiver
    {
        public World World;
        public Action<WorldUiRequestFeedback> Callback;

        public readonly HashSet<Entity> Pending = new HashSet<Entity>();
    }

    private static readonly Dictionary<ulong, Receiver> Receivers = new Dictionary<ulong, Receiver>();
    private static ulong _sequence;
    /// <summary>생존 World에 새 등록 토큰을 발급한다. null/해제된 World는 등록하지 않고 0을 반환한다.</summary>
    public static ulong Register(World world, Action<WorldUiRequestFeedback> callback)
    {
        if (world == null)
        {
            return 0;
        }
        if (!world.IsCreated)
        {
            return 0;
        }
        if (_sequence == ulong.MaxValue)
        {
            throw new InvalidOperationException("UI feedback registration sequence exhausted.");
        }

        ulong token = ++_sequence;
        Receivers.Add(token, new Receiver { World = world, Callback = callback });
        return token;
    }

    public static void Unregister(ulong token)
    {
        Receivers.Remove(token);
    }

    public static void Deliver(EntityManager manager, ulong token, WorldUiRequestFeedback feedback)
    {
        if (!Receivers.TryGetValue(token, out var receiver))
        {
            return;
        }

        if (receiver.World == null)
        {
            return;
        }

        if (!receiver.World.IsCreated)
        {
            return;
        }

        if (!receiver.World.EntityManager.Equals(manager))
        {
            return;
        }

        // 요청은 소비자가 이미 제거했다. 전체 Entity 식별자로 표시 권한만 확인한다.
        if (!receiver.Pending.Remove(feedback.Request))
        {
            return;
        }

        receiver.Callback(feedback);
    }

    public static void Track(EntityManager manager, ulong token, Entity request)
    {
        manager.AddComponentData(request, new WorldUiRequestOwner { FeedbackToken = token });
        if (!Receivers.TryGetValue(token, out var receiver))
        {
            return;
        }
        if (receiver.World == null)
        {
            return;
        }
        if (!receiver.World.IsCreated)
        {
            return;
        }
        if (!receiver.World.EntityManager.Equals(manager))
        {
            return;
        }

        receiver.Pending.Add(request);
    }

    public static void ClearPending(ulong token)
    {
        if (Receivers.TryGetValue(token, out var receiver))
        {
            receiver.Pending.Clear();
        }
    }
}
