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
    /// (MÉTODO ATUALIZADO) Adiciona um item e retorna os slots que foram alterados.
    /// </summary>
    /// <returns>Um dicionário com [índice do slot, novo ItemStack] para cada slot modificado.</returns>
    public Dictionary<int, ItemStack> AddItem(string itemID, int quantity = 1)
    {
        var changedSlots = new Dictionary<int, ItemStack>();
        if (!DataManager.Items.TryGetValue(itemID, out var itemData)) return changedSlots;

        // 1. Tenta empilhar em stacks existentes.
        if (itemData.isStackable)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot != null && slot.ItemID == itemID && slot.Quantity < itemData.maxStackSize)
                {
                    int spaceAvailable = itemData.maxStackSize - slot.Quantity;
                    int amountToAdd = Math.Min(quantity, spaceAvailable);
                    slot.Quantity += amountToAdd;
                    changedSlots[i] = slot; // Registra a mudança

                    quantity -= amountToAdd;
                    if (quantity <= 0) return changedSlots;
                }
            }
        }

        // 2. Se ainda restarem itens, adiciona a slots vazios.
        while (quantity > 0)
        {
            int? emptySlotIndex = FindEmptySlot();
            if (!emptySlotIndex.HasValue) return changedSlots; // Inventário cheio

            int amountToAdd = Math.Min(quantity, itemData.maxStackSize);
            var newStack = new ItemStack(itemID, amountToAdd);
            slots[emptySlotIndex.Value] = newStack;
            changedSlots[emptySlotIndex.Value] = newStack; // Registra a mudança

            quantity -= amountToAdd;
        }

        return changedSlots;
    }

    /// <summary>
    /// (MÉTODO ATUALIZADO) Adiciona um ItemStack pré-existente e retorna os slots alterados.
    /// </summary>
    /// <returns>Um dicionário com [índice do slot, novo ItemStack] para cada slot modificado.</returns>
    public Dictionary<int, ItemStack> AddItemStack(ItemStack stackToAdd)
    {
        var changedSlots = new Dictionary<int, ItemStack>();
        if (stackToAdd == null || stackToAdd.Quantity <= 0) return changedSlots;
        if (!DataManager.Items.TryGetValue(stackToAdd.ItemID, out var itemData)) return changedSlots;

        if (itemData.isStackable)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot != null && slot.ItemID == stackToAdd.ItemID && slot.Quantity < itemData.maxStackSize)
                {
                    int spaceAvailable = itemData.maxStackSize - slot.Quantity;
                    int amountToAdd = Math.Min(stackToAdd.Quantity, spaceAvailable);

                    slot.Quantity += amountToAdd;
                    changedSlots[i] = slot; // Registra a mudança

                    stackToAdd.Quantity -= amountToAdd;
                    if (stackToAdd.Quantity <= 0)
                    {
                        UDPServer.Instance?.ItemInstanceManager.UnregisterItem(stackToAdd.InstanceID);
                        return changedSlots;
                    }
                }
            }
        }

        // Adiciona o resto (ou o item não empilhável) a um novo slot.
        if (stackToAdd.Quantity > 0)
        {
            int? emptySlotIndex = FindEmptySlot();
            if (emptySlotIndex.HasValue)
            {
                slots[emptySlotIndex.Value] = stackToAdd;
                changedSlots[emptySlotIndex.Value] = stackToAdd; // Registra a mudança
            }
        }

        return changedSlots;
    }

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