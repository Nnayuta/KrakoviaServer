// ARQUIVO COMPLETO E CORRIGIDO: Managers/LootManager.cs

using System;
using System.Collections.Generic;
using System.Linq;

public class LootManager
{
    private readonly UDPServer _server;
    private readonly Random _random = new Random();

    public LootManager(UDPServer server)
    {
        _server = server;
    }

    public List<ItemStack> GenerateLootForNpc(string lootTableId, int npcLevel)
    {
        var itemsToDrop = new List<ItemStack>();
        if (string.IsNullOrEmpty(lootTableId) || !DataManager.LootTables.TryGetValue(lootTableId, out var table))
        {
            return itemsToDrop;
        }

        foreach (var pool in table.Pools)
        {
            if (_random.NextDouble() >= pool.Chance) continue;

            for (int i = 0; i < pool.Rolls; i++)
            {
                int totalWeight = pool.Entries.Sum(e => e.Weight);
                if (totalWeight <= 0) continue;

                int randomWeight = _random.Next(1, totalWeight + 1);
                int currentWeight = 0;

                foreach (var entry in pool.Entries)
                {
                    currentWeight += entry.Weight;
                    if (randomWeight <= currentWeight)
                    {
                        int quantity = _random.Next(entry.MinQuantity, entry.MaxQuantity + 1);
                        if (quantity > 0)
                        {
                            var itemStack = new ItemStack(entry.ItemID, quantity);
                            var itemTemplate = DataManager.Items[entry.ItemID];

                            if (itemTemplate is ServerEquipmentData eqItemTemplate)
                            {
                                int itemLevel = ItemLevelConverter.GetItemLevelForCreature(npcLevel);
                                int requiredLevel = ItemLevelConverter.GetRequiredLevelForItemLevel(itemLevel);
                                var (generatedStats, finalQuality) = ServerStatAllocator.GenerateStatsForItem(eqItemTemplate, itemLevel);

                                var instanceData = new ItemInstanceData
                                {
                                    Quality = finalQuality,
                                    ItemLevel = itemLevel,
                                    RequiredLevel = requiredLevel,
                                    Stats = generatedStats,
                                    SellPrice = 0 // O preço de venda será calculado a seguir
                                };

                                // Calcula o preço de venda com base nos dados que acabamos de gerar.
                                instanceData.SellPrice = ServerStatAllocator.CalculateSellPrice(instanceData);

                                _server.ItemInstanceManager.RegisterGeneratedItem(itemStack.InstanceID, instanceData);
                            }

                            itemsToDrop.Add(itemStack);
                        }
                        break;
                    }
                }
            }
        }
        return itemsToDrop;
    }
}