using System.Collections.Generic;

public class ServerLootEntry
{
    public string ItemID { get; set; }
    public int Weight { get; set; }
    public int MinQuantity { get; set; }
    public int MaxQuantity { get; set; }
}

public class ServerLootPool
{
    public float Chance { get; set; }
    public int Rolls { get; set; }
    public ItemQuality MinQuality { get; set; } // <-- ADICIONADO
    public List<ServerLootEntry> Entries { get; set; }
}

public class ServerLootTable
{
    public string LootTableID { get; set; }
    public List<ServerLootPool> Pools { get; set; }
}

public class LootTableWrapper
{
    public List<ServerLootTable> LootTables { get; set; }
}