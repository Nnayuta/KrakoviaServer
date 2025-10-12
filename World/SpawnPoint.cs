using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;

public class SpawnPoint
{
    // Propriedades existentes do seu spawns.json
    public required string NpcTypeId { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 InitialRotation { get; set; } = Vector3.Zero;
    public int Quantity { get; set; }
    public float SpawnRadius { get; set; }
    public NpcAiType AiType { get; set; } = NpcAiType.Wandering_Aggressive;
    public List<Vector3>? PatrolPath { get; set; }

    // --- NOVAS PROPRIEDADES DE RASTREAMENTO (NÃO PRECISAM ESTAR NO JSON) ---

    [JsonIgnore]
    public List<string> ActiveNpcInstanceIds { get; set; } = new List<string>();

    [JsonIgnore]
    public DateTime RespawnEndTime { get; set; } = DateTime.MinValue;

}