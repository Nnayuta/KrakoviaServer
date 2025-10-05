// Define os tipos de modificadores de status para a ordem correta de cálculo.
public enum StatModifierType
{
    /// <summary>
    /// Bônus aditivo fixo. Ex: +10 de Força. Calculado primeiro.
    /// </summary>
    Flat,

    /// <summary>
    /// Bônus percentual que se soma a outros bônus percentuais. Ex: Talentos que dão +5% e +10% de Vigor resultam em +15%.
    /// </summary>
    PercentAdd,

    /// <summary>
    /// Bônus percentual que multiplica o valor total. Ex: Um buff que aumenta todo o dano em 20%.
    /// </summary>
    PercentMult,
}