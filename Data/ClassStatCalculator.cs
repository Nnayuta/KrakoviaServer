
public static class ClassStatCalculator
{
    // ========================================================================
    // >> PONTO CENTRAL DE BALANCEAMENTO <<
    // Se quiser que o crescimento "High" seja mais forte, mude o valor aqui
    // e TODAS as classes com crescimento "High" serão atualizadas.
    // ========================================================================
    private const float LOW_GROWTH_MULTIPLIER = 0.8f;
    private const float MEDIUM_GROWTH_MULTIPLIER = 1.5f;
    private const float HIGH_GROWTH_MULTIPLIER = 2.5f;

    // A fórmula principal que calcula o valor de um stat em um determinado nível.
    public static float GetStatAtLevel(float baseStat, GrowthTier tier, int level)
    {
        if (level <= 1)
        {
            return baseStat;
        }

        float growthPerLevel = 0f;

        switch (tier)
        {
            case GrowthTier.Low:
                growthPerLevel = LOW_GROWTH_MULTIPLIER;
                break;
            case GrowthTier.Medium:
                growthPerLevel = MEDIUM_GROWTH_MULTIPLIER;
                break;
            case GrowthTier.High:
                growthPerLevel = HIGH_GROWTH_MULTIPLIER;
                break;
            case GrowthTier.None:
            default:
                growthPerLevel = 0f;
                break;
        }

        // Fórmula: StatBase + (CrescimentoPorNivel * (NiveisAcimaDo1))
        return baseStat + (growthPerLevel * (level - 1));
    }
}