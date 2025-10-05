// Servidor/Managers/LootManager.cs
using System;
using System.Collections.Generic;
// ...

// REMOVIDO: A classe LootDrop não é mais necessária aqui.
// public class LootDrop { ... }

public class LootManager
{
    private readonly UDPServer _server;
    private readonly Random _random = new Random();

    public LootManager(UDPServer server) { _server = server; }

    // =================================================================================
    // MÉTODO REATORADO: Agora ele apenas gera a lista de itens e a retorna.
    // =================================================================================
    public List<ItemStack> GenerateLootForNpc(string lootTableId)
    {
        var itemsToDrop = new List<ItemStack>();
        if (string.IsNullOrEmpty(lootTableId) || !DataManager.LootTables.TryGetValue(lootTableId, out var table))
        {
            return itemsToDrop;
        }

        foreach (var entry in table.Entries)
        {
            if (_random.NextDouble() * 100 < entry.DropChance)
            {
                int quantity = _random.Next(entry.MinQuantity, entry.MaxQuantity + 1);
                itemsToDrop.Add(new ItemStack(entry.ItemID, quantity));
            }
        }

        Console.WriteLine($"[Loot] Gerado {itemsToDrop.Count} item(s) da tabela '{lootTableId}'.");
        return itemsToDrop;
    }

    // O método HandleLootRequest pode ser movido ou adaptado
}