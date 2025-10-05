// Servidor/Data/LootTable.cs
using System.Collections.Generic;

[System.Serializable]
public class LootTableEntry
{
    public string ItemID { get; set; }
    public float DropChance { get; set; } // Chance de 0.0 a 100.0
    public int MinQuantity { get; set; } = 1;
    public int MaxQuantity { get; set; } = 1;
}

[System.Serializable]
public class LootTable
{
    public string LootTableID { get; set; }
    public List<LootTableEntry> Entries { get; set; } = new List<LootTableEntry>();
}