// Servidor/Models/Equipment.cs
using System;
using System.Collections.Generic;

public class Equipment
{
    public event Action<ServerItemData, ServerItemData> OnEquipmentChanged;
    public Dictionary<EquipmentSlot, ItemStack> equippedItems { get; private set; } = new();

    public Equipment()
    {
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            equippedItems[slot] = null;
        }
    }

    public ItemStack GetItemInSlot(EquipmentSlot slot)
    {
        return equippedItems.GetValueOrDefault(slot);
    }

    public ItemStack SetItemInSlot(EquipmentSlot slot, ItemStack? newItemStack)
    {
        // Pega o item antigo ANTES de qualquer modificação
        ItemStack oldItemStack = equippedItems.GetValueOrDefault(slot);
        if (oldItemStack == newItemStack) return oldItemStack;

        // Atualiza o dicionário
        equippedItems[slot] = newItemStack;

        ServerItemData? oldItemData = null;
        // Apenas tenta buscar o item antigo se um existia
        if (oldItemStack != null && !string.IsNullOrEmpty(oldItemStack.ItemID))
        {
            DataManager.Items.TryGetValue(oldItemStack.ItemID, out oldItemData);
        }

        ServerItemData? newItemData = null;
        // Apenas tenta buscar o item novo se um foi fornecido
        if (newItemStack != null && !string.IsNullOrEmpty(newItemStack.ItemID))
        {
            DataManager.Items.TryGetValue(newItemStack.ItemID, out newItemData);
        }

        // Dispara o evento com os dados encontrados (que podem ser nulos)
        OnEquipmentChanged?.Invoke(oldItemData, newItemData);

        return oldItemStack;
    }

    public WeaponType? GetMainHandWeaponType()
    {
        ItemStack? mainHandStack = GetItemInSlot(EquipmentSlot.MainHand);
        if (mainHandStack == null) return null;

        if (DataManager.Items.TryGetValue(mainHandStack.ItemID, out var itemData) && itemData is ServerWeaponData weaponData)
        {
            return weaponData.weaponType;
        }
        return null;
    }
}