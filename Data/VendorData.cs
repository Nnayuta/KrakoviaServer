// Data/VendorData.cs
using System.Collections.Generic;

public class VendorItemData
{
    public string ItemID { get; set; }
    public int BuyPrice { get; set; }
    // Futuramente, você pode adicionar:
    // public int? Stock { get; set; } // Para estoque limitado
}

public class VendorData
{
    public string NpcTypeId { get; set; }
    public string VendorName { get; set; }
    public string CurrencyType { get; set; } = "Gold"; // Moeda padrão
    public List<VendorItemData> Items { get; set; } = new List<VendorItemData>();
}