// ARQUIVO ATUALIZADO: Server/Utilities/ServerStatAllocator.cs

using System;
using System.Collections.Generic;
using System.Linq;

public static class ServerStatAllocator
{
    private static readonly Random _random = new Random();

    // ================================================================================================
    // --- BASE DE DADOS DE BALANCEAMENTO ---
    // ================================================================================================

    // --- CHANCES DE QUALIDADE ---
    // A soma não precisa ser 100. Funciona como "pesos".
    // 70 de chance de ser Comum, 25 de Incomum, 4.5 de Raro, etc.
    private static readonly Dictionary<ItemQuality, float> qualityWeights = new Dictionary<ItemQuality, float>
    {
        { ItemQuality.Common, 70f },
        { ItemQuality.Uncommon, 25f },
        { ItemQuality.Rare, 4.5f },
        { ItemQuality.Epic, 0.45f },
        { ItemQuality.Legendary, 0.05f }
    };
    private static readonly float totalQualityWeight = qualityWeights.Values.Sum();

    // --- Multiplicadores e Pesos (como antes) ---
    private static readonly Dictionary<ItemQuality, float> qualityStatMultipliers = new Dictionary<ItemQuality, float>
    {
        { ItemQuality.Common, 1.0f }, { ItemQuality.Uncommon, 1.15f }, { ItemQuality.Rare, 1.30f },
        { ItemQuality.Epic, 1.45f }, { ItemQuality.Legendary, 1.65f }
    };
    private static readonly Dictionary<EquipmentSlot, float> slotWeights = new Dictionary<EquipmentSlot, float>
    {
        { EquipmentSlot.Head, 0.8f }, { EquipmentSlot.Chest, 1.0f }, { EquipmentSlot.Legs, 0.95f },
        { EquipmentSlot.Hands, 0.6f }, { EquipmentSlot.Feet, 0.6f }, { EquipmentSlot.Cloak, 0.45f },
        { EquipmentSlot.MainHand, 0.75f }, { EquipmentSlot.OffHand, 0.75f },
    };
    private const float STAT_BUDGET_BASE = 5.0f;
    private const float STAT_BUDGET_EXP_RATE = 0.045f;
    private static readonly Dictionary<ArmorType, float> armorBaseMultiplier = new Dictionary<ArmorType, float>
    {
        { ArmorType.Cloth, 8.0f }, { ArmorType.Leather, 16.0f }, { ArmorType.Mail, 32.0f }, { ArmorType.Plate, 48.0f }
    };
    private const float ARMOR_LEVEL_POWER = 1.25f;
    private const float TERTIARY_STAT_CHANCE = 0.20f;
    private const float TERTIARY_BUDGET_RATIO = 0.4f;

    // ================================================================================================
    // --- MÉTODOS PÚBLICOS DE GERAÇÃO ---
    // ================================================================================================

    /// <summary>
    /// Sorteia aleatoriamente uma qualidade de item com base nos pesos definidos.
    /// </summary>
    public static ItemQuality RollItemQuality()
    {
        double roll = _random.NextDouble() * totalQualityWeight;
        float cumulative = 0;
        foreach (var pair in qualityWeights)
        {
            cumulative += pair.Value;
            if (roll < cumulative)
            {
                return pair.Key;
            }
        }
        return ItemQuality.Common; // Fallback
    }

    /// <summary>
    /// Gera stats para uma instância de item, incluindo o sorteio da qualidade.
    /// Retorna tanto os stats gerados quanto a qualidade final do item.
    /// </summary>
    public static (List<BaseStatData> stats, ItemQuality finalQuality) GenerateStatsForItem(ServerEquipmentData eqItemTemplate, int itemLevel)
    {
        // 1. SORTEIA A QUALIDADE!
        ItemQuality finalQuality = RollItemQuality();

        // O "itemLevel" é dinâmico, baseado no nível de quem dropou o item.
        // int itemLevel = entityLevel;
        var generatedStats = new Dictionary<StatType, int>();

        // Determina o stat primário com base no primaryStatFocus do template.
        StatType chosenPrimaryStat;
        switch (eqItemTemplate.primaryStatFocus)
        {
            case PrimaryStatFocus.Agility: chosenPrimaryStat = StatType.Agility; break;
            case PrimaryStatFocus.Intellect: chosenPrimaryStat = StatType.Intellect; break;
            default: chosenPrimaryStat = StatType.Strength; break;
        }

        // 2. CALCULAR ORÇAMENTO (BUDGET) USANDO A QUALIDADE SORTEADA
        float baseBudget = (float)(STAT_BUDGET_BASE * Math.Exp(STAT_BUDGET_EXP_RATE * (itemLevel - 1)));
        float qualityMultiplier = qualityStatMultipliers[finalQuality];
        float slotMultiplier = slotWeights.ContainsKey(eqItemTemplate.equipmentSlot) ? slotWeights[eqItemTemplate.equipmentSlot] : 1.0f;
        int totalStatBudget = (int)Math.Round(baseBudget * qualityMultiplier * slotMultiplier);

        // 3. GERAR ARMADURA
        if (eqItemTemplate is ServerArmorData armorItem)
        {
            float baseArmor = armorBaseMultiplier[armorItem.armorType];
            int armorValue = (int)Math.Round(baseArmor * Math.Pow(itemLevel, ARMOR_LEVEL_POWER) * slotMultiplier);
            if (armorValue > 0) generatedStats[StatType.Armor] = armorValue;
        }

        // 4. DISTRIBUIR BUDGET PRINCIPAL
        int staminaBudget = (int)Math.Round(totalStatBudget * 0.5f);
        if (staminaBudget > 0) generatedStats[StatType.Stamina] = staminaBudget;

        int remainingBudgetForOthers = totalStatBudget - staminaBudget;

        List<StatType> possibleSecondaries = new List<StatType> { StatType.CriticalStrikeRating, StatType.HasteRating, StatType.MasteryRating };

        // Número de stats secundários AGORA DEPENDE DA QUALIDADE SORTEADA
        int secondaryStatCount = (finalQuality >= ItemQuality.Rare) ? 2 : (finalQuality >= ItemQuality.Uncommon ? 1 : 0);
        secondaryStatCount = Math.Min(secondaryStatCount, possibleSecondaries.Count);

        int secondaryBudgetTotal = (int)Math.Round(remainingBudgetForOthers * 0.45f);
        int remainingSecondaryBudget = secondaryBudgetTotal;

        for (int i = 0; i < secondaryStatCount; i++)
        {
            if (remainingSecondaryBudget <= 0 || !possibleSecondaries.Any()) break;
            int randIndex = _random.Next(0, possibleSecondaries.Count);
            StatType chosenStat = possibleSecondaries[randIndex];
            possibleSecondaries.RemoveAt(randIndex);
            int valueToAllocate = (i == secondaryStatCount - 1) ? remainingSecondaryBudget : (int)Math.Round((float)remainingSecondaryBudget / (secondaryStatCount - i));
            if (valueToAllocate > 0)
            {
                generatedStats[chosenStat] = valueToAllocate;
                remainingSecondaryBudget -= valueToAllocate;
            }
        }

        int primaryStatBudget = remainingBudgetForOthers - (secondaryBudgetTotal - remainingSecondaryBudget);
        if (primaryStatBudget > 0) generatedStats[chosenPrimaryStat] = primaryStatBudget;

        // 5. GERAR STATS TERCIÁRIOS (BÔNUS)
        var possibleTertiaries = new List<StatType> { StatType.MovementSpeed, StatType.Leech, StatType.Avoidance };
        if (_random.NextDouble() < TERTIARY_STAT_CHANCE)
        {
            StatType chosenTertiary = possibleTertiaries[_random.Next(0, possibleTertiaries.Count)];
            float secondaryStatEquivalentBudget = (float)secondaryBudgetTotal / Math.Max(1, secondaryStatCount);
            int tertiaryValue = (int)Math.Round(secondaryStatEquivalentBudget * TERTIARY_BUDGET_RATIO);
            if (tertiaryValue > 0) generatedStats[chosenTertiary] = tertiaryValue;
        }

        // 6. Finalizar e retornar a tupla.
        var finalList = generatedStats
            .Where(kvp => kvp.Value > 0)
            .Select(kvp => new BaseStatData { Stat = kvp.Key, Value = kvp.Value })
            .ToList();

        return (finalList, finalQuality);
    }

    public static float CalculateStatBudget(int itemLevel)
    {
        return (float)(STAT_BUDGET_BASE * Math.Exp(STAT_BUDGET_EXP_RATE * (itemLevel - 1)));
    }

    public static int CalculateBuyPrice(ServerEquipmentData eqItem, int playerLevel)
    {
        // Usa o nível do jogador como o iLvl para o cálculo do preço.
        float baseBudget = (float)(STAT_BUDGET_BASE * Math.Exp(STAT_BUDGET_EXP_RATE * (playerLevel - 1)));

        // Itens de vendedor geralmente são de qualidade Incomum (verde), podemos fixar isso.
        float qualityMultiplier = qualityStatMultipliers[ItemQuality.Uncommon];
        float slotMultiplier = slotWeights.ContainsKey(eqItem.equipmentSlot) ? slotWeights[eqItem.equipmentSlot] : 1.0f;
        int totalBudget = (int)Math.Round(baseBudget * qualityMultiplier * slotMultiplier);

        const float bronzePerBudgetPoint = 30.0f; // Vendedores cobram mais caro
        float priceQualityMultiplier = 1.0f + ((int)ItemQuality.Uncommon * 0.75f);
        int finalPrice = (int)(totalBudget * bronzePerBudgetPoint * priceQualityMultiplier);

        // Arredondamento para preços "limpos"
        if (finalPrice > 100)
        {
            return (int)(Math.Round(finalPrice / 5.0) * 5);
        }
        return Math.Max(1, finalPrice);
    }

    public static int CalculateSellPrice(ItemInstanceData instanceData)
    {
        // O preço de venda é uma fração do "budget" de stats do item.
        // Primeiro, calculamos o budget total que este item teria.
        float baseBudget = CalculateStatBudget(instanceData.ItemLevel);
        float qualityMultiplier = qualityStatMultipliers[instanceData.Quality];
        // Precisamos do slot, que não está no ItemInstanceData. Isso é um problema.
        // Solução: O preço de venda é simplesmente 1/4 do preço de compra. É mais simples.

        // Vamos criar um método de preço de venda genérico.
        // Por simplicidade, o preço de venda será 25% do que custaria para comprar um item
        // Incomum do mesmo nível.
        float buyPriceBudget = (float)(STAT_BUDGET_BASE * Math.Exp(STAT_BUDGET_EXP_RATE * (instanceData.ItemLevel - 1)));
        const float bronzePerBudgetPoint = 5.0f;
        float priceQualityMultiplier = 1.0f + ((int)instanceData.Quality * 0.75f);
        int simulatedBuyPrice = (int)(buyPriceBudget * bronzePerBudgetPoint * priceQualityMultiplier);

        // O preço de venda é 25% do preço de "valor" do item.
        return Math.Max(1, (int)(simulatedBuyPrice * 0.25f));
    }
}