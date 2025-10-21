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

        // =================================================================================
        // <<< MUDANÇA PRINCIPAL AQUI >>>
        // =================================================================================

        // 1. Equipar itens iniciais E GERAR SEUS STATS
        foreach (string itemID in classData.StartingEquipmentIDs)
        {
            if (DataManager.Items.TryGetValue(itemID, out var itemTemplate) && itemTemplate is ServerEquipmentData eqItemTemplate)
            {
                // a. Cria o ItemStack. Ele ganha um InstanceID único automaticamente.
                var itemStack = new ItemStack(itemID, 1);

                // b. Gera os stats e a qualidade para o item.
                //    Para itens iniciais, o nível do item é sempre 1.
                var (generatedStats, finalQuality) = ServerStatAllocator.GenerateStatsForItem(eqItemTemplate, 1);

                // c. Cria o pacote de dados da instância.
                var instanceData = new ItemInstanceData
                {
                    Quality = finalQuality,
                    ItemLevel = 1,
                    Stats = generatedStats
                };

                // d. Registra a nova instância e seus stats no manager global.
                //    Usamos o Singleton do servidor para ter acesso ao manager.
                UDPServer.Instance.ItemInstanceManager.RegisterGeneratedItem(itemStack.InstanceID, instanceData);

                // e. Equipa o item no personagem.
                equipment.SetItemInSlot(eqItemTemplate.equipmentSlot, itemStack);
            }
        }

        // 2. Adicionar itens ao inventário (sem mudanças, geralmente são consumíveis sem stats gerados)
        foreach (string itemID in classData.StartingInventoryIDs)
        {
            inventory.AddItem(itemID);
        }

        // 3. Calcular habilidades conhecidas (sem mudanças)
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