// Servidor/Data/GatherableSpawnPoint.cs
using System.Numerics;

public class GatherableSpawnPoint
{
    public string GatherableTypeID { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public float SpawnRadius { get; set; }
}