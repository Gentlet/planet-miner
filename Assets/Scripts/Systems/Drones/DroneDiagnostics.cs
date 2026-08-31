using Unity.Entities;
using UnityEngine;
using Conditional = System.Diagnostics.ConditionalAttribute;

public static class DroneDiagnostics
{
    public static bool LifecycleLoggingEnabled { get; set; }

    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void LogTaskCreated(
        Entity taskEntity,
        DroneTaskTypeEnum taskType,
        ulong creationOrder)
    {
        if (!LifecycleLoggingEnabled)
            return;

        Debug.Log(
            $"[DroneLifecycle] Created task. Task : {taskEntity}, " +
            $"Type : {taskType}, CreationOrder : {creationOrder}");
    }

    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void LogTaskPriorityChanged(
        Entity taskEntity,
        DroneTaskPriorityClassEnum priorityClass,
        int normalPriority)
    {
        if (!LifecycleLoggingEnabled)
            return;

        Debug.Log(
            $"[DroneLifecycle] Changed task priority. Task : {taskEntity}, " +
            $"Class : {priorityClass}, Priority : {normalPriority}");
    }

    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void LogTaskSuspensionChanged(
        Entity taskEntity,
        DroneTaskStateEnum taskState)
    {
        if (!LifecycleLoggingEnabled)
            return;

        Debug.Log(
            $"[DroneLifecycle] Changed task suspension. Task : {taskEntity}, " +
            $"State : {taskState}");
    }

    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void LogTaskCancelled(Entity taskEntity)
    {
        if (!LifecycleLoggingEnabled)
            return;

        Debug.Log($"[DroneLifecycle] Cancelled task. Task : {taskEntity}");
    }

    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void LogReservationCreated(
        Entity reservationEntity,
        Entity taskEntity,
        ItemTypeEnum itemType,
        int quantity)
    {
        if (!LifecycleLoggingEnabled)
            return;

        Debug.Log(
            $"[DroneLifecycle] Created reservation. Reservation : {reservationEntity}, " +
            $"Task : {taskEntity}, Item : {itemType}, Quantity : {quantity}");
    }

    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    public static void LogReservationReleased(
        Entity reservationEntity,
        Entity taskEntity)
    {
        if (!LifecycleLoggingEnabled)
            return;

        Debug.Log(
            $"[DroneLifecycle] Released reservation. Reservation : {reservationEntity}, " +
            $"Task : {taskEntity}");
    }
}
