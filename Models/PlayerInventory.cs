// Models/PlayerInventory.cs
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
    /// Adiciona um item ao inventário, tentando empilhar primeiro.
    /// Retorna true se o item foi adicionado completamente.
    /// </summary>
    public bool AddItem(string itemID, int quantity = 1)
    {
        if (!DataManager.Items.TryGetValue(itemID, out var itemData)) return false;

        // 1. Tenta empilhar em stacks existentes.
        // ESTA LINHA AGORA FUNCIONA!
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

        // 2. Se ainda restarem itens, tenta adicioná-los a slots vazios.
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


    public void RemoveItem(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < slots.Count)
        {
            slots[slotIndex] = null;
        }
    }

    public bool RemoveItemFromSlot(int slotIndex, int quantity)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count || slots[slotIndex] == null)
        {
            return false; // Slot inválido ou vazio
        }

        var itemStack = slots[slotIndex]!; // Usamos '!' para dizer ao compilador que não é nulo aqui.
        if (itemStack.Quantity < quantity)
        {
            return false; // Não há itens suficientes para remover
        }

        itemStack.Quantity -= quantity;

        if (itemStack.Quantity <= 0)
        {
            slots[slotIndex] = null; // Remove o item completamente se o stack acabar
        }

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

    /// <summary>
    /// Verifica se há espaço para uma certa quantidade de um item.
    /// </summary>
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