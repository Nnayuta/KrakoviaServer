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
            // NOVO LOG DE ERRO
            Console.WriteLine($"[LootManager-WARN] LootTableID '{lootTableId}' not found or is null.");
            return itemsToDrop;
        }

        foreach (var pool in table.Pools)
        {
            // Pula este pool se o sorteio de chance falhar
            if (_random.NextDouble() >= pool.Chance) continue;

            // Roda os dados para este pool o número de vezes definido em 'Rolls'
            for (int i = 0; i < pool.Rolls; i++)
            {
                int totalWeight = pool.Entries.Sum(e => e.Weight);
                Console.WriteLine($"[LootManager-DEBUG] Processing LootTable '{lootTableId}', Pool Chance '{pool.Chance}', Rolls '{pool.Rolls}'. Total Weight in Pool: {totalWeight}. Entries count: {pool.Entries.Count}");
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

                            // Se o item for um equipamento, gere seus stats e instância
                            if (itemTemplate is ServerEquipmentData eqItemTemplate)
                            {
                                int itemLevel = ItemLevelConverter.GetItemLevelForCreature(npcLevel);
                                int requiredLevel = ItemLevelConverter.GetRequiredLevelForItemLevel(itemLevel);

                                // --- MUDANÇA PRINCIPAL AQUI ---
                                // Agora passamos a qualidade mínima do pool para o gerador de stats.
                                var (generatedStats, finalQuality) = ServerStatAllocator.GenerateStatsForItem(
                                    eqItemTemplate,
                                    itemLevel,
                                    pool.MinQuality // Passando o novo parâmetro!
                                );

                                var instanceData = new ItemInstanceData
                                {
                                    Quality = finalQuality,
                                    ItemLevel = itemLevel,
                                    RequiredLevel = requiredLevel,
                                    Stats = generatedStats,
                                    SellPrice = 0 // Será calculado a seguir
                                };

                                // Calcula o preço de venda com base nos dados gerados.
                                instanceData.SellPrice = ServerStatAllocator.CalculateSellPrice(instanceData);

                                _server.ItemInstanceManager.RegisterGeneratedItem(itemStack.InstanceID, instanceData);
                            }

                            itemsToDrop.Add(itemStack);
                        }
                        // Item foi escolhido, pare de procurar neste 'roll'
                        break;
                    }
                }
            }
        }
        return itemsToDrop;
    }
}