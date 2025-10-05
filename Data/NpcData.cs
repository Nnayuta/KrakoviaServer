// Servidor/Data/NpcData.cs

using System.Collections.Generic;

/// <summary>
/// Classe auxiliar e simples, usada APENAS para definir os stats base de um NPC
/// nos arquivos de dados (ex: JSON). É um contêiner de dados puro.
/// </summary>
[System.Serializable]
public class BaseStatData
{
    public StatType Stat;
    public int Value;
}

/// <summary>
/// Representa o template de dados para um tipo de NPC, carregado de um arquivo.
/// Contém todas as informações base que não mudam entre instâncias.
/// </summary>
public class NpcData
{
    public string TypeId { get; set; }
    public NpcFaction Faction { get; set; }
    public NpcAiType AiType { get; set; }
    public List<BaseStatData> Stats { get; set; } = new List<BaseStatData>();

    public int Level { get; set; }
    public bool IsBoss { get; set; }

    // Propriedades de Comportamento
    public float AggroRange { get; set; }
    public float LeashRange { get; set; }
    public int RespawnTimeSeconds { get; set; }

    public float SwingTimer { get; set; }
    public string AutoAttackAbilityID { get; set; }
    public List<string> AbilityIDs { get; set; } = new();

    // Propriedades de Recompensa
    public int ExperienceReward { get; set; }
    public string? LootTableID { get; set; }

    // Propriedade calculada, não precisa estar no JSON
    [Newtonsoft.Json.JsonIgnore]
    public float MaxAbilityRange { get; set; } = 0f;
}