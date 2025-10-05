using System.Collections.Generic;
using System.Linq;

public static class CharacterStateGenerator
{
    public static (Inventory, Equipment, ActionBarData, List<string>) GenerateInitialState(string classId, int level)
    {
        var inventory = new Inventory(20);
        var equipment = new Equipment();
        var actionBar = new ActionBarData(12);

        if (!DataManager.Classes.TryGetValue(classId, out var classData))
        {
            return (inventory, equipment, actionBar, new List<string>());
        }

        // 1. Equipar itens iniciais
        foreach (string itemID in classData.StartingEquipmentIDs)
        {
            if (DataManager.Items.TryGetValue(itemID, out var itemData) && itemData is ServerEquipmentData eqData)
            {
                equipment.SetItemInSlot(eqData.equipmentSlot, new ItemStack(itemID, 1));
            }
        }

        // 2. Adicionar itens ao inventário
        foreach (string itemID in classData.StartingInventoryIDs)
        {
            inventory.AddItem(itemID);
        }

        // 3. Calcular habilidades conhecidas
        var knownAbilities = new List<string>();
        for (int i = 1; i <= level; i++)
        {
            if (classData.BaseAbilityUnlocks.TryGetValue(i, out var abilitiesToLearn))
            {
                knownAbilities.AddRange(abilitiesToLearn);
            }
        }

        return (inventory, equipment, actionBar, knownAbilities);
    }
}