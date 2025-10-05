// Representa uma única modificação a um status, vinda de um item, buff, etc.
[System.Serializable]
public class StatModifier
{
    // Usamos float para permitir valores percentuais.
    public readonly float Value;
    public readonly StatModifierType Type;

    // A fonte do modificador (ex: "ItemID_HelmOfWrath", "BuffID_BattleShout")
    // Essencial para saber o que remover quando um item é desequipado ou um buff expira.
    public readonly object Source;

    public StatModifier(float value, StatModifierType type, object source)
    {
        Value = value;
        Type = type;
        Source = source;
    }
}