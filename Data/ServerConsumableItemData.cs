/// <summary>
/// Representa um item que pode ser consumido para gerar efeitos, como poções.
/// Herda de ServerItemData para ter as propriedades básicas de um item.
/// </summary>
public class ServerConsumableItemData : ServerItemData
{
    public int InstantHealthGain { get; set; }
    public int InstantResourceGain { get; set; }
    public string? StatusEffectToApplyID { get; set; } // O ID do StatusEffect a ser aplicado
}
