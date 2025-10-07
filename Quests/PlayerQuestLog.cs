// Servidor/Quests/PlayerQuestLog.cs
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public enum QuestStatus { NotStarted, InProgress, Completed }

public class PlayerQuestProgress
{
    public string QuestID;
    public QuestStatus Status;
    public Dictionary<string, int> ObjectiveProgress = new Dictionary<string, int>();
}

// Esta classe agora é o DTO (Data Transfer Object) para o progresso
public class QuestProgress
{
    public string QuestID { get; set; }
    public QuestStatus Status { get; set; }

    // Este dicionário será salvo como JSON no banco de dados
    public Dictionary<string, int> ObjectiveProgress { get; set; } = new Dictionary<string, int>();

    // Timestamp de conclusão, crucial para quests diárias
    [JsonIgnore] // Não precisa ir para o cliente, mas o BD usa
    public DateTime? CompletionTime { get; set; }

    // Construtores
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
    public QuestProgress() { } // Construtor vazio para deserialização
}

// Classe que gerencia todas as quests de um único jogador.
public class PlayerQuestLog
{
    // Usamos um único dicionário como fonte da verdade. É mais simples de gerenciar e salvar.
    public Dictionary<string, QuestProgress> AllQuests { get; private set; } = new Dictionary<string, QuestProgress>();

    [JsonIgnore]
    private Player _owner; // << Mude para 'private'

    public PlayerQuestLog(Player owner)
    {
        _owner = owner;
    }

    public void SetOwner(Player owner)
    {
        _owner = owner;
    }

    // Construtor vazio para o Newtonsoft.Json
    public PlayerQuestLog() { }

    public void AcceptQuest(string questId)
    {
        if (!CanAcceptQuest(questId)) return;

        var newProgress = new QuestProgress(questId);
        AllQuests[questId] = newProgress;
        Console.WriteLine($"[Quest] Jogador '{_owner.CharacterName}' aceitou a quest '{questId}'.");
    }

    public void CompleteQuest(string questId)
    {
        if (!AllQuests.TryGetValue(questId, out var progress) || progress.Status != QuestStatus.InProgress) return;

        progress.Status = QuestStatus.Completed;
        progress.CompletionTime = DateTime.UtcNow; // Salva o timestamp!
        Console.WriteLine($"[Quest] Jogador '{_owner.CharacterName}' completou a quest '{questId}'.");
    }

    public bool CanAcceptQuest(string questId)
    {
        if (!DataManager.Quests.TryGetValue(questId, out var questData)) return false;

        // Regras básicas
        QuestStatus currentStatus = GetQuestStatus(questId);
        if (currentStatus != QuestStatus.NotStarted) return false;
        if (_owner.Level < questData.RequiredLevel) return false;

        // Pré-requisitos
        foreach (string prereqId in questData.PrerequisiteQuestIDs)
        {
            if (GetQuestStatus(prereqId) != QuestStatus.Completed) return false;
        }

        // >> LÓGICA DE QUEST DIÁRIA <<
        if (questData.Category == QuestCategory.Daily)
        {
            if (AllQuests.TryGetValue(questId, out var oldProgress) && oldProgress.Status == QuestStatus.Completed)
            {
                // TODO: Uma lógica de "reset diário" (ex: todo dia às 8h) é melhor, mas para a jam, 24h funciona.
                if (oldProgress.CompletionTime.HasValue && DateTime.UtcNow < oldProgress.CompletionTime.Value.AddHours(24))
                {
                    Console.WriteLine($"[Quest] Tentativa de aceitar a quest diária '{questId}' antes do reset.");
                    return false; // Não pode aceitar, ainda não se passaram 24h.
                }
            }
        }

        return true;
    }

    public QuestStatus GetQuestStatus(string questId)
    {
        if (AllQuests.TryGetValue(questId, out var progress))
        {
            return progress.Status;
        }
        return QuestStatus.NotStarted;
    }

    public void AbandonQuest(string questId)
    {
        // A lógica de abandonar agora apenas remove a quest do dicionário principal.
        if (AllQuests.TryGetValue(questId, out var progress) && progress.Status == QuestStatus.InProgress)
        {
            AllQuests.Remove(questId);
            Console.WriteLine($"[Quest] Jogador '{_owner.CharacterName}' abandonou a quest '{questId}'.");
        }
    }

    public bool AreObjectivesComplete(string questId)
    {
        if (!AllQuests.TryGetValue(questId, out var progress) || progress.Status != QuestStatus.InProgress) return false;
        if (!DataManager.Quests.TryGetValue(questId, out var questData)) return false;

        foreach (var objective in questData.Objectives)
        {
            progress.ObjectiveProgress.TryGetValue(objective.TargetID, out int currentAmount);
            if (currentAmount < objective.RequiredAmount)
            {
                return false;
            }
        }
        return true;
    }

    // =================================================================================
    // >> PROPRIEDADES DE CONVENIÊNCIA ADICIONADAS AQUI <<
    // Para que o código antigo no QuestManager e NetworkManager funcione com poucas mudanças.
    // =================================================================================

    [JsonIgnore]
    public IEnumerable<QuestProgress> ActiveQuests => AllQuests.Values.Where(q => q.Status == QuestStatus.InProgress);
    [JsonIgnore]
    public IEnumerable<string> CompletedQuestIDs => AllQuests.Where(kvp => kvp.Value.Status == QuestStatus.Completed).Select(kvp => kvp.Key);

}