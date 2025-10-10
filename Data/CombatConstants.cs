// Servidor/Data/CombatConstants.cs
public static class CombatConstants
{
    public const float ARMOR_K_BASE = 400f;
    public const float ARMOR_K_LEVEL_MULTIPLIER = 85f;
    public const float MAX_ARMOR_DAMAGE_REDUCTION = 0.75f;

    // --- (NOVAS CONSTANTES) MODIFICADOR DE DANO POR NÍVEL ---
    public const float DAMAGE_MOD_PER_LEVEL = 0.1f; // 10% por nível
    public const int MAX_LEVEL_DIFFERENCE_MOD = 5; // O efeito para de aumentar após 5 níveis de diferença
}