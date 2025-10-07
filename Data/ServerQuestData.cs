// Servidor/Data/ServerQuestData.cs
using System.Collections.Generic;

// Definimos os enums novamente no servidor
public enum QuestObjectiveType { Slay, Collect, GoTo }
public enum QuestRewardType { Item, Experience, Currency, Ability }

public class ServerQuestObjective
{
    public QuestObjectiveType Type { get; set; }
    public string TargetID { get; set; }
    public int RequiredAmount { get; set; }
}

public class ServerQuestReward
{
    public QuestRewardType Type { get; set; }
    public string? ItemID { get; set; }
    public long Amount { get; set; }
    public string? AbilityID { get; set; }
}

public enum QuestCategory { Main, Side, Daily, Weekly }

public class ServerQuestData
{
    public string QuestID { get; set; }
    public QuestCategory Category { get; set; }
    public int RequiredLevel { get; set; }
    public List<string> PrerequisiteQuestIDs { get; set; } = new List<string>();
    public string QuestGiverID { get; set; }
    public string QuestCompleterID { get; set; }
    public List<ServerQuestObjective> Objectives { get; set; } = new List<ServerQuestObjective>();
    public List<ServerQuestReward> GuaranteedRewards { get; set; } = new List<ServerQuestReward>();
    public List<ServerQuestReward> ChooseOneRewards { get; set; } = new List<ServerQuestReward>();
}