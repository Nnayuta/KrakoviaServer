// Servidor/Data/VendorData.cs
using System.Collections.Generic;

// Representa um item na lista de um vendedor
public class VendorItemData
{
    public string ItemID { get; set; }
    public int BuyPrice { get; set; }
}

// Representa os dados completos de um único vendedor, linkado a um NpcTypeId
public class VendorData
{
    public string NpcTypeId { get; set; }
    public List<VendorItemData> Items { get; set; }
}