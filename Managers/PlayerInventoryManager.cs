// Managers/PlayerInventoryManager.cs
using System;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Gerencia todas as operações relacionadas ao inventário do jogador,
/// incluindo movimentação de itens e interações com lojas.
/// </summary>
public class PlayerInventoryManager
{
    private readonly UDPServer _server;

    public PlayerInventoryManager(UDPServer server)
    {
        _server = server;
    }

    #region Handlers de Requisições do Cliente

    /// <summary>
    /// Tenta adicionar uma lista de itens ao inventário do jogador.
    /// Notifica o jogador sobre os itens recebidos ou se o inventário está cheio.
    /// </summary>
    /// <param name="player">O jogador que receberá o loot.</param>
    /// <param name="lootItems">A lista de itens a serem concedidos.</param>
    public void GrantLootToPlayer(Player player, List<ItemStack> lootItems)
    {
        if (lootItems == null || !lootItems.Any())
        {
            _server.NetworkManager.SendMessageToPlayer(player, "SHOW_FEEDBACK|Você não encontrou nada.");
            return;
        }

        foreach (var itemStack in lootItems)
        {
            var originalQuantity = itemStack.Quantity;

            // Agora 'changedSlots' é um Dicionário, como esperado.
            var changedSlots = player.PlayerInventory.AddItemStack(itemStack);

            // A verificação .Any() agora funciona corretamente.
            if (changedSlots.Any())
            {
                // Pegamos o primeiro item alterado para mostrar o feedback.
                var firstChangedStack = changedSlots.First().Value;
                if (firstChangedStack == null) continue; // Segurança extra

                var itemData = DataManager.Items[firstChangedStack.ItemID];
                string feedback = originalQuantity > 1 ? $"+{originalQuantity} {itemData.itemName}" : $"+{itemData.itemName}";
                _server.NetworkManager.SendMessageToPlayer(player, $"SHOW_FEEDBACK|{feedback}");

                // Envia os dados da instância.
                var instanceData = _server.ItemInstanceManager.GetDataForInstance(firstChangedStack.InstanceID);
                if (instanceData != null)
                {
                    _server.NetworkManager.SendItemInstanceData(player, firstChangedStack.InstanceID, instanceData);
                }

                // O loop foreach agora funciona corretamente.
                // Notifica o cliente sobre CADA slot que foi modificado.
                foreach (var kvp in changedSlots)
                {
                    // kvp.Key é o índice do slot (int)
                    // kvp.Value é o ItemStack (nunca será null aqui)
                    _server.NetworkManager.SendInventorySlotUpdate(player, kvp.Key, kvp.Value);
                }
            }
            else
            {
                // Se AddItemStack retornou um dicionário vazio, o inventário está cheio.
                _server.NetworkManager.SendMessageToPlayer(player, "ERROR|Inventário cheio.");
                break;
            }
        }
    }

    /// <summary>
    /// Handler principal para a movimentação de itens. Cobre arrastar e soltar,
    /// trocar, mover para slot vazio, e empilhar.
    /// </summary>
    public void HandleMoveItemRequest(Player player, int fromSlot, int toSlot)
    {
        var inventory = player.PlayerInventory;
        if (!IsValidSlot(inventory, fromSlot) || !IsValidSlot(inventory, toSlot) || fromSlot == toSlot) return;

        ItemStack fromItemStack = inventory.slots[fromSlot];
        ItemStack toItemStack = inventory.slots[toSlot];

        // Se estamos movendo um item para um slot vazio, é uma troca simples.
        if (fromItemStack != null && toItemStack == null)
        {
            inventory.slots[toSlot] = fromItemStack;
            inventory.slots[fromSlot] = null;

            // <<< MUDANÇA AQUI >>>
            // Notifica o cliente da troca exata.
            _server.NetworkManager.SendInventorySlotSwap(player, fromSlot, toSlot);
            return; // Fim da operação
        }

        // Se ambos os slots têm itens.
        if (fromItemStack != null && toItemStack != null)
        {
            // Se são itens iguais e empilháveis, tentamos empilhar.
            if (fromItemStack.ItemID == toItemStack.ItemID && DataManager.Items.TryGetValue(fromItemStack.ItemID, out var itemData) && itemData.isStackable)
            {
                int spaceInStack = itemData.maxStackSize - toItemStack.Quantity;
                int amountToMove = Math.Min(fromItemStack.Quantity, spaceInStack);

                toItemStack.Quantity += amountToMove;
                fromItemStack.Quantity -= amountToMove;

                if (fromItemStack.Quantity <= 0)
                {
                    inventory.slots[fromSlot] = null;
                }

                // <<< MUDANÇA AQUI >>>
                // Notificamos a atualização dos dois slots afetados.
                _server.NetworkManager.SendInventorySlotUpdate(player, fromSlot, inventory.slots[fromSlot]);
                _server.NetworkManager.SendInventorySlotUpdate(player, toSlot, inventory.slots[toSlot]);
            }
            else // Se não, simplesmente trocamos.
            {
                (inventory.slots[fromSlot], inventory.slots[toSlot]) = (inventory.slots[toSlot], inventory.slots[fromSlot]);
                // <<< MUDANÇA AQUI >>>
                _server.NetworkManager.SendInventorySlotSwap(player, fromSlot, toSlot);
            }
            return; // Fim da operação
        }
        // A chamada antiga para SendInventoryUpdate() foi completamente removida.
    }

    public void HandleUseItemRequest(Player player, int inventorySlot)
    {
        if (!IsValidSlot(player.PlayerInventory, inventorySlot)) return;

        ItemStack? itemStack = player.PlayerInventory.slots[inventorySlot];
        if (itemStack == null)
        {
            Console.WriteLine($"[UseItem] FALHA: {player.Username} tentou usar um slot vazio ({inventorySlot}).");
            return;
        }

        if (!DataManager.Items.TryGetValue(itemStack.ItemID, out var itemData) || itemData is not ServerConsumableData consumableData)
        {
            Console.WriteLine($"[UseItem] FALHA: {player.Username} tentou usar o item '{itemStack.ItemID}' que não é consumível.");
            return;
        }

        Console.WriteLine($"[UseItem] {player.Username} está usando o item '{consumableData.itemName}'.");

        // --- Aplicação dos Efeitos ---

        if (consumableData.InstantHealthGain > 0)
        {
            player.ReceiveHealing(consumableData.InstantHealthGain, _server);
        }

        if (consumableData.InstantResourceGain > 0)
        {
            player.CurrentResource = Math.Min(player.MaxResource, player.CurrentResource + consumableData.InstantResourceGain);
        }

        // CORREÇÃO: Apenas esta chamada é necessária.
        // O jogador é tanto o conjurador (caster) quanto o alvo (target).
        if (!string.IsNullOrEmpty(consumableData.StatusEffectID))
        {
            player.StatusEffectController.ApplyEffect(consumableData.StatusEffectID, player);
        }

        // --- Consumo do Item ---
        player.PlayerInventory.RemoveItemFromSlot(inventorySlot, 1);

        // --- Feedback para o Cliente ---
        // A notificação de stats/vitals já é tratada dentro do StatusEffectController
        // e dos métodos ReceiveHealing. Só precisamos atualizar o inventário.
        _server.NetworkManager.SendInventorySlotUpdate(player, inventorySlot, player.PlayerInventory.slots[inventorySlot]);
        _server.NetworkManager.SendVitalsUpdate(player);
    }


