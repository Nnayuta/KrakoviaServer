// Enums/StatType.cs

public enum StatType
{
    // --- Atributos Primários ---
    Strength,       // Força
    Agility,        // Agilidade
    Intellect,      // Intelecto
    Stamina,        // Vigor

    // --- Atributos Defensivos ---
    Armor,          // Armadura

    // --- Atributos Secundários (Combat Ratings) ---
    // Armazenamos a "Classificação" (Rating), não a porcentagem diretamente.
    CriticalStrikeRating, // Chance de Crítico
    HasteRating,          // Aceleração
    MasteryRating,        // Maestria

    // --- Atributos Terciários (Bônus Raros) ---
    MovementSpeed,  // Velocidade de Movimento (geralmente como %)
    Leech,          // Roubo de Vida (como %)
    Avoidance,      // Evasão (redução de dano em área, como %)

    // --- Atributos Derivados (não aparecem em itens, são calculados) ---
    Health,         // Vida
    Mana,           // Ou outro recurso
    AttackPower,    // Poder de Ataque
    SpellPower,     // Poder de Magia
    CriticalStrikeChance, // A chance % real
    Haste,                // A aceleração % real

    /// <summary>
    /// A quantidade de Mana (recurso) restaurada a cada "tick" de regeneração fora de combate.
    /// </summary>
    ManaRegeneration,

    /// <summary>
    /// A porcentagem (0.0 a 1.0) de ManaRegeneration que continua ativa durante o combate.
    /// </summary>
    CombatManaRegenPercent,
}

// Adicione em um arquivo de Enums
public enum GrowthTier
{
    None,    // O stat não cresce com o nível.
    Low,     // Crescimento baixo.
    Medium,  // Crescimento médio.
    High     // Crescimento alto.
}