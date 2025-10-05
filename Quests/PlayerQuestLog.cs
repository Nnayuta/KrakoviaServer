// Servidor/Quests/PlayerQuestLog.cs
using System.Collections.Generic;
using System.Linq;

public enum QuestStatus { NotStarted, InProgress, Completed }

public class PlayerQuestProgress
{
    public string QuestID;
    public QuestStatus Status;
    public Dictionary<string, int> ObjectiveProgress = new Dictionary<string, int>();
}

public class QuestProgress
{
    public string? QuestID;
    public QuestStatus Status;
    public Dictionary<string, int> ObjectiveProgress = new Dictionary<string, int>();

    // Construtor principal para quests ativas
    public QuestProgress(string questId)
    {
        QuestID = questId;
        Status = QuestStatus.InProgress;
        if (DataManager.Quests.TryGetValue(questId, out var questData))
        {
            foreach (var objective in questData.Objectives)
            {
                ObjectiveProgress[objective.TargetID] = 0;
            }
        }
    }

    // =================================================================================
    // NOVO: Adicionar um construtor vazio para facilitar a deserialização e a criação
    // de instâncias temporárias.
    // =================================================================================
    public QuestProgress() { }
}

// Classe que gerencia todas as quests de um único jogador.
public class PlayerQuestLog
{
    private readonly Player _owner;
    public Dictionary<string, QuestProgress> ActiveQuests { get; private set; } = new();
    public HashSet<string> CompletedQuests { get; private set; } = new();

    public PlayerQuestLog(Player owner)
    {
        _owner = owner;
    }

    public void AcceptQuest(string questId)
    {
        if (CanAcceptQuest(questId))
        {
            var newProgress = new QuestProgress(questId);
            ActiveQuests.Add(questId, newProgress);
            Console.WriteLine($"[Quest] Jogador '{_owner.Username}' aceitou a quest '{questId}'.");

            // TODO: Notificar o cliente sobre a nova quest.
        }
    }

    public bool CanAcceptQuest(string questId)
    {
        if (!DataManager.Quests.TryGetValue(questId, out var questData)) return false; // Quest não existe.
        if (GetQuestStatus(questId) != QuestStatus.NotStarted) return false; // Já tem ou completou.
        if (_owner.Level < questData.RequiredLevel) return false; // Nível baixo.

        // Verifica os pré-requisitos.
        foreach (string prereqId in questData.PrerequisiteQuestIDs)
        {
            if (!CompletedQuests.Contains(prereqId)) return false;
        }
        return true;
    }

    public void AbandonQuest(string questId)
    {
        if (ActiveQuests.ContainsKey(questId))
        {
            ActiveQuests.Remove(questId);
            Console.WriteLine($"[Quest] Jogador '{_owner.Username}' abandonou a quest '{questId}'.");
        }
    }

    public void CompleteQuest(string questId)
    {
        if (ActiveQuests.Remove(questId)) // Remove da lista de ativas
        {
            CompletedQuests.Add(questId); // Adiciona na lista de completas
            Console.WriteLine($"[Quest] Jogador '{_owner.Username}' completou a quest '{questId}'.");
        }
    }

    public bool AreObjectivesComplete(string questId)
    {
        // Se a quest não está ativa, não pode ser completada
        if (!ActiveQuests.TryGetValue(questId, out var progress)) return false;

        // Se os dados da quest não existem, não pode ser completada
        if (!DataManager.Quests.TryGetValue(questId, out var questData)) return false;

        // Itera por todos os objetivos definidos para a quest
        foreach (var objective in questData.Objectives)
        {
            // Pega o progresso atual do jogador para este objetivo
            progress.ObjectiveProgress.TryGetValue(objective.TargetID, out int currentAmount);

            // Se o progresso atual for menor que o necessário, os objetivos não estão completos
            if (currentAmount < objective.RequiredAmount)
            {
                return false;
            }
        }

        // Se o loop terminar sem retornar falso, todos os objetivos foram cumpridos
        return true;
    }

    public QuestStatus GetQuestStatus(string questId)
    {
        if (CompletedQuests.Contains(questId)) return QuestStatus.Completed;
        if (ActiveQuests.ContainsKey(questId)) return QuestStatus.InProgress;
        return QuestStatus.NotStarted;
    }
}