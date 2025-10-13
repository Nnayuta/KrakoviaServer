// Servidor/Data/DataManager.cs

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization; // Necessário para CustomSerializationBinder
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

public static class DataManager
{
    // Propriedades estáticas
    public static Dictionary<string, NpcData> Npcs { get; private set; } = new();
    public static Dictionary<string, AbilityData> Abilities { get; private set; } = new();
    public static List<SpawnPoint> SpawnPoints { get; private set; } = new();
    public static Dictionary<string, ServerClassData> Classes { get; private set; } = new();
    public static Dictionary<string, ServerItemData> Items { get; private set; } = new();
    public static Dictionary<string, VendorData> Vendors { get; private set; } = new();
    public static Dictionary<string, LootTable> LootTables { get; private set; } = new();
    public static Dictionary<string, ServerQuestData> Quests { get; private set; } = new();
    public static Dictionary<string, ServerStatusEffectData> StatusEffects { get; private set; } = new();
    private class LootTableWrapper { public List<LootTable> LootTables { get; set; } }
    private class QuestListWrapper { public List<ServerQuestData> Quests { get; set; } }
    public static Dictionary<string, GatherableData> Gatherables { get; private set; } = new();
    public static List<GatherableSpawnPoint> GatherableSpawnPoints { get; private set; } = new();

    public static void LoadAllData()
    {
        Console.WriteLine("[DataManager] Iniciando carregamento de dados do jogo...");

        LoadAbilities();
        LoadItems();
        LoadStatusEffects();
        LoadNpcs();
        LoadGatherables();
        LoadClasses();
        LoadSpawnPoints();
        // (NOVO) Carrega os spawns dos coletáveis
        LoadGatherableSpawnPoints();
        LoadLootTables();
        LoadVendors();
        LoadQuests();

        Console.WriteLine("[DataManager] Carregamento de dados concluído.");
    }

    private static void LoadGatherableSpawnPoints()
    {
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData", "gatherable_spawns.json");
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[AVISO] Arquivo de Spawns de Coletáveis não encontrado: {filePath}");
                return;
            }
            string jsonContent = File.ReadAllText(filePath);

            var wrapper = JsonConvert.DeserializeObject<GatherableSpawnPointListWrapper>(jsonContent);
            if (wrapper?.GatherableSpawnPoints != null)
            {
                GatherableSpawnPoints = wrapper.GatherableSpawnPoints;
                Console.WriteLine($"[DataManager] {GatherableSpawnPoints.Count} pontos de spawn de coletáveis carregados.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO FATAL] Falha ao carregar gatherable_spawns.json: {ex.Message}");
        }
    }

    private static void LoadStatusEffects()
    {
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData", "status_effects.json");
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[AVISO] Arquivo de Status Effects não encontrado: {filePath}");
                return;
            }
            string jsonContent = File.ReadAllText(filePath);

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new CustomSerializationBinder()
            };

            var wrapper = JsonConvert.DeserializeObject<StatusEffectListWrapper>(jsonContent, settings);
            if (wrapper?.StatusEffects != null)
            {
                StatusEffects = wrapper.StatusEffects.ToDictionary(e => e.EffectID, e => e);
                Console.WriteLine($"[DataManager] {StatusEffects.Count} status effects carregados.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO FATAL] Falha ao carregar status_effects.json: {ex.Message}");
        }
    }

    private static void LoadQuests()
    {
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData", "quests.json");
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[AVISO] Arquivo de quests não encontrado: {filePath}");
                return;
            }
            string jsonContent = File.ReadAllText(filePath);


            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new CustomSerializationBinder()
            };

            var wrapper = JsonConvert.DeserializeObject<QuestListWrapper>(jsonContent, settings);
            if (wrapper?.Quests != null)
            {
                Quests = wrapper.Quests.ToDictionary(q => q.QuestID, q => q);
                Console.WriteLine($"[DataManager] {Quests.Count} quests carregadas.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO FATAL] Falha ao carregar quests.json: {ex.Message}");
        }
    }

    private static void LoadLootTables()
    {
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData", "loottables.json");
        try
        {
            if (!File.Exists(filePath)) { Console.WriteLine($"[AVISO] Arquivo de Loot Tables não encontrado: {filePath}"); return; }
            string jsonContent = File.ReadAllText(filePath);


            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new CustomSerializationBinder()
            };

            var wrapper = JsonConvert.DeserializeObject<LootTableWrapper>(jsonContent, settings);
            if (wrapper?.LootTables != null)
            {
                LootTables = wrapper.LootTables.ToDictionary(lt => lt.LootTableID, lt => lt);
                Console.WriteLine($"[DataManager] {LootTables.Count} tabelas de loot carregadas.");
            }
        }
        catch (Exception ex) { Console.WriteLine($"[ERRO FATAL] Falha ao carregar loottables.json: {ex.Message}"); }
    }

    private static void LoadItems()
    {
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData", "items.json");
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[AVISO] Arquivo de itens não encontrado: {filePath}");
                return;
            }
            string jsonContent = File.ReadAllText(filePath);

            JsonSerializerSettings itemSettings = new JsonSerializerSettings
            {
                // Esta configuração é específica para itens
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new CustomSerializationBinder()
            };


            var itemList = JsonConvert.DeserializeObject<List<ServerItemData>>(jsonContent, itemSettings);
            if (itemList != null)
            {
                Items = itemList.ToDictionary(item => item.itemID, item => item);
                Console.WriteLine($"[DataManager] {Items.Count} itens carregados.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO FATAL] Falha ao carregar items.json: {ex.Message}");
        }
    }

    private static void LoadClasses()
    {
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData", "classes.json");
        try
        {
            if (!File.Exists(filePath)) { Console.WriteLine($"[AVISO] Arquivo de classes não encontrado: {filePath}"); return; }
            string jsonContent = File.ReadAllText(filePath);


            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new CustomSerializationBinder()
            };

            var wrapper = JsonConvert.DeserializeObject<ClassListWrapper>(jsonContent, settings);
            if (wrapper?.Classes != null)
            {
                Classes = wrapper.Classes.ToDictionary(c => c.ClassID, c => c);
                Console.WriteLine($"[DataManager] {Classes.Count} classes de jogador carregadas.");
            }
        }
        catch (Exception ex) { Console.WriteLine($"[ERRO FATAL] Falha ao carregar classes.json: {ex.Message}"); }
    }

    private static void LoadNpcs()
    {
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData", "npcs.json");
        try
        {
            if (!File.Exists(filePath)) { Console.WriteLine($"[ERRO] Arquivo de NPCs não encontrado: {filePath}"); return; }
            string jsonContent = File.ReadAllText(filePath);


            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new CustomSerializationBinder()
            };

            var wrapper = JsonConvert.DeserializeObject<NpcListWrapper>(jsonContent, settings);
            if (wrapper?.Npcs != null)
            {
                Npcs = wrapper.Npcs.ToDictionary(n => n.TypeId, n => n);

                foreach (var npcData in Npcs.Values)
                {
                    if (npcData.AbilityIDs != null && npcData.AbilityIDs.Any())
                    {
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
                }

                Console.WriteLine($"[DataManager] {Npcs.Count} tipos de NPC carregados e processados.");
            }
        }
        catch (Exception ex) { Console.WriteLine($"[ERRO FATAL] Falha ao carregar npcs.json: {ex.Message}"); }
    }

    private static void LoadSpawnPoints()
    {
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData", "spawns.json");
        try
        {
            if (!File.Exists(filePath)) { Console.WriteLine($"[ERRO] Arquivo de Spawns não encontrado: {filePath}"); return; }
            string jsonContent = File.ReadAllText(filePath);


            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new CustomSerializationBinder()
            };

            var wrapper = JsonConvert.DeserializeObject<SpawnPointListWrapper>(jsonContent, settings);
            if (wrapper?.SpawnPoints != null)
            {
                SpawnPoints = wrapper.SpawnPoints;
                Console.WriteLine($"[DataManager] {SpawnPoints.Count} pontos de spawn carregados.");
            }
        }
        catch (Exception ex) { Console.WriteLine($"[ERRO FATAL] Falha ao carregar spawns.json: {ex.Message}"); }
    }

    private static void LoadAbilities()
    {
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData", "abilities.json");
        try
        {
            if (!File.Exists(filePath)) { Console.WriteLine($"[AVISO] Arquivo de habilidades não encontrado: {filePath}"); return; }
            string jsonContent = File.ReadAllText(filePath);

            var abilitySettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new CustomSerializationBinder()
            };

            var wrapper = JsonConvert.DeserializeObject<AbilityListWrapper>(jsonContent, abilitySettings);

            if (wrapper?.Abilities != null)
            {
                Abilities.Clear();
                foreach (var ability in wrapper.Abilities)
                {
                    if (!string.IsNullOrEmpty(ability.ID) && !Abilities.ContainsKey(ability.ID))
                    {
                        Abilities.Add(ability.ID, ability);
                    }
                    else
                    {
                        Console.WriteLine($"[AVISO DataManager] ID de habilidade duplicado ou inválido: '{ability.ID}'. Ignorado.");
                    }
                }
                Console.WriteLine($"[DataManager] {Abilities.Count} habilidades carregadas.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO FATAL] Falha ao carregar abilities.json: {ex.Message}");
        }
    }

    private static void LoadVendors()
    {
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData", "vendors.json");
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[AVISO] Arquivo de vendedores não encontrado: {filePath}");
                return;
            }
            string jsonContent = File.ReadAllText(filePath);


            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new CustomSerializationBinder()
            };

            var wrapper = JsonConvert.DeserializeObject<VendorListWrapper>(jsonContent, settings);
            if (wrapper?.Vendors != null)
            {
                Vendors = wrapper.Vendors.ToDictionary(v => v.NpcTypeId, v => v);
                Console.WriteLine($"[DataManager] {Vendors.Count} vendedores carregados.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO FATAL] Falha ao carregar vendors.json: {ex.Message}");
        }
    }

    private static void LoadGatherables()
    {
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerData", "gatherables.json");
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[AVISO] Arquivo de Itens Coletáveis não encontrado: {filePath}");
                return;
            }
            string jsonContent = File.ReadAllText(filePath);

            var wrapper = JsonConvert.DeserializeObject<GatherableListWrapper>(jsonContent);
            if (wrapper?.Gatherables != null)
            {
                Gatherables = wrapper.Gatherables.ToDictionary(g => g.ID, g => g);
                Console.WriteLine($"[DataManager] {Gatherables.Count} itens coletáveis carregados.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO FATAL] Falha ao carregar gatherables.json: {ex.Message}");
        }
    }


    // =================================================================================
    // Classes Wrapper para a desserialização correta dos arquivos JSON
    // =================================================================================
    private class StatusEffectListWrapper { public List<ServerStatusEffectData> StatusEffects { get; set; } }
    private class ClassListWrapper { public List<ServerClassData> Classes { get; set; } }
    private class NpcListWrapper { public List<NpcData> Npcs { get; set; } }
    private class SpawnPointListWrapper { public List<SpawnPoint> SpawnPoints { get; set; } }
    private class AbilityListWrapper { public List<AbilityData> Abilities { get; set; } }
    private class VendorListWrapper { public List<VendorData> Vendors { get; set; } }
    private class ItemListWrapper { public List<ServerItemData> Items { get; set; } }
    private class GatherableSpawnPointListWrapper { public List<GatherableSpawnPoint> GatherableSpawnPoints { get; set; } }
    private class GatherableListWrapper { public List<GatherableData> Gatherables { get; set; } }
}