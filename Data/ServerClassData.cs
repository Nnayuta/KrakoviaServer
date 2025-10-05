// Servidor/Data/ServerClassData.cs
using System.Collections.Generic;

using AbilityUnlockMap = System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<string>>;

public class ServerSpecData
{
    public string SpecID { get; set; } = string.Empty;
    public AbilityUnlockMap AbilityUnlocks { get; set; } = new();
}

public class ServerClassData
{
    // Informações da Classe
    public string ClassID { get; set; } = string.Empty;
    public StatType PrimaryStat { get; set; }

    // Recursos e Stats Base (Nível 1)
    public int BaseHealth { get; set; }
    public int BaseResource { get; set; }
    public int BaseStrength { get; set; }
    public int BaseAgility { get; set; }
    public int BaseIntelligence { get; set; }
    public int BaseStamina { get; set; }

    // Crescimento por Nível
    public float HealthPerLevel { get; set; }
    public float ResourcePerLevel { get; set; }
    public float StrengthPerLevel { get; set; }
    public float AgilityPerLevel { get; set; }
    public float IntelligencePerLevel { get; set; }
    public float StaminaPerLevel { get; set; }

    // Habilidades e Itens
    public AbilityUnlockMap BaseAbilityUnlocks { get; set; } = new();
    public List<string> StartingEquipmentIDs { get; set; } = new();
    public List<string> StartingInventoryIDs { get; set; } = new();
    public List<ServerSpecData> Specializations { get; set; } = new();
    public List<WeaponType> WeaponProficiencies { get; set; } = new();
}