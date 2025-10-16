// Servidor/Managers/LootManager.cs
using System;
using System.Collections.Generic;
using System.Linq; // Essencial para usar .Sum()

public class LootManager
{
    private readonly UDPServer _server;
    private readonly Random _random = new Random();

    public LootManager(UDPServer server)
    {
        _server = server;
    }

    /// <summary>
    /// Gera uma lista de itens para um NPC com base em um sistema de pools e pesos.
    /// </summary>
    /// <param name="lootTableId">O ID da tabela de loot a ser usada.</param>
    /// <returns>Uma lista de ItemStack a serem dropados.</returns>
    public List<ItemStack> GenerateLootForNpc(string lootTableId)
    {
        var itemsToDrop = new List<ItemStack>();
        if (string.IsNullOrEmpty(lootTableId) || !DataManager.LootTables.TryGetValue(lootTableId, out var table))
        {
            // Retorna lista vazia se o NPC não tiver loot table ou se o ID for inválido.
            return itemsToDrop;
        }

        // Itera sobre cada "pool" (grupo de itens) na tabela.
        foreach (var pool in table.Pools)
        {
            // 1. O pool tem chance de ser ativado?
            if (_random.NextDouble() >= pool.Chance)
            {
                continue; // Falhou na chance, vai para o próximo pool.
            }

            // 2. Quantas vezes vamos sortear um item deste pool?
            for (int i = 0; i < pool.Rolls; i++)
            {
                // Calcula o peso total de todos os itens no pool.
                int totalWeight = pool.Entries.Sum(e => e.Weight);
                if (totalWeight <= 0) continue;

                // Sorteia um número aleatório dentro do peso total.
                int randomWeight = _random.Next(1, totalWeight + 1);
                int currentWeight = 0;

                // 3. Seleciona o item com base no peso sorteado.
                foreach (var entry in pool.Entries)
                {
                    currentWeight += entry.Weight;
                    if (randomWeight <= currentWeight)
                    {
                        // Item selecionado! Agora definimos a quantidade.
                        int quantity = _random.Next(entry.MinQuantity, entry.MaxQuantity + 1);
                        if (quantity > 0)
                        {
                            itemsToDrop.Add(new ItemStack(entry.ItemID, quantity));
                        }
                        break; // Para de procurar itens neste roll, pois já encontramos um.
                    }
                }
            }
        }

        // Console.WriteLine($"[Loot] Gerado {itemsToDrop.Count} item(s) da tabela '{lootTableId}'.");
        return itemsToDrop;
    }
}