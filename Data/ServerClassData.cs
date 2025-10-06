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
    public GrowthTier HealthGrowth { get; set; }
    public GrowthTier ResourceGrowth { get; set; }
    public GrowthTier StrengthGrowth { get; set; }
    public GrowthTier AgilityGrowth { get; set; }
    public GrowthTier IntelligenceGrowth { get; set; }
    public GrowthTier StaminaGrowth { get; set; }

    /// <summary>
    /// A porcentagem de regeneração de mana que esta classe mantém em combate.
    /// 0.3 = 30%
    /// </summary>
    public float BaseCombatManaRegenPercent { get; set; } = 0f; // Padrão é 0% para classes não-caster

    // Habilidades e Itens
    public AbilityUnlockMap BaseAbilityUnlocks { get; set; } = new();
    public List<string> StartingEquipmentIDs { get; set; } = new();
    public List<string> StartingInventoryIDs { get; set; } = new();
    public List<ServerSpecData> Specializations { get; set; } = new();
    public List<WeaponType> WeaponProficiencies { get; set; } = new();
}