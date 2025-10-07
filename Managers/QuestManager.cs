// Servidor/Managers/QuestManager.cs
using System.Linq;

public class QuestManager
{
    private readonly UDPServer _server;
    public QuestManager(UDPServer server)
    {
        _server = server;
    }



    public void HandleAcceptQuestRequest(Player player, string questId)
    {
        if (!player.QuestLog.CanAcceptQuest(questId)) return;

        player.QuestLog.AcceptQuest(questId);

        // Acessa o progresso através do dicionário principal
        if (player.QuestLog.AllQuests.TryGetValue(questId, out var progress))
        {
            _server.NetworkManager.SendQuestUpdate(player, progress);
        }
    }

    public void HandleCompleteQuestRequest(Player player, string questId)
    {
        if (!DataManager.Quests.TryGetValue(questId, out var questData)) return;

        if (player.QuestLog.AreObjectivesComplete(questId))
        {
            GiveRewards(player, questData);
            player.QuestLog.CompleteQuest(questId);

            // Pega o progresso atualizado para enviar ao cliente
            if (player.QuestLog.AllQuests.TryGetValue(questId, out var progress))
            {
                _server.NetworkManager.SendQuestUpdate(player, progress);
            }
        }
    }

    private void GiveRewards(Player player, ServerQuestData quest)
    {
        foreach (var reward in quest.GuaranteedRewards)
        {
            switch (reward.Type)
            {
                case QuestRewardType.Experience:
                    _server.PlayerProgressionManager.GrantExperience(player, (int)reward.Amount);
                    break;
                case QuestRewardType.Currency:
                    player.TotalBronze += reward.Amount;
                    _server.NetworkManager.SendCurrencyUpdate(player); // Notifica o cliente
                    break;
                case QuestRewardType.Item:
                    if (!string.IsNullOrEmpty(reward.ItemID))
                    {
                        player.PlayerInventory.AddItem(reward.ItemID, (int)reward.Amount);
                    }
                    break;
                case QuestRewardType.Ability:
                    // TODO: Adicionar lógica para ensinar habilidades
                    break;
            }
        }
        _server.NetworkManager.SendInventoryUpdate(player); // Envia o inventário atualizado
    }


    public void HandleAbandonQuestRequest(Player player, string questId)
    {
        // A lógica de checagem agora é mais simples
        if (player.QuestLog.GetQuestStatus(questId) == QuestStatus.InProgress)
        {
            player.QuestLog.AbandonQuest(questId);
            _server.NetworkManager.SendFullQuestLog(player);
        }
    }


    public void OnEntitySlain(Player killer, NpcInstance victim)
    {
        // Itera sobre as quests ativas usando a nova propriedade de conveniência
        foreach (var questProgress in killer.QuestLog.ActiveQuests.ToList())
        {
            // O resto da lógica já estava correto
            if (DataManager.Quests.TryGetValue(questProgress.QuestID, out var questData))
            {
                foreach (var objective in questData.Objectives)
                {
                    if (objective.Type == QuestObjectiveType.Slay &&
                        objective.TargetID == victim.BaseData.TypeId &&
                        questProgress.ObjectiveProgress.TryGetValue(objective.TargetID, out int currentAmount) &&
                        currentAmount < objective.RequiredAmount)
                    {
                        questProgress.ObjectiveProgress[objective.TargetID]++;
                        _server.NetworkManager.SendQuestUpdate(killer, questProgress);
                    }
                }
            }
        }
    }
}