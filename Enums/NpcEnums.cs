// Enums/NpcEnums.cs
public enum NpcFaction { Enemy, Neutral, Friendly }

public enum NpcAiType
{
    Passive_Aggressive,
    Patrolling_Aggressive,
    Wandering_Aggressive,
    Ambient_Fleeing,
    Ambient_Passive
}

public enum NpcAiState { Idle, Patrolling, Chasing, Attacking, ReturningToSpawn, Fleeing, Dead, Wandering }