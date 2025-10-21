// NOVO ARQUIVO: Server/Managers/ItemInstanceManager.cs

using System.Collections.Concurrent;
using System.Collections.Generic;

/// <summary>
/// Rastreia os stats gerados para instâncias de itens únicos.
/// O DataManager guarda os templates, e este manager guarda os stats rolados.
/// </summary>
public class ItemInstanceManager
{
    private readonly UDPServer _server;

    // Dicionário: <InstanceID do Item (GUID), Lista de Stats Gerados>
    private readonly ConcurrentDictionary<string, List<BaseStatData>> _generatedItemStats = new();
    private readonly ConcurrentDictionary<string, ItemInstanceData> _generatedItems = new();

    public ItemInstanceManager(UDPServer server) { _server = server; }

    public void RegisterGeneratedItem(string instanceId, ItemInstanceData data)
    {
        _generatedItems.TryAdd(instanceId, data);
    }

    public ItemInstanceData GetDataForInstance(string instanceId)
    {
        _generatedItems.TryGetValue(instanceId, out var data);
        return data;
    }

    public void UnregisterItem(string instanceId) { _generatedItems.TryRemove(instanceId, out _); }

    public List<BaseStatData> GetStatsForInstance(string instanceId)
    {
        _generatedItemStats.TryGetValue(instanceId, out var stats);
        return stats ?? new List<BaseStatData>(); // Retorna lista vazia se não encontrado
    }

    // Métodos para persistência (salvar/carregar)
    // public ConcurrentDictionary<string, List<BaseStatData>> GetAllDataForSaving()
    // {
    //     return _generatedItemStats;
    // }

    public ConcurrentDictionary<string, ItemInstanceData> GetAllDataForSaving() => _generatedItems;

    public void LoadData(ConcurrentDictionary<string, ItemInstanceData> data)
    {
        if (data == null) return;
        foreach (var pair in data) { _generatedItems.TryAdd(pair.Key, pair.Value); }
    }
}