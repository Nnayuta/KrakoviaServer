using System;
using System.Collections.Generic;

// O componente central que toda entidade de combate (Player, NPC) terá.
// Ele orquestra todos os objetos Stat individuais.
public class CharacterStats
{
    public event Action<StatType> OnStatChanged;

    private readonly Dictionary<StatType, Stat> _stats = new Dictionary<StatType, Stat>();
    private readonly int _level;

    public CharacterStats(ServerClassData classData, int level)
    {
        _level = level;

        // Inicializa todos os stats possíveis para evitar erros de chave não encontrada.
        foreach (StatType statType in Enum.GetValues(typeof(StatType)))
        {
            _stats.Add(statType, new Stat(0));
        }

        // Calcula os valores base dos atributos primários com base na classe e nível.
        _stats[StatType.Strength].SetBaseValue(classData.BaseStrength + (classData.StrengthPerLevel * (_level - 1)));
        _stats[StatType.Agility].SetBaseValue(classData.BaseAgility + (classData.AgilityPerLevel * (_level - 1)));
        _stats[StatType.Intellect].SetBaseValue(classData.BaseIntelligence + (classData.IntelligencePerLevel * (_level - 1)));
        _stats[StatType.Stamina].SetBaseValue(classData.BaseStamina + (classData.StaminaPerLevel * (_level - 1)));

        // Atribui outros valores base definidos na classe
        _stats[StatType.Health].SetBaseValue(classData.BaseHealth + (classData.HealthPerLevel * (_level - 1)));
        _stats[StatType.Mana].SetBaseValue(classData.BaseResource + (classData.ResourcePerLevel * (_level - 1)));
    }

    public float GetStatValue(StatType statType)
    {
        return _stats[statType].Value;
    }

    public void AddStatModifier(StatType statType, StatModifier modifier)
    {
        _stats[statType].AddModifier(modifier);
        RecalculateAffectedStats(statType);
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

        // Se algum modificador foi de fato removido, precisamos recalcular os stats derivados.
        if (statsChanged)
        {
            CalculateAllDerivedStats();
            // Disparar um evento global de atualização seria bom aqui.
        }
    }

    private void RecalculateAffectedStats(StatType changedStat)
    {
        // Se um stat primário ou rating muda, recalcula todos os stats derivados.
        // Isso é mais simples e menos propenso a erros do que rastrear dependências individuais.
        CalculateAllDerivedStats();
    }

    public void CalculateAllDerivedStats()
    {
        // --- Fórmulas de Conversão (Inspiradas em WoW, ajuste para o seu jogo!) ---

        // Vida: Vigor é um grande contribuidor.
        // A vida base já foi definida no construtor. Agora adicionamos a contribuição do Vigor.
        float totalHealth = GetStatValue(StatType.Health) + (GetStatValue(StatType.Stamina) * 20); // Ex: 1 Vigor = 20 Vida
        _stats[StatType.Health].SetBaseValue(totalHealth);

        // Poder de Ataque/Magia: Derivado dos stats primários.
        float attackPower = GetStatValue(StatType.Strength) * 2; // Ex: 1 Força = 2 Poder de Ataque
        _stats[StatType.AttackPower].SetBaseValue(attackPower);

        float spellPower = GetStatValue(StatType.Intellect); // Ex: 1 Intelecto = 1 Poder de Magia
        _stats[StatType.SpellPower].SetBaseValue(spellPower);

        // Crítico: Rating + contribuição da Agilidade.
        float critRating = GetStatValue(StatType.CriticalStrikeRating);
        float critFromRating = critRating / (22.0f * _level); // Ex: Precisa de 22 de rating por nível para 1% de crítico.
        float critFromAgility = GetStatValue(StatType.Agility) / (52.0f * _level); // Ex: Precisa de 52 de agilidade por nível para 1%.
        _stats[StatType.CriticalStrikeChance].SetBaseValue(5.0f + critFromRating + critFromAgility); // Adiciona uma chance base de 5%.

        // Aceleração: Convertido diretamente do Rating.
        float hasteRating = GetStatValue(StatType.HasteRating);
        float hasteFromRating = hasteRating / (18.0f * _level); // Ex: 18 de rating por nível para 1% de aceleração.
        _stats[StatType.Haste].SetBaseValue(hasteFromRating);
    }
}