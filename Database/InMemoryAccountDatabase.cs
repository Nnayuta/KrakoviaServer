// Data/InMemoryAccountDatabase.cs
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using BCrypt.Net;

public class InMemoryAccountDatabase : IAccountDatabase
{
    private static readonly ConcurrentDictionary<string, Account> _accounts = new ConcurrentDictionary<string, Account>();

    public Task<bool> RegisterAsync(string username, string password)
    {
        // Gera o hash seguro da senha com um "sal" automático
        string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

        var account = new Account
        {
            Username = username,
            HashedPassword = hashedPassword
        };

        bool success = _accounts.TryAdd(username.ToLowerInvariant(), account);

        // Task.FromResult é usado para retornar um valor dentro de uma Task concluída,
        // já que a operação em memória é síncrona.
        return Task.FromResult(success);
    }

    public Task<Account?> AuthenticateAsync(string username, string password)
    {
        if (_accounts.TryGetValue(username.ToLowerInvariant(), out var account))
        {
            // Verifica a senha digitada contra o hash armazenado
            if (BCrypt.Net.BCrypt.Verify(password, account.HashedPassword))
            {
                return Task.FromResult<Account?>(account);
            }
        }
        return Task.FromResult<Account?>(null); // Login falhou
    }

    public Task<bool> AddCharacterToAccountAsync(string username, Character newCharacter)
    {
        if (_accounts.TryGetValue(username.ToLowerInvariant(), out var account))
        {
            // Validações (permanecem as mesmas)
            if (account.Characters.Count >= 5 ||
                account.Characters.Any(c => c.Name.Equals(newCharacter.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }

            account.Characters.Add(newCharacter);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<Account?> GetAccountByUsernameAsync(string username)
    {
        _accounts.TryGetValue(username.ToLowerInvariant(), out var account);
        return Task.FromResult(account);
    }
}