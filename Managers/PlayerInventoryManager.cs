// Managers/PlayerInventoryManager.cs
using System;
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
            // Se não há loot, podemos enviar uma mensagem de "não encontrou nada"
            _server.NetworkManager.SendMessageToClient("SHOW_FEEDBACK|Você não encontrou nada.", player.EndPoint);
            return;
        }

        foreach (var itemStack in lootItems)
        {
            // Tenta adicionar o item usando o método que já existe no inventário do jogador.
            if (player.PlayerInventory.AddItem(itemStack.ItemID, itemStack.Quantity))
            {
                // Sucesso! Envia feedback visual para o cliente.
                var itemData = DataManager.Items[itemStack.ItemID];
                string feedback = itemStack.Quantity > 1 ? $"+{itemStack.Quantity} {itemData.itemName}" : $"+{itemData.itemName}";
                _server.NetworkManager.SendMessageToClient($"SHOW_FEEDBACK|{feedback}", player.EndPoint);
            }
            else
            {
                // Falha! O inventário está cheio.
                _server.NetworkManager.SendMessageToClient("ERROR|Inventário cheio.", player.EndPoint);

                // TODO: Lógica futura para enviar o item pelo correio ou dropá-lo no chão.
                // Por enquanto, paramos de adicionar o resto.
                break;
            }
        }

        // Após adicionar todos os itens (ou falhar), envia uma atualização completa do inventário.
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

    /// <summary>
    /// Handler para requisições de compra de itens de um NPC.
    /// </summary>
    public void HandleBuyItemRequest(Player player, string npcId, string itemId, int quantity)
    {
        // Validação 1: O NPC é um vendedor válido?
        // Usamos o 'TypeId' do NpcInstance para encontrar os dados do vendedor
        if (!_server.ActiveNpcs.TryGetValue(npcId, out var npcInstance) ||
            !DataManager.Vendors.TryGetValue(npcInstance.BaseData.TypeId, out var vendorData))
        {
            Console.WriteLine($"[Loja] FALHA: Requisição de compra para um NPC que não é vendedor ({npcId}).");
            return;
        }

        // Validação 2: O vendedor vende este item?
        var itemForSale = vendorData.Items.FirstOrDefault(i => i.ItemID == itemId);
        if (itemForSale == null)
        {
            Console.WriteLine($"[Loja] FALHA: {player.Username} tentou comprar o item {itemId} que o NPC {npcId} não vende.");
            return;
        }

        // Validação 3: O jogador tem dinheiro/moeda suficiente?
        long totalCostInBronze = (long)itemForSale.BuyPrice * quantity;

        // if (!player.HasCurrency(vendorData.CurrencyType, totalCost)) return; // Lógica de moeda mais avançada
        if (player.TotalBronze < totalCostInBronze)
        {
            Console.WriteLine($"[Loja] FALHA: {player.Username} não tem {totalCostInBronze} de bronze.");
            return;
        }

        // Validação 4: O jogador tem espaço no inventário?
        if (!player.PlayerInventory.HasSpaceFor(itemId, quantity))
        {
            Console.WriteLine($"[Loja] FALHA: {player.Username} não tem espaço no inventário.");
            // Enviar uma mensagem de erro para o cliente é uma boa ideia.
            return;
        }

        // Execução da Transação
        // player.RemoveCurrency(vendorData.CurrencyType, totalCost);
        player.TotalBronze -= totalCostInBronze;
        player.PlayerInventory.AddItem(itemId, quantity);

        _server.NetworkManager.SendInventoryUpdate(player);

        // Futuramente, você enviará uma atualização de moeda
        _server.NetworkManager.SendMessageToClient($"CURRENCY_UPDATE|{player.TotalBronze}", player.EndPoint);
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

        // TODO: Verificar se o NPC é um vendedor que pode comprar este item.

        int sellQuantity = Math.Min(quantity, itemStack.Quantity);
        long totalValueInBronze = (long)itemData.sellPrice * sellQuantity;

        // Execução da Transação
        itemStack.Quantity -= sellQuantity;
        if (itemStack.Quantity <= 0)
        {
            inventory.slots[inventorySlot] = null;
        }

        player.TotalBronze += totalValueInBronze;

        Console.WriteLine($"[Loja] {player.Username} vendeu {sellQuantity}x {itemData.itemName}. Saldo restante: {new Currency(player.TotalBronze)}");
        _server.NetworkManager.SendInventoryUpdate(player);
        _server.NetworkManager.SendMessageToClient($"CURRENCY_UPDATE|{player.TotalBronze}", player.EndPoint);
    }

    #endregion

    #region Métodos Auxiliares

    private bool IsValidSlot(Inventory inventory, int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < inventory.slots.Count;
    }

    #endregion
}