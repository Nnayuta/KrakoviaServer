using System;
using System.Collections.Generic;

public class CharacterStats
{
    public event Action<StatType> OnStatChanged;

    private readonly Dictionary<StatType, Stat> _stats = new Dictionary<StatType, Stat>();
    private readonly int _level;

    // Uma fonte constante para identificar os modificadores que vêm de stats primários.
    private const string PRIMARY_STAT_SOURCE = "PrimaryStatContribution";

    public CharacterStats(ServerClassData classData, int level)
    {
        _level = level;

        // Inicializa todos os stats com um valor base de 0.
        foreach (StatType statType in Enum.GetValues(typeof(StatType)))
        {
            _stats.Add(statType, new Stat(0));
        }

        // Calcula os valores base dos atributos primários usando o novo sistema.
        float finalStrength = ClassStatCalculator.GetStatAtLevel(classData.BaseStrength, classData.StrengthGrowth, _level);
        float finalAgility = ClassStatCalculator.GetStatAtLevel(classData.BaseAgility, classData.AgilityGrowth, _level);
        float finalIntellect = ClassStatCalculator.GetStatAtLevel(classData.BaseIntelligence, classData.IntelligenceGrowth, _level);
        float finalStamina = ClassStatCalculator.GetStatAtLevel(classData.BaseStamina, classData.StaminaGrowth, _level);

        _stats[StatType.MovementSpeed].SetBaseValue(100.0f);
        _stats[StatType.Strength].SetBaseValue(finalStrength);
        _stats[StatType.Agility].SetBaseValue(finalAgility);
        _stats[StatType.Intellect].SetBaseValue(finalIntellect);
        _stats[StatType.Stamina].SetBaseValue(finalStamina);

        // Atribui outros valores base definidos na classe.
        float finalHealth = ClassStatCalculator.GetStatAtLevel(classData.BaseHealth, classData.HealthGrowth, _level);
        float finalResource = ClassStatCalculator.GetStatAtLevel(classData.BaseResource, classData.ResourceGrowth, _level);

        _stats[StatType.Health].SetBaseValue(finalHealth);
        _stats[StatType.Mana].SetBaseValue(finalResource);

        _stats[StatType.CriticalStrikeChance].SetBaseValue(5.0f);
    }

    public float GetStatValue(StatType statType)
    {
        return _stats[statType].Value;
    }

    public void AddStatModifier_NoRecalculate(StatType statType, StatModifier modifier)
    {
        _stats[statType].AddModifier(modifier);
        // Note que não chamamos CalculateAllDerivedStats() aqui.
    }

    public void AddStatModifier(StatType statType, StatModifier modifier)
    {
        _stats[statType].AddModifier(modifier);
        CalculateAllDerivedStats();
        OnStatChanged?.Invoke(statType);
    }

    public void RemoveAllStatModifiersFromSource(object source)
    {
        bool statsChanged = false;
        foreach (var stat in _stats.Values)
        {
            if (stat.RemoveAllModifiersFromSource(source))
            {
                statsChanged = true;
            }
        }

        if (statsChanged)
        {
            CalculateAllDerivedStats();
        }
    }

    public void CalculateAllDerivedStats()
    {
        // 1. LIMPA a lousa: Remove todas as contribuições de stats primários do cálculo anterior.
        RemoveAllStatModifiersFromSource(PRIMARY_STAT_SOURCE);

        // 2. RECALCULA as contribuições como MODIFICADORES.

        // Vida: Adiciona um modificador de vida com base no Vigor (Stamina) total.
        float staminaContribution = GetStatValue(StatType.Stamina) * 10; // 1 Vigor = 10 Vida
        if (staminaContribution > 0)
        {
            var healthFromStamina = new StatModifier(staminaContribution, StatModifierType.Flat, PRIMARY_STAT_SOURCE);
            _stats[StatType.Health].AddModifier(healthFromStamina);
        }

        // Mana: Adiciona um modificador de mana com base no Intelecto total.
        float intellectContribution = GetStatValue(StatType.Intellect) * 15; // 1 Intelecto = 15 Mana
        if (intellectContribution > 0)
        {
            var manaFromIntellect = new StatModifier(intellectContribution, StatModifierType.Flat, PRIMARY_STAT_SOURCE);
            _stats[StatType.Mana].AddModifier(manaFromIntellect);
        }

        // Poder de Ataque (Melee): Derivado de Força e/ou Agilidade.
        float apFromStrength = GetStatValue(StatType.Strength) * 2;
        float apFromAgility = GetStatValue(StatType.Agility) * 1;
        if (apFromStrength + apFromAgility > 0)
        {
            var apModifier = new StatModifier(apFromStrength + apFromAgility, StatModifierType.Flat, PRIMARY_STAT_SOURCE);
            _stats[StatType.AttackPower].AddModifier(apModifier);
        }

        // Poder Mágico: Derivado do Intelecto.
        float spFromIntellect = GetStatValue(StatType.Intellect);
        if (spFromIntellect > 0)
        {
            var spModifier = new StatModifier(spFromIntellect, StatModifierType.Flat, PRIMARY_STAT_SOURCE);
            _stats[StatType.SpellPower].AddModifier(spModifier);
        }

        // Chance de Crítico (Melee/Ranged): Derivado da Agilidade.
        float critFromAgility = GetStatValue(StatType.Agility) / 20f; // Ex: 20 Agilidade = 1% Crítico
        if (critFromAgility > 0)
        {
            var critAgiModifier = new StatModifier(critFromAgility, StatModifierType.Flat, PRIMARY_STAT_SOURCE);
            _stats[StatType.CriticalStrikeChance].AddModifier(critAgiModifier);
        }

        // Conversão de Ratings (índices) para Porcentagens
        float critFromRating = GetStatValue(StatType.CriticalStrikeRating) / 22.08f;
        if (critFromRating > 0)
        {
            var critRatingModifier = new StatModifier(critFromRating, StatModifierType.Flat, PRIMARY_STAT_SOURCE);
            _stats[StatType.CriticalStrikeChance].AddModifier(critRatingModifier);
        }

        float hasteFromRating = GetStatValue(StatType.HasteRating) / 15.77f;
        if (hasteFromRating > 0)
        {
            var hasteRatingModifier = new StatModifier(hasteFromRating, StatModifierType.Flat, PRIMARY_STAT_SOURCE);
            _stats[StatType.Haste].AddModifier(hasteRatingModifier);
        }

        float manaRegenFromIntellect = 5f + (GetStatValue(StatType.Intellect) * 0.1f);
        if (manaRegenFromIntellect > 0)
        {
            var manaRegenModifier = new StatModifier(manaRegenFromIntellect, StatModifierType.Flat, PRIMARY_STAT_SOURCE);
            _stats[StatType.ManaRegeneration].AddModifier(manaRegenModifier);
        }


        // Notifica que os stats derivados foram atualizados.
        OnStatChanged?.Invoke(StatType.Health);
        OnStatChanged?.Invoke(StatType.Mana);
        OnStatChanged?.Invoke(StatType.AttackPower);
        OnStatChanged?.Invoke(StatType.SpellPower);
        OnStatChanged?.Invoke(StatType.CriticalStrikeChance);
        OnStatChanged?.Invoke(StatType.Haste);
        OnStatChanged?.Invoke(StatType.ManaRegeneration);
        OnStatChanged?.Invoke(StatType.CombatManaRegenPercent);
    }
}