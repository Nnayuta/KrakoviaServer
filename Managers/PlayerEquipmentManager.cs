// ARQUIVO COMPLETO E CORRIGIDO: Managers/PlayerEquipmentManager.cs

using System;
using System.Linq;

public class PlayerEquipmentManager
{
    private readonly UDPServer _server;

    public PlayerEquipmentManager(UDPServer server)
    {
        _server = server;
    }

    public void HandleEquipItemRequest(Player player, int inventorySlot, EquipmentSlot equipmentSlot)
    {
        // --- VALIDAÇÃO BÁSICA ---
        if (inventorySlot < 0 || inventorySlot >= player.PlayerInventory.slots.Count) return;
        ItemStack itemToEquipStack = player.PlayerInventory.slots[inventorySlot];
        if (itemToEquipStack == null) return;

        // Pega o template para informações genéricas (tipo de slot, etc.)
        if (!DataManager.Items.TryGetValue(itemToEquipStack.ItemID, out ServerItemData itemTemplate)) return;
        if (itemTemplate is not ServerEquipmentData eqTemplate || eqTemplate.equipmentSlot != equipmentSlot) return;
        int requiredLevel = eqTemplate.requiredLevel;
        var instanceData = _server.ItemInstanceManager.GetDataForInstance(itemToEquipStack.InstanceID);
        if (instanceData != null)
        {
            // Se encontrou dados de instância, o nível requerido é o que foi gerado!
            requiredLevel = instanceData.RequiredLevel;
        }

        // Agora, faz a verificação usando o nível correto.
        if (player.Level < requiredLevel)
        {
            Console.WriteLine($"[Equip] FALHA: {player.Username} (Nível {player.Level}) tentou equipar item que requer nível {requiredLevel}.");
            _server.NetworkManager.SendMessageToPlayer(player, "ERROR|Você não tem o nível necessário para equipar este item.");
            return;
        }
        // =================================================================================

        // --- VALIDAÇÕES DE ARMA (sem alteração) ---
        if (eqTemplate is ServerWeaponData weaponData)
        {
            if (!player.CurrentWeaponProficiencies.Contains(weaponData.weaponType)) return;
            if (weaponData.handType == WeaponHandType.TwoHanded)
            {
                if (equipmentSlot != EquipmentSlot.MainHand) return;
                UnequipSlot(player, EquipmentSlot.OffHand);
            }
        }
        if (equipmentSlot == EquipmentSlot.OffHand)
        {
            ItemStack mainHandStack = player.PlayerEquipment.GetItemInSlot(EquipmentSlot.MainHand);
            if (mainHandStack != null && DataManager.Items.TryGetValue(mainHandStack.ItemID, out var mainHandItem) &&
                mainHandItem is ServerWeaponData mainHandWeapon && mainHandWeapon.handType == WeaponHandType.TwoHanded)
            {
                return;
            }
        }

        // --- EXECUÇÃO DA TROCA (sem alteração) ---
        Console.WriteLine($"[Equip] {player.Username} tentando equipar '{itemToEquipStack.ItemID}'.");

        ItemStack currentlyEquippedStack = player.PlayerEquipment.GetItemInSlot(equipmentSlot);
        player.PlayerInventory.slots[inventorySlot] = null;
        if (currentlyEquippedStack != null)
        {
            player.PlayerInventory.AddItemStack(currentlyEquippedStack);
        }

        player.EquipItem(itemToEquipStack, equipmentSlot);
    }

    // O resto da classe (HandleUnequipItemRequest, UnequipSlot) pode continuar exatamente como está.
    public void HandleUnequipItemRequest(Player player, EquipmentSlot equipmentSlot)
    {
        ItemStack? itemToUnequipStack = player.PlayerEquipment.GetItemInSlot(equipmentSlot);
        if (itemToUnequipStack == null) return;

        var changedSlots = player.PlayerInventory.AddItemStack(itemToUnequipStack);
        if (changedSlots.Any())
        {
            player.UnequipItem(equipmentSlot);
        }
        else
        {
            Console.WriteLine($"[Equip] FALHA: Inventário de {player.Username} está cheio.");
            _server.NetworkManager.SendMessageToPlayer(player, "ERROR|Inventário cheio.");
        }
    }

    private void UnequipSlot(Player player, EquipmentSlot slot)
    {
        ItemStack itemStack = player.PlayerEquipment.GetItemInSlot(slot);
        if (itemStack == null) return;

        var changedSlots = player.PlayerInventory.AddItemStack(itemStack);
        if (changedSlots.Any())
        {
            player.PlayerEquipment.SetItemInSlot(slot, null);
        }
    }
}