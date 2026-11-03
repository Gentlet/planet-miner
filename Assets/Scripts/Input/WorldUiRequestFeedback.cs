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

/// <summary>화면 등록 수명만 연결한다. 비활성화/World 교체 시 해제하고 재등록에는 새 토큰을 사용한다.</summary>
public static class WorldUiRequestFeedbackRelay
{
    private sealed class Receiver
    {
        public World World;
        public Action<WorldUiRequestFeedback> Callback;
    }

    private static readonly Dictionary<ulong, Receiver> Receivers = new Dictionary<ulong, Receiver>();
    private static ulong _sequence;
    public static ulong Register(World world, Action<WorldUiRequestFeedback> callback)
    {
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

        receiver.Callback(feedback);
    }
}
