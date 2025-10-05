using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

public class InMemoryCharacterDatabase : ICharacterDatabase
{
    // O dicionário agora é um campo de instância, não mais estático.
    private readonly ConcurrentDictionary<string, CharacterData> _characters = new ConcurrentDictionary<string, CharacterData>();

    public Task<CharacterData> LoadOrCreateAsync(AuthenticatedPlayerInfo authInfo)
    {
        if (_characters.TryGetValue(authInfo.CharacterId, out var data))
        {
            return Task.FromResult(data);
        }

        Console.WriteLine($"[InMemoryCharacterDB] Primeira vez... Criando dados para {authInfo.CharacterId}...");

        var newCharData = new CharacterData(authInfo.CharacterId, authInfo.ClassID, authInfo.Level, authInfo.Appearance);

        // A posição inicial já é definida no construtor de CharacterData.

        _characters.TryAdd(authInfo.CharacterId, newCharData);

        // A lógica para itens iniciais é movida para cá, do antigo método estático.
        if (authInfo.Level == 1)
        {
            var initialState = CharacterStateGenerator.GenerateInitialState(authInfo.ClassID, authInfo.Level);
            newCharData.PlayerEquipment = initialState.Item2;
            newCharData.PlayerInventory = initialState.Item1;
            newCharData.PlayerActionBar = initialState.Item3;
        }

        return Task.FromResult(newCharData);
    }

    public Task SaveAsync(CharacterData dataToSave)
    {
        // A lógica de salvar é a mesma de antes.
        _characters[dataToSave.CharacterId] = dataToSave;
        Console.WriteLine($"[InMemoryCharacterDB] Dados do personagem '{dataToSave.CharacterId}' salvos na memória.");
        // Como a operação é síncrona, retornamos uma Task já completada.
        return Task.CompletedTask;
    }
}