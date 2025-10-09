// Enums/NpcEnums.cs
public enum NpcFaction { Enemy, Neutral, Friendly }

public enum NpcAiType
{
    // Agressivos
    Passive_Aggressive,       // Fica parado, ataca se for atacado
    Patrolling_Aggressive,    // Patrulha, ataca se vir um jogador
    Wandering_Aggressive,     // Vagueia, ataca se vir um jogador

    // Passivos e de Ambiente
    Ambient_Fleeing,          // Foge quando um jogador se aproxima
    Ambient_Passive,          // Criatura passiva que não faz nada (pode ser atacada)

    /// <summary>
    /// NPC que fica completamente parado (guarda, vendedor). Pode ser agressivo se atacado (controlado pela facção).
    /// A diferença para o Passive_Aggressive é que este NUNCA se move da sua posição inicial, mesmo fora de combate.
    /// </summary>
    Stationary_Guard,
    Patrolling_Guard,

    /// <summary>
    /// NPC que anda por uma área de forma aleatória, mas é totalmente passivo e não reage a ataques.
    /// Ex: Cidadãos, animais que não fogem.
    /// </summary>
    Ambient_Wandering,

    /// <summary>
    /// NPC especial que fica parado, é atacável, mas nunca morre ou reage.
    /// Usado para jogadores testarem seu dano. Terá lógica especial de reset de vida.
    /// </summary>
    Training_Dummy
}

public enum NpcAiState { Idle, Patrolling, Chasing, Attacking, ReturningToSpawn, Fleeing, Dead, Wandering }