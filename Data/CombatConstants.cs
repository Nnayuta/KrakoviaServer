// Servidor/Data/CombatConstants.cs
public static class CombatConstants
{
    // Constantes para a fórmula de Redução de Dano por Armadura (baseado no WoW)
    public const float ARMOR_K_BASE = 400f;
    public const float ARMOR_K_LEVEL_MULTIPLIER = 85f;

    // Limite máximo de redução de dano por armadura (WoW usa 75%)
    public const float MAX_ARMOR_DAMAGE_REDUCTION = 0.75f;
}