    /// <summary>
    /// Handler para requisições de compra de itens de um NPC.
    /// </summary>
    public void HandleBuyItemRequest(Player player, string npcId, string itemId, int quantity)
    {
        // --- Validações Iniciais (sem mudança) ---
        if (!_server.ActiveNpcs.TryGetValue(npcId, out var npcInstance) ||
            !DataManager.Vendors.TryGetValue(npcInstance.BaseData.TypeId, out var vendorData)) return;

        var itemForSaleTemplate = vendorData.Items.FirstOrDefault(i => i.ItemID == itemId);
        if (itemForSaleTemplate == null) return;

        if (!DataManager.Items.TryGetValue(itemId, out var itemTemplate)) return;

        long totalCost;

        // --- LÓGICA DE GERAÇÃO DINÂMICA PARA EQUIPAMENTOS ---
        if (itemTemplate is ServerEquipmentData eqTemplate)
        {
            // Se for um equipamento, o preço é dinâmico e só pode comprar um por vez.
            if (quantity > 1) return;

            // Calcula o preço dinâmico com base no nível do jogador.
            totalCost = ServerStatAllocator.CalculateBuyPrice(eqTemplate, player.Level);

            if (player.TotalBronze < totalCost) return; // Não tem dinheiro

            // Verifica se há espaço para UM item.
            if (player.PlayerInventory.FindEmptySlot() == null)
            {
                _server.NetworkManager.SendMessageToPlayer(player, "ERROR|Inventário cheio.");
                return;
            }

            // --- GERAÇÃO DO ITEM ---
            var itemStack = new ItemStack(itemId, 1);

            // Usamos o nível do jogador para gerar o iLvl e o ReqLvl
            int itemLevel = ItemLevelConverter.GetItemLevelForCreature(player.Level);
            int requiredLevel = ItemLevelConverter.GetRequiredLevelForItemLevel(itemLevel);

            // Itens de vendedor geralmente são de qualidade Incomum (verde).
            var (generatedStats, _) = ServerStatAllocator.GenerateStatsForItem(eqTemplate, itemLevel);

            var instanceData = new ItemInstanceData
            {
                Quality = ItemQuality.Uncommon, // Qualidade fixa para itens de vendedor
                ItemLevel = itemLevel,
                RequiredLevel = requiredLevel,
                Stats = generatedStats
            };

            // Registra a nova instância
            _server.ItemInstanceManager.RegisterGeneratedItem(itemStack.InstanceID, instanceData);

            // --- EXECUÇÃO DA TRANSAÇÃO ---
            player.TotalBronze -= totalCost;
            player.PlayerInventory.AddItemStack(itemStack);

            // Envia os dados da instância para o cliente
            _server.NetworkManager.SendItemInstanceData(player, itemStack.InstanceID, instanceData);
        }
        else // --- LÓGICA ANTIGA PARA ITENS NORMAIS (Consumíveis, Lixo) ---
        {
            totalCost = (long)itemForSaleTemplate.BuyPrice * quantity;
            if (player.TotalBronze < totalCost) return;
            if (!player.PlayerInventory.HasSpaceFor(itemId, quantity))
            {
                _server.NetworkManager.SendMessageToPlayer(player, "ERROR|Inventário cheio.");
                return;
            }

            player.TotalBronze -= totalCost;
            player.PlayerInventory.AddItem(itemId, quantity);
        }

        // --- ATUALIZAÇÕES FINAIS PARA O CLIENTE ---
        _server.NetworkManager.SendFullInventory(player, true);
        _server.NetworkManager.SendCurrencyUpdate(player, true);
    }

    /// <summary>
    /// Handler para requisições de venda de itens para um NPC.
    /// </summary>
    public void HandleSellItemRequest(Player player, string npcId, int inventorySlot, int quantity)
    {
        var inventory = player.PlayerInventory;
        if (!IsValidSlot(inventory, inventorySlot) || inventory.slots[inventorySlot] == null) return;
        ItemStack itemStack = inventory.slots[inventorySlot]!;


        if (!DataManager.Items.TryGetValue(itemStack.ItemID, out var itemData)) return;

        int sellQuantity = Math.Min(quantity, itemStack.Quantity);
        long totalValueInBronze = (long)itemData.sellPrice * sellQuantity;

        // Execução da Transação
        itemStack.Quantity -= sellQuantity;
        if (itemStack.Quantity <= 0)
        {
            // <<< A LÓGICA DE LIMPEZA VEM AQUI >>>
            // Antes de remover o item do slot, pegamos seu InstanceID.
            string instanceIdToUnregister = itemStack.InstanceID;

            // Remove o item do inventário do jogador.
            inventory.slots[inventorySlot] = null;

            // Notifica o ItemInstanceManager para remover os dados desta instância, liberando memória.
            _server.ItemInstanceManager.UnregisterItem(instanceIdToUnregister);
            Console.WriteLine($"[ItemCleanup] Stats para o item {instanceIdToUnregister} foram removidos do cache.");
        }

        player.TotalBronze += totalValueInBronze;

        _server.NetworkManager.SendInventorySlotUpdate(player, inventorySlot, player.PlayerInventory.slots[inventorySlot]);
        _server.NetworkManager.SendCurrencyUpdate(player, true);
    }

    #endregion

    #region Métodos Auxiliares

    private bool IsValidSlot(Inventory inventory, int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < inventory.slots.Count;
    }

    #endregion
}