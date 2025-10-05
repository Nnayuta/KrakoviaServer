// --- SERVIDOR ---
public enum TargetType { Self, SingleTarget, AreaOfEffect, Cone, Projectile }

// Modelo para Efeitos (Buffs, Debuffs, Dano, etc.)
public class AbilityEffect
{
    public AbilityEffectType EffectType { get; set; }
    public float BaseValue { get; set; } // Dano, cura, percentual do slow, etc.
    public required string StatusEffectID { get; set; } // Ex: "poison_dot", "armor_buff"
    public float Duration { get; set; } // Em segundos
}