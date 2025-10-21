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
    /// <<< MÉTODO NOVO E CRUCIAL >>>
    /// Adiciona um ItemStack pré-existente ao inventário, preservando seu InstanceID.
    /// Ideal para loot de monstros, trocas entre jogadores, etc.
    /// </summary>
    /// <param name="stackToAdd">O ItemStack a ser adicionado.</param>
    /// <returns>True se o item foi adicionado com sucesso.</returns>
    public bool AddItemStack(ItemStack stackToAdd)
    {
        if (stackToAdd == null) return false;
        if (!DataManager.Items.TryGetValue(stackToAdd.ItemID, out var itemData)) return false;

        // Para itens não empilháveis (como equipamentos), simplesmente encontra um slot vazio.
        if (!itemData.isStackable)
        {
            int? emptySlot = FindEmptySlot();
            if (emptySlot.HasValue)
            {
                slots[emptySlot.Value] = stackToAdd;
                return true;
            }
            return false; // Inventário cheio
        }
        else
        {
            // Para itens empilháveis, primeiro tenta adicionar ao stack existente (se houver).
            // (Esta é uma lógica mais complexa que podemos simplificar por enquanto)
            // Por simplicidade na jam, vamos assumir que loot empilhável também vai para um novo slot.
            int? emptySlot = FindEmptySlot();
            if (emptySlot.HasValue)
            {
                slots[emptySlot.Value] = stackToAdd;
                return true;
            }
            return false;
        }
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