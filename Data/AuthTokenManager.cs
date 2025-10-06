// AuthTokenManager.cs
using System.Collections.Concurrent;
using System;
using System.Diagnostics.CodeAnalysis;

public static class AuthTokenManager
{
    private static readonly ConcurrentDictionary<string, AuthenticatedPlayerInfo> validTokens = new();

    public static string GenerateToken(Account account, Character character)
    {
        string token = Guid.NewGuid().ToString("N");

        var playerInfo = new AuthenticatedPlayerInfo
        {
            Username = account.Username,
            CharacterId = character.Id,
            CharacterName = character.Name,
            ClassID = character.ClassID,
            Level = character.Level,
            Appearance = character.Appearance,
            PermissionLevel = account.PermissionLevel
        };

        validTokens.TryAdd(token, playerInfo);

        // Adicionando a permissão ao log para facilitar a depuração no futuro
        Console.WriteLine($"[AUTH] Token gerado para {character.Name} (Conta: {account.Username}, Perm: {account.PermissionLevel}): {token}");
        return token;
    }

    public static bool IsTokenValid(string token, [MaybeNullWhen(false)] out AuthenticatedPlayerInfo playerInfo)
    {
        // A lógica de remoção garante que um token só pode ser usado uma vez.
        return validTokens.TryRemove(token, out playerInfo);
    }
}

// A sua classe AuthenticatedPlayerInfo já deve estar correta,
// com a propriedade PermissionLevel.
public class AuthenticatedPlayerInfo
{
    public string Username { get; set; }
    public string CharacterId { get; set; }
    public string CharacterName { get; set; }
    public string ClassID { get; set; }
    public int Level { get; set; }
    public CharacterAppearance Appearance { get; set; }
    public int PermissionLevel { get; set; }
}