using System.Collections.Generic;

public class ItemInstanceData
{
    public ItemQuality Quality { get; set; } // ou [System.Serializable] no cliente
    public int ItemLevel { get; set; }
    public int RequiredLevel { get; set; }
    public List<BaseStatData> Stats { get; set; } // ou ItemBaseStatUnity no cliente
    public int SellPrice { get; set; } // <<< ADICIONE ESTA LINHA
}