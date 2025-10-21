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
    // ARQUIVO ATUALIZADO: Managers/PlayerInventoryManager.cs

    public void GrantLootToPlayer(Player player, List<ItemStack> lootItems)
    {
        if (lootItems == null || !lootItems.Any())
        {
            _server.NetworkManager.SendMessageToPlayer(player, "SHOW_FEEDBACK|Você não encontrou nada.");
            return;
        }

        foreach (var itemStack in lootItems)
        {
            // <<< A CORREÇÃO >>>
            // Usamos o novo método AddItemStack que preserva o InstanceID.
            if (player.PlayerInventory.AddItemStack(itemStack))
            {
                // Sucesso!
                var itemData = DataManager.Items[itemStack.ItemID];
                string feedback = itemStack.Quantity > 1 ? $"+{itemStack.Quantity} {itemData.itemName}" : $"+{itemData.itemName}";
                _server.NetworkManager.SendMessageToPlayer(player, $"SHOW_FEEDBACK|{feedback}");

                // AGORA, enviamos os dados da instância para o cliente, pois o item foi adicionado com sucesso.
                var instanceData = _server.ItemInstanceManager.GetDataForInstance(itemStack.InstanceID);
                if (instanceData != null)
                {
                    _server.NetworkManager.SendItemInstanceData(player, itemStack.InstanceID, instanceData);
                }
            }
            else
            {
                // Falha! O inventário está cheio.
                _server.NetworkManager.SendMessageToPlayer(player, "ERROR|Inventário cheio.");
                // TODO: Lógica de correio ou dropar no chão.
                break;
            }
        }

        // Após a tentativa de adicionar, sempre envia a atualização do inventário.
        _server.NetworkManager.SendInventoryUpdate(player);
    }
    /// <summary>
    /// Handler principal para a movimentação de itens. Cobre arrastar e soltar,
    /// trocar, mover para slot vazio, e empilhar.
    /// </summary>
    public void HandleMoveItemRequest(Player player, int fromSlot, int toSlot)
    {
        var inventory = player.PlayerInventory;

        // Validação básica
        if (!IsValidSlot(inventory, fromSlot) || !IsValidSlot(inventory, toSlot) || fromSlot == toSlot) return;

        ItemStack? fromItemStack = inventory.slots[fromSlot];
        ItemStack? toItemStack = inventory.slots[toSlot];

        // Caso 1: Movendo para um slot vazio (ou trocando com um slot vazio)
        if (toItemStack == null)
        {
            inventory.slots[toSlot] = fromItemStack;
            inventory.slots[fromSlot] = null;
        }
        // Caso 2: Os dois slots têm itens. Tentamos trocar ou empilhar.
        else
        {
            // Se os itens são do mesmo tipo e empilháveis...
            if (fromItemStack != null && fromItemStack.ItemID == toItemStack.ItemID &&
                DataManager.Items.TryGetValue(fromItemStack.ItemID, out var itemData) && itemData.isStackable)
            {
                // Empilha os itens
                int spaceInStack = itemData.maxStackSize - toItemStack.Quantity;
                int amountToMove = Math.Min(fromItemStack.Quantity, spaceInStack);

                toItemStack.Quantity += amountToMove;
                fromItemStack.Quantity -= amountToMove;

                // Se o stack de origem ficou vazio, remove-o.
                if (fromItemStack.Quantity <= 0)
                {
                    inventory.slots[fromSlot] = null;
                }
            }
            else
            {
                // Se não puder empilhar, simplesmente troca os slots.
                (inventory.slots[fromSlot], inventory.slots[toSlot]) = (inventory.slots[toSlot], inventory.slots[fromSlot]);
            }
        }

        // Após qualquer modificação, envia o estado atualizado para o cliente.
        _server.NetworkManager.SendInventoryUpdate(player);
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
        _server.NetworkManager.SendInventoryUpdate(player);
        _server.NetworkManager.SendVitalsUpdate(player); // É bom garantir a atualização dos vitals também.
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
        _server.NetworkManager.SendInventoryUpdate(player, true);
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

        Console.WriteLine($"[Loja] {player.Username} vendeu {sellQuantity}x {itemData.itemName}. Saldo restante: {new Currency(player.TotalBronze)}");
        _server.NetworkManager.SendInventoryUpdate(player);
        _server.NetworkManager.SendMessageToPlayer(player, $"CURRENCY_UPDATE|{player.TotalBronze}");
    }

    #endregion

    #region Métodos Auxiliares

    private bool IsValidSlot(Inventory inventory, int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < inventory.slots.Count;
    }

    #endregion
}