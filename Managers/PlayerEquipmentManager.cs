// Servidor/Managers/PlayerEquipmentManager.cs
using System;
using System.Linq;

public class PlayerEquipmentManager
{
    private readonly UDPServer _server;

    public PlayerEquipmentManager(UDPServer server)
    {
        _server = server;
    }

    /// <summary>
    /// Lida com uma requisição para EQUIPAR um item do inventário.
    /// </summary>
    public void HandleEquipItemRequest(Player player, int inventorySlot, EquipmentSlot equipmentSlot)
    {
        // --- VALIDAÇÃO (sem alterações) ---
        if (inventorySlot < 0 || inventorySlot >= player.PlayerInventory.slots.Count) return;
        ItemStack itemToEquipStack = player.PlayerInventory.slots[inventorySlot];
        if (itemToEquipStack == null) return;
        if (!DataManager.Items.TryGetValue(itemToEquipStack.ItemID, out ServerItemData itemData)) return;
        if (itemData is not ServerEquipmentData equipmentData) return;
        if (equipmentData.equipmentSlot != equipmentSlot) return;
        if (player.Level < equipmentData.requiredLevel) return;

        // --- LÓGICA DE ARMAS (sem alterações) ---
        if (equipmentData is ServerWeaponData weaponDataToEquip)
        {

            Console.WriteLine($"[Equip-Debug] Tentando equipar arma. Tipo: {weaponDataToEquip.weaponType}. " + $"Proficiências do jogador: [{string.Join(", ", player.CurrentWeaponProficiencies)}]");

            if (!player.CurrentWeaponProficiencies.Contains(weaponDataToEquip.weaponType)) {
                Console.WriteLine($"[Equip] FALHA: {player.Username} não tem proficiência para usar {weaponDataToEquip.weaponType}.");
                return;
            }


            if (weaponDataToEquip.handType == WeaponHandType.TwoHanded)
            {
                if (equipmentSlot != EquipmentSlot.MainHand)
                {
                    Console.WriteLine($"[Equip] FALHA: Armas de duas mãos só podem ser equipadas na Mão Principal.");

                    return;
                }
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

        // --- EXECUÇÃO DA TROCA ---
        Console.WriteLine($"[Equip] {player.Username} equipando '{equipmentData.itemName}' no slot {equipmentSlot}.");

        // =================================================================================
        // LÓGICA DE TROCA CORRIGIDA E ATÔMICA
        // =================================================================================
        // 1. Guarda uma referência do item que está atualmente equipado.
        ItemStack currentlyEquippedStack = player.PlayerEquipment.GetItemInSlot(equipmentSlot);

        // 2. Coloca o item antigo DIRETAMENTE no slot de inventário de onde o novo veio.
        player.PlayerInventory.slots[inventorySlot] = currentlyEquippedStack;

        // 3. Coloca o item novo no slot de equipamento. Esta é a única chamada que
        //    dispara o evento OnEquipmentChanged, garantindo uma única atualização de stats.
        player.PlayerEquipment.SetItemInSlot(equipmentSlot, itemToEquipStack);

        // As chamadas para UpdateCharacterState e SendFullStateToPlayer não são mais necessárias aqui,
        // pois o evento OnEquipmentChanged e o Player.cs agora cuidam disso.
        // No entanto, ainda precisamos notificar o cliente sobre a mudança no inventário.
        player.SendFullStateToClient();
    }

    /// <summary>
    /// Lida com uma requisição para DESEQUIPAR um item.
    /// </summary>
    public void HandleUnequipItemRequest(Player player, EquipmentSlot equipmentSlot)
    {
        ItemStack itemToUnequipStack = player.PlayerEquipment.GetItemInSlot(equipmentSlot);
        if (itemToUnequipStack == null) return;

        // Tenta adicionar o item de volta ao inventário.
        if (player.PlayerInventory.AddItem(itemToUnequipStack.ItemID, itemToUnequipStack.Quantity))
        {
            // Se conseguiu adicionar, remove o item do slot de equipamento.
            // Isso irá disparar o OnEquipmentChanged e recalcular os stats.
            player.PlayerEquipment.SetItemInSlot(equipmentSlot, null);
            Console.WriteLine($"[Equip] {player.Username} desequipou item do slot {equipmentSlot}.");

            player.SendFullStateToClient();
        }
        else
        {
            Console.WriteLine($"[Equip] FALHA: {player.Username} tentou desequipar, mas o inventário está cheio.");
            // TODO: Enviar uma mensagem de erro para o cliente.
        }
    }

    private void UnequipSlot(Player player, EquipmentSlot slot)
    {
        ItemStack itemStack = player.PlayerEquipment.GetItemInSlot(slot);
        if (itemStack == null) return;

        if (player.PlayerInventory.AddItem(itemStack.ItemID, itemStack.Quantity))
        {
            player.PlayerEquipment.SetItemInSlot(slot, null);
        }
    }
}