// Servidor/Data/AbilityEffects/ (CRIE ESTES ARQUIVOS)

// A classe base para polimorfismo no JSON
public abstract class ServerAbilityEffectData
{
    // Adicionamos um tipo para facilitar o debug e o processamento
    public string EffectType => this.GetType().Name;
}

public class ServerDamageEffectData : ServerAbilityEffectData
{
    public float BaseValue { get; set; }
    public float AttackPowerScaling { get; set; }
    public float SpellPowerScaling { get; set; }
    // public DamageType DamageType { get; set; } // Se precisar no servidor para resistências
}

public class ServerHealEffectData : ServerAbilityEffectData
{
    public float BaseValue { get; set; }
    public float SpellPowerScaling { get; set; }
}

public class ServerApplyStatusEffectData : ServerAbilityEffectData
{
    public string StatusEffectID { get; set; } = string.Empty;
}

// E o ServerStatusEffectData (carregado de um status_effects.json)
// (Este é análogo à sua AbilityData)
public class ServerStatusEffectData
{
    public string EffectID { get; set; } = string.Empty;
    public float Duration { get; set; }
    public bool IsBuff { get; set; }
    public List<StatModifierDefinition> StatModifiers { get; set; } = new();
}

[System.Serializable]
public class StatModifierDefinition
{
    public StatType targetStat;
    public float value;
    public StatModifierType type;
}