using Newtonsoft.Json;

public enum AbilityEffectType { Damage, Heal, ApplyBuff, Resurrect }
public enum AbilityIntent { Harmful, Helpful }

public class AbilityData
{
    public string ID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AbilityIntent Intent { get; set; }
    public float Cooldown { get; set; }
    public float Range { get; set; }
    public float ResourceCost { get; set; }
    public bool RequiresTarget { get; set; }
    public WeaponRequirement WeaponRequirement { get; set; }
    public int Priority { get; set; } = 0;
    public AbilityType Type { get; set; }
    public WeaponType GrantsWeaponProficiency { get; set; }
    public AbilityEffectType EffectType { get; set; }
    public float BaseValue { get; set; }
    public float AttackPowerScaling { get; set; }
    public float SpellPowerScaling { get; set; }
    public float CastTime { get; set; }
    public bool CanMoveWhileCasting { get; set; }
    public float ProjectileSpeed { get; set; }
    public TargetType TargetType { get; set; }

    [JsonProperty("AoeRadius")] // Opcional, mas bom para JSON
    public float AoeRadius { get; set; } // <<<< ADICIONE ESTA LINHA

    [JsonProperty("ConeAngle")] // Opcional, mas bom para JSON
    public float ConeAngle { get; set; } // <<<< ADICIONE ESTA LINHA
}