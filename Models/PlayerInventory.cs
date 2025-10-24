// ARQUIVO COMPLETO E CORRIGIDO: Models/PlayerInventory.cs

using System;
using System.Collections.Generic;
using System.Linq;

public class ItemStack
{
    public string InstanceID { get; set; }
    public string ItemID { get; set; }
    public int Quantity { get; set; }

    public ItemStack(string itemID, int quantity)
    {
        InstanceID = Guid.NewGuid().ToString("N");
        ItemID = itemID;
        Quantity = quantity;
    }
}

public class Inventory
{
    public List<ItemStack?> slots;

    public Inventory(int size)
    {
        slots = new List<ItemStack?>(new ItemStack?[size]);
    }

    /// <summary>
    /// Adiciona um item ao inventário pelo seu ID, criando novos stacks.
    /// Ideal para itens de quest, itens comprados, ou qualquer item que não tenha uma instância pré-existente.
    /// </summary>
    public bool AddItem(string itemID, int quantity = 1)
    {
        if (!DataManager.Items.TryGetValue(itemID, out var itemData)) return false;

        // 1. Tenta empilhar em stacks existentes.
        if (itemData.isStackable)
        {
            foreach (var slot in slots)
            {
                if (slot != null && slot.ItemID == itemID && slot.Quantity < itemData.maxStackSize)
                {
                    int spaceAvailable = itemData.maxStackSize - slot.Quantity;
                    int amountToAdd = Math.Min(quantity, spaceAvailable);
                    slot.Quantity += amountToAdd;
                    quantity -= amountToAdd;
                    if (quantity <= 0) return true;
                }
            }
        }

        // 2. Se ainda restarem itens, tenta adicioná-los a slots vazios, criando novos stacks.
        while (quantity > 0)
        {
            int? emptySlot = FindEmptySlot();
            if (!emptySlot.HasValue) return false; // Inventário cheio

            int amountToAdd = Math.Min(quantity, itemData.maxStackSize);
            slots[emptySlot.Value] = new ItemStack(itemID, amountToAdd);
            quantity -= amountToAdd;
        }

        return true;
    }

    /// <summary>
    /// Adiciona um ItemStack pré-existente ao inventário, preservando seu InstanceID e empilhando quando possível.
    /// Ideal para loot de monstros.
    /// </summary>
    public bool AddItemStack(ItemStack stackToAdd)
    {
        if (stackToAdd == null || stackToAdd.Quantity <= 0) return false;
        if (!DataManager.Items.TryGetValue(stackToAdd.ItemID, out var itemData)) return false;

        // --- LÓGICA PARA ITENS EMPILHÁVEIS ---
        if (itemData.isStackable)
        {
            // 1. Tenta empilhar em stacks existentes do mesmo item.
            foreach (var slot in slots)
            {
                if (slot != null && slot.ItemID == stackToAdd.ItemID && slot.Quantity < itemData.maxStackSize)
                {
                    int spaceAvailable = itemData.maxStackSize - slot.Quantity;
                    int amountToAdd = Math.Min(stackToAdd.Quantity, spaceAvailable);

                    slot.Quantity += amountToAdd;
                    stackToAdd.Quantity -= amountToAdd;

                    // Se empilhamos tudo, o trabalho acabou.
                    if (stackToAdd.Quantity <= 0)
                    {
                        // Como o item original foi "consumido", podemos remover seus dados de instância.
                        // Isso evita que dados de itens "fantasmas" fiquem na memória.
                        UDPServer.Instance?.ItemInstanceManager.UnregisterItem(stackToAdd.InstanceID);
                        return true;
                    }
                }
            }

            // 2. Se ainda restam itens no stackToAdd, ele precisa de um novo slot.
            if (stackToAdd.Quantity > 0)
            {
                int? emptySlot = FindEmptySlot();
                if (emptySlot.HasValue)
                {
                    slots[emptySlot.Value] = stackToAdd;
                    return true;
                }
            }
            else
            {
                // Isso acontece se a quantidade foi totalmente empilhada.
                return true;
            }
        }
        // --- LÓGICA PARA ITENS NÃO EMPILHÁVEIS (EQUIPAMENTOS) ---
        else
        {
            int? emptySlot = FindEmptySlot();
            if (emptySlot.HasValue)
            {
                slots[emptySlot.Value] = stackToAdd;
                return true;
            }
        }

        // Se chegamos aqui, o inventário está cheio.
        return false;
    }

    // --- O RESTO DA CLASSE CONTINUA IGUAL ---

    public void RemoveItem(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < slots.Count)
        {
            slots[slotIndex] = null;
        }
    }

    public bool RemoveItemFromSlot(int slotIndex, int quantity)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count || slots[slotIndex] == null) return false;
        var itemStack = slots[slotIndex]!;
        if (itemStack.Quantity < quantity) return false;
        itemStack.Quantity -= quantity;
        if (itemStack.Quantity <= 0) slots[slotIndex] = null;
        return true;
    }

    public int? FindEmptySlot()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null) return i;
        }
        return null;
    }

    public bool HasSpaceFor(string itemID, int quantity)
    {
        if (!DataManager.Items.TryGetValue(itemID, out var itemData)) return false;
        int spaceAvailable = 0;
        foreach (var slot in slots)
        {
            if (slot != null && slot.ItemID == itemID && itemData.isStackable)
            {
                spaceAvailable += itemData.maxStackSize - slot.Quantity;
            }
            else if (slot == null)
            {
                spaceAvailable += itemData.maxStackSize;
            }
            if (spaceAvailable >= quantity) return true;
        }
        return false;
    }
}