// Interfaces/IWorldEntity.cs
using System.Numerics;

public interface IWorldEntity
{
    /// <summary>
    /// O ID único global da entidade no mundo.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// A posição atual da entidade no mundo.
    /// </summary>
    Vector3 Position { get; }

    /// <summary>
    /// Gera a string de mensagem de spawn para esta entidade.
    /// Ex: "SPAWN_PLAYER|id|pos" ou "SPAWN_NPC|id|type|...".
    /// </summary>
    string GetSpawnMessage();
}