// Servidor/Data/DataManager.cs
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class DataManager
{
    // Propriedades estáticas (permanecem as mesmas)
    public static Dictionary<string, NpcData> Npcs { get; private set; } = new();
    public static Dictionary<string, AbilityData> Abilities { get; private set; } = new();
    public static List<SpawnPoint> SpawnPoints { get; private set; } = new();
    public static Dictionary<string, ServerClassData> Classes { get; private set; } = new();
    public static Dictionary<string, ServerItemData> Items { get; private set; } = new();
    public static Dictionary<string, VendorData> Vendors { get; private set; } = new();
    public static Dictionary<string, ServerLootTable> LootTables { get; private set; } = new();
    public static Dictionary<string, ServerQuestData> Quests { get; private set; } = new();
    public static Dictionary<string, ServerStatusEffectData> StatusEffects { get; private set; } = new();
    public static Dictionary<string, GatherableData> Gatherables { get; private set; } = new();
    public static List<GatherableSpawnPoint> GatherableSpawnPoints { get; private set; } = new();

    private static readonly string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData");
    private static readonly JsonSerializerSettings jsonSettings = new()
    {
        TypeNameHandling = TypeNameHandling.Auto,
        SerializationBinder = new CustomSerializationBinder()
    };

    public static void LoadAllData()
    {
        LogInfo("=============================================");
        LogInfo("[DataManager] Iniciando carregamento de dados do jogo...");

        // Carrega dados sem dependências primeiro
        LoadDataToDictionary<AbilityListWrapper, AbilityData, string>("abilities.json", "Habilidades", w => w.Abilities, a => a.ID, dict => Abilities = dict);
        LoadDataToDictionary<List<ServerItemData>, ServerItemData, string>("items.json", "Itens", list => list, i => i.itemID, dict => Items = dict);
        LoadDataToDictionary<StatusEffectListWrapper, ServerStatusEffectData, string>("status_effects.json", "Status Effects", w => w.StatusEffects, e => e.EffectID, dict => StatusEffects = dict);
        LoadDataToDictionary<GatherableListWrapper, GatherableData, string>("gatherables.json", "Coletáveis", w => w.Gatherables, g => g.ID, dict => Gatherables = dict);
        LoadDataToDictionary<ClassListWrapper, ServerClassData, string>("classes.json", "Classes", w => w.Classes, c => c.ClassID, dict => Classes = dict);
        LoadDataToDictionary<LootTableWrapper, ServerLootTable, string>("loottables.json", "Tabelas de Loot", w => w.LootTables, lt => lt.LootTableID, dict => LootTables = dict);
        LoadDataToDictionary<VendorListWrapper, VendorData, string>("vendors.json", "Vendedores", w => w.Vendors, v => v.NpcTypeId, dict => Vendors = dict);
        LoadDataToDictionary<QuestListWrapper, ServerQuestData, string>("quests.json", "Quests", w => w.Quests, q => q.QuestID, dict => Quests = dict);

        // Carrega dados que podem ter dependências (como NPCs que dependem de Habilidades)
        LoadDataToDictionary<NpcListWrapper, NpcData, string>("npcs.json", "NPCs", w => w.Npcs, n => n.TypeId, dict => Npcs = dict, PostProcessNpcs);

        // Carrega dados que são apenas listas
        LoadDataToList<SpawnPointListWrapper, SpawnPoint>("spawns.json", "Spawns de NPCs", w => w.SpawnPoints, list => SpawnPoints = list);
        LoadDataToList<GatherableSpawnPointListWrapper, GatherableSpawnPoint>("gatherable_spawns.json", "Spawns de Coletáveis", w => w.GatherableSpawnPoints, list => GatherableSpawnPoints = list);

        LogInfo("[DataManager] Carregamento de dados concluído.");
        LogInfo("=============================================");
    }

    /// <summary>
    /// Método genérico para carregar uma lista de dados de um JSON e convertê-la em um dicionário, com verificação de duplicados.
    /// </summary>
    /// <typeparam name="TWrapper">O tipo do objeto que envolve a lista no JSON.</typeparam>
    /// <typeparam name="TData">O tipo dos dados a serem carregados (ex: NpcData).</typeparam>
    /// <typeparam name="TKey">O tipo da chave no dicionário (geralmente string).</typeparam>
    /// <param name="fileName">Nome do arquivo JSON (ex: "npcs.json").</param>
    /// <param name="dataTypeName">Nome amigável do tipo de dado para logging (ex: "NPCs").</param>
    /// <param name="listExtractor">Função que extrai a List<TData> do objeto TWrapper.</param>
    /// <param name="keySelector">Função que extrai a chave (ID) de um objeto TData.</param>
    /// <param name="assignAction">Ação que atribui o dicionário resultante à propriedade estática correta.</param>
    /// <param name="postProcessAction">Ação opcional para executar após o carregamento bem-sucedido.</param>
    private static void LoadDataToDictionary<TWrapper, TData, TKey>(
        string fileName,
        string dataTypeName,
        Func<TWrapper, List<TData>> listExtractor,
        Func<TData, TKey> keySelector,
        Action<Dictionary<TKey, TData>> assignAction,
        Action<Dictionary<TKey, TData>> postProcessAction = null)
    {
        string filePath = Path.Combine(dataPath, fileName);
        if (!File.Exists(filePath))
        {
            LogWarning($"Arquivo de {dataTypeName} não encontrado: {fileName}");
            return;
        }

        try
        {
            string jsonContent = File.ReadAllText(filePath);
            TWrapper wrapper = JsonConvert.DeserializeObject<TWrapper>(jsonContent, jsonSettings);
            List<TData> dataList = listExtractor(wrapper);

            if (dataList == null)
            {
                LogWarning($"Nenhum dado encontrado no arquivo {fileName}. A lista está vazia ou o formato do JSON está incorreto.");
                return;
            }

            var dictionary = new Dictionary<TKey, TData>();
            foreach (var item in dataList)
            {
                TKey key = keySelector(item);
                if (key == null)
                {
                    LogWarning($"Item em '{fileName}' tem uma chave nula e será ignorado.");
                    continue;
                }

                if (!dictionary.TryAdd(key, item))
                {
                    LogError($"[DUPLICADO] ID '{key}' duplicado encontrado em '{fileName}'. O item foi ignorado.");
                }
            }

            assignAction(dictionary);
            postProcessAction?.Invoke(dictionary); // Executa o pós-processamento se existir

            LogSuccess($"Carregados {dictionary.Count} de {dataList.Count} {dataTypeName} de '{fileName}'.");
        }
        catch (Exception ex)
        {
            LogError($"[ERRO FATAL] Falha ao carregar ou processar '{fileName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Método genérico para carregar dados que são apenas uma lista.
    /// </summary>
    private static void LoadDataToList<TWrapper, TData>(
        string fileName,
        string dataTypeName,
        Func<TWrapper, List<TData>> listExtractor,
        Action<List<TData>> assignAction)
    {
        string filePath = Path.Combine(dataPath, fileName);
        if (!File.Exists(filePath))
        {
            LogWarning($"Arquivo de {dataTypeName} não encontrado: {fileName}");
            return;
        }

        try
        {
            string jsonContent = File.ReadAllText(filePath);
            TWrapper wrapper = JsonConvert.DeserializeObject<TWrapper>(jsonContent, jsonSettings);
            List<TData> dataList = listExtractor(wrapper) ?? new List<TData>();
            assignAction(dataList);
            LogSuccess($"Carregados {dataList.Count} {dataTypeName} de '{fileName}'.");
        }
        catch (Exception ex)
        {
            LogError($"[ERRO FATAL] Falha ao carregar ou processar '{fileName}': {ex.Message}");
        }
    }

    #region Métodos de Pós-Processamento

    private static void PostProcessNpcs(Dictionary<string, NpcData> npcs)
    {
        foreach (var npcData in npcs.Values)
        {
            if (npcData.AbilityIDs == null || !npcData.AbilityIDs.Any()) continue;

            float maxRange = 0f;
            foreach (var abilityId in npcData.AbilityIDs)
            {
                if (Abilities.TryGetValue(abilityId, out AbilityData ability) && ability.Range > maxRange)
                {
                    maxRange = ability.Range;
                }
            }
            npcData.MaxAbilityRange = maxRange;
        }
        LogInfo("Pós-processamento de NPCs concluído (cálculo de MaxAbilityRange).");
    }

    #endregion

    #region Logging Helpers

    private static void LogInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void LogSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCESSO] {message}");
        Console.ResetColor();
    }

    private static void LogWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[AVISO] {message}");
        Console.ResetColor();
    }

    private static void LogError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    #endregion

    #region Classes Wrapper
    // As classes wrapper permanecem as mesmas, pois definem a estrutura dos seus arquivos JSON.
    private class LootTableWrapper { public List<ServerLootTable> LootTables { get; set; } }
    private class QuestListWrapper { public List<ServerQuestData> Quests { get; set; } }
    private class StatusEffectListWrapper { public List<ServerStatusEffectData> StatusEffects { get; set; } }
    private class ClassListWrapper { public List<ServerClassData> Classes { get; set; } }
    private class NpcListWrapper { public List<NpcData> Npcs { get; set; } }
    private class SpawnPointListWrapper { public List<SpawnPoint> SpawnPoints { get; set; } }
    private class AbilityListWrapper { public List<AbilityData> Abilities { get; set; } }
    private class VendorListWrapper { public List<VendorData> Vendors { get; set; } }
    private class GatherableSpawnPointListWrapper { public List<GatherableSpawnPoint> GatherableSpawnPoints { get; set; } }
    private class GatherableListWrapper { public List<GatherableData> Gatherables { get; set; } }
    #endregion
}