// Substitua o arquivo AuthTokenManager.cs inteiro por este.
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
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
            Appearance = character.Appearance
        };

        validTokens.TryAdd(token, playerInfo);

        Console.WriteLine($"[AUTH] Token gerado para o personagem {character.Name} (Classe: {character.ClassID}, Nível: {character.Level}): {token}");
        return token;
    }

    public static bool IsTokenValid(string token, [MaybeNullWhen(false)] out AuthenticatedPlayerInfo playerInfo)
    {
        return validTokens.TryRemove(token, out playerInfo);
    }
}

// A classe AuthenticatedPlayerInfo já deve estar assim, mas confirme.
// Ela precisa da propriedade Appearance.
public class AuthenticatedPlayerInfo
{
    public string Username { get; set; } = string.Empty;
    public string CharacterId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string ClassID { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public CharacterAppearance Appearance { get; set; }
}