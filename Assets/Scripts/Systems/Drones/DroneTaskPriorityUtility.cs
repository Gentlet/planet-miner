public static class DroneTaskPriorityUtility
{
    public const int MinimumNormalPriority = 1;
    public const int MaximumNormalPriority = 10;

    public static bool IsValidNormalPriority(int priority)
    {
        return priority >= MinimumNormalPriority &&
               priority <= MaximumNormalPriority;
    }

    public static bool IsValidPriorityClass(
        DroneTaskPriorityClassEnum priorityClass)
    {
        return priorityClass < DroneTaskPriorityClassEnum.Count;
    }

    public static bool IsHigherPriority(
        DroneTaskPriority candidatePriority,
        ulong candidateCreationOrder,
        DroneTaskPriority currentPriority,
        ulong currentCreationOrder)
    {
        if (candidatePriority.priorityClass != currentPriority.priorityClass)
        {
            return candidatePriority.priorityClass ==
                   DroneTaskPriorityClassEnum.Emergency;
        }

        if (candidatePriority.priorityClass ==
                DroneTaskPriorityClassEnum.Normal &&
            candidatePriority.normalPriority != currentPriority.normalPriority)
        {
            return candidatePriority.normalPriority <
                   currentPriority.normalPriority;
        }

        return candidateCreationOrder < currentCreationOrder;
    }
}
