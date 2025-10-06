using System.Collections.Generic;
using System.Linq;

// Gerencia um único status (ex: Força), seu valor base e todos os seus modificadores.
public class Stat
{
    public float BaseValue { get; private set; }

    // Usamos um sistema "dirty flag" para evitar recálculos desnecessários.
    // O valor só é recalculado se um modificador for adicionado ou removido.
    private bool _isDirty = true;
    private float _lastCalculatedValue;
    public float Value
    {
        get
        {
            if (_isDirty)
            {
                _lastCalculatedValue = CalculateFinalValue();
                _isDirty = false;
            }
            return _lastCalculatedValue;
        }
    }

    private readonly List<StatModifier> _modifiers = new List<StatModifier>();

    public Stat(float baseValue = 0)
    {
        BaseValue = baseValue;
    }

    public void SetBaseValue(float newBaseValue)
    {
        BaseValue = newBaseValue;
        _isDirty = true;
    }

    public void AddModifier(StatModifier mod)
    {
        _modifiers.Add(mod);
        _modifiers.Sort(CompareModifierOrder); // Mantém a lista ordenada para cálculo otimizado
        _isDirty = true;
    }

    public bool RemoveModifier(StatModifier mod)
    {
        if (_modifiers.Remove(mod))
        {
            _isDirty = true;
            return true;
        }
        return false;
    }

    public bool RemoveAllModifiersFromSource(object source)
    {
        int numRemovals = _modifiers.RemoveAll(mod => mod.Source.Equals(source));
        if (numRemovals > 0)
        {
            // Este log é útil e deve permanecer.
            Console.WriteLine($"[DEBUG] Stat: Removido {numRemovals} modificador(es) da fonte '{source}'.");
            _isDirty = true;
            return true;
        }

        return false;
    }

    // Ordena os modificadores para que Flat venha antes de PercentAdd, etc.
    private int CompareModifierOrder(StatModifier a, StatModifier b)
    {
        if (a.Type < b.Type) return -1;
        if (a.Type > b.Type) return 1;
        return 0;
    }

    private float CalculateFinalValue()
    {
        float finalValue = BaseValue;
        float percentAddSum = 0;

        // A ordem de cálculo é crucial: (Base + Flat) * (1 + PercentAddTotal) * (1 + PercentMult1) * ...
        foreach (var mod in _modifiers)
        {
            if (mod.Type == StatModifierType.Flat)
            {
                finalValue += mod.Value;
            }
            else if (mod.Type == StatModifierType.PercentAdd)
            {
                percentAddSum += mod.Value;
            }
            else if (mod.Type == StatModifierType.PercentMult)
            {
                finalValue *= (1 + mod.Value);
            }
        }

        if (percentAddSum != 0)
        {
            finalValue *= (1 + percentAddSum);
        }

        // Arredondar pode evitar problemas de ponto flutuante.
        return (float)System.Math.Round(finalValue, 4);
    }
}