using System.Collections.Concurrent;

/// <summary>
/// Gerencia o estado online dos personagens em todo o servidor.
/// É uma classe estática para ser facilmente acessível tanto pelo TCP quanto pelo UDP.
/// </summary>
public static class OnlineStatusManager
{
    // Usamos um ConcurrentDictionary para garantir a segurança entre threads (TCP e UDP podem acessá-lo).
    // O dicionário armazena <CharacterId, bool>. O valor booleano é trivial (sempre true),
    // mas o ConcurrentDictionary é otimizado para essas operações.
    private static readonly ConcurrentDictionary<string, bool> _onlineCharacters = new();

    /// <summary>
    /// Marca um personagem como online.
    /// </summary>
    /// <param name="characterId">O ID do personagem que está entrando no mundo.</param>
    public static void SetOnline(string characterId)
    {
        _onlineCharacters.TryAdd(characterId, true);
        Console.WriteLine($"[OnlineStatus] Personagem {characterId} marcado como ONLINE.");
    }

    /// <summary>
    /// Marca um personagem como offline.
    /// </summary>
    /// <param name="characterId">O ID do personagem que está saindo do mundo.</param>
    public static void SetOffline(string characterId)
    {
        _onlineCharacters.TryRemove(characterId, out _);
        Console.WriteLine($"[OnlineStatus] Personagem {characterId} marcado como OFFLINE.");
    }

    /// <summary>
    /// Verifica se um personagem está atualmente marcado como online.
    /// </summary>
    /// <param name="characterId">O ID do personagem a ser verificado.</param>
    /// <returns>True se o personagem estiver online, False caso contrário.</returns>
    public static bool IsOnline(string characterId)
    {
        return _onlineCharacters.ContainsKey(characterId);
    }
}