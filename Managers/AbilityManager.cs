// // --- SERVIDOR (AbilityManager.cs) ---

// using System;
// using System.Collections.Generic;
// using System.IO;
// using System.Text.Json;

// public class AbilityManager
// {
//     // Mantém private set (boa prática de encapsulamento)
//     // já que o GameDataLoader só precisa ler.
//     public Dictionary<string, AbilityData> AllAbilities { get; private set; }

//     public AbilityManager()
//     {
//         AllAbilities = new Dictionary<string, AbilityData>();
//     }

//     public void LoadAbilities(string filePath)
//     {
//         //Console.WriteLine($"Tentando carregar habilidades de: {filePath}");

//         if (!File.Exists(filePath))
//         {
//             Console.WriteLine($"ERRO: Arquivo de habilidades não encontrado em '{filePath}'");
//             return;
//         }

//         string json = File.ReadAllText(filePath);

//         var abilityListWrapper = JsonSerializer.Deserialize<AbilityListWrapper>(json);

//         if (abilityListWrapper?.Abilities == null)
//         {
//             Console.WriteLine("ERRO: JSON de habilidades inválido ou vazio.");
//             return;
//         }

//         foreach (var abilityData in abilityListWrapper.Abilities)
//         {
//             AllAbilities[abilityData.ID] = abilityData;
//         }

//         //Console.WriteLine($"{AllAbilities.Count} habilidades carregadas com sucesso!");
//     }

//     public AbilityData? GetAbility(string id)
//     {
//         AllAbilities.TryGetValue(id, out var ability);
//         return ability;
//     }
// }

// public class AbilityListWrapper
// {
//     public List<AbilityData> Abilities { get; set; } = new List<AbilityData>();
// }
