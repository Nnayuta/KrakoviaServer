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

        // Após aceitar, notifica o cliente sobre o novo estado desta quest
        if (player.QuestLog.ActiveQuests.TryGetValue(questId, out var progress))
        {
            _server.NetworkManager.SendQuestUpdate(player, progress);
        }
    }

    public void HandleCompleteQuestRequest(Player player, string questId)
    {
        if (!DataManager.Quests.TryGetValue(questId, out var questData)) return;

        // Verifica se o jogador está falando com o NPC correto e se os objetivos estão completos
        // TODO: Precisamos saber em quem o jogador está focado. Por enquanto, vamos ignorar a checagem de NPC.
        if (player.QuestLog.AreObjectivesComplete(questId))
        {
            GiveRewards(player, questData);
            player.QuestLog.CompleteQuest(questId);

            // Notifica o cliente que a quest está completa.
            _server.NetworkManager.SendQuestUpdate(player, new QuestProgress
            {
                QuestID = questId,
                Status = QuestStatus.Completed
            });
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
        if (player.QuestLog.ActiveQuests.ContainsKey(questId))
        {
            player.QuestLog.AbandonQuest(questId);

            // Após abandonar, envia o log de quests completo para ressincronizar o cliente
            _server.NetworkManager.SendFullQuestLog(player);
        }
    }


    // Chamado pelo CombatManager/NpcAiManager quando algo morre
    public void OnEntitySlain(Player killer, NpcInstance victim)
    {
        // Itera sobre uma cópia para evitar problemas de modificação da coleção
        foreach (var questProgress in killer.QuestLog.ActiveQuests.Values.ToList())
        {
            if (DataManager.Quests.TryGetValue(questProgress.QuestID, out var questData))
            {
                foreach (var objective in questData.Objectives)
                {
                    if (objective.Type == QuestObjectiveType.Slay &&
                        objective.TargetID == victim.BaseData.TypeId &&
                        questProgress.ObjectiveProgress[objective.TargetID] < objective.RequiredAmount)
                    {
                        questProgress.ObjectiveProgress[objective.TargetID]++;

                        // Notifica o cliente sobre o progresso
                        _server.NetworkManager.SendQuestUpdate(killer, questProgress);
                    }
                }
            }
        }
    }
}