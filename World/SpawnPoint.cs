// World/SpawnPoint.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;

public class SpawnPoint
{
    public string NpcTypeId { get; set; }
    public Vector3 Position { get; set; }
    public int Quantity { get; set; } = 1;
    public float SpawnRadius { get; set; } = 0f;
    public List<Vector3>? PatrolPath { get; set; }

    [JsonIgnore]
    public List<string> ActiveNpcInstanceIds { get; set; } = new List<string>();

    [JsonIgnore]
    public DateTime RespawnEndTime { get; set; }
}