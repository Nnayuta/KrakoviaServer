// Data/MariaDBAccountDatabase.cs
using System;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using BCrypt.Net;
using System.Collections.Generic;
using Newtonsoft.Json;

public class MariaDBAccountDatabase : IAccountDatabase
{
    private readonly string _connectionString;

    public MariaDBAccountDatabase(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<bool> RegisterAsync(string username, string password)
    {
        // Esta implementação já estava correta e segura, não precisa de mudanças.
        string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);
        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = "INSERT INTO accounts (username, password_hash) VALUES (@username, @password_hash);";
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@username", username);
                command.Parameters.AddWithValue("@password_hash", hashedPassword);
                try
                {
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
                catch (MySqlException ex)
                {
                    if (ex.Number == 1062) return false;
                    Console.WriteLine($"[DB-ERROR] Erro inesperado ao registrar: {ex.Message}");
                    throw;
                }
            }
        }
    }

    public async Task<Account?> AuthenticateAsync(string username, string password)
    {
        // 1. Busca a conta COMPLETA primeiro
        Account? account = await GetAccountByUsernameAsync(username);

        // 2. Se a conta existe, verifica a senha
        if (account != null)
        {
            // Usa o HashedPassword que já foi carregado pelo GetAccountByUsernameAsync
            if (BCrypt.Net.BCrypt.Verify(password, account.HashedPassword))
            {
                // Senha correta! Retorna a conta completa que já carregamos.
                return account;
            }
        }

        // Se a conta não existe ou a senha está incorreta, retorna null.
        return null;
    }

    public async Task<Account?> GetAccountByUsernameAsync(string username)
    {
        Account? account = null;
        var characters = new Dictionary<string, Character>();

        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            // <<< A CORREÇÃO ESTÁ AQUI >>>
            var accountQuery = @"
            SELECT id as account_id, username, password_hash, permission_level
            FROM accounts
            WHERE username = @username;";

            using (var command = new MySqlCommand(accountQuery, connection))
            {
                command.Parameters.AddWithValue("@username", username);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                    {
                        return null; // A conta não existe
                    }

                    account = new Account
                    {
                        Id = Convert.ToInt32(reader["account_id"]),
                        Username = Convert.ToString(reader["username"]),
                        HashedPassword = Convert.ToString(reader["password_hash"]),
                        PermissionLevel = Convert.ToInt32(reader["permission_level"]),
                        Characters = new List<Character>()
                    };
                }
            }

            // O resto do método permanece o mesmo
            if (account == null) return null; // Adiciona uma verificação de segurança

            var charactersQuery = @"
            SELECT
                c.id as character_id, c.name, c.class_id, c.level, c.appearance_json,
                ce.slot_name, ce.item_id
            FROM characters c
            LEFT JOIN character_equipment ce ON c.id = ce.character_id
            WHERE c.account_id = @account_id;";

            using (var command = new MySqlCommand(charactersQuery, connection))
            {
                command.Parameters.AddWithValue("@account_id", account.Id);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string charId = Convert.ToString(reader["character_id"]);

                        if (!characters.ContainsKey(charId))
                        {
                            characters[charId] = new Character
                            {
                                Id = charId,
                                Name = Convert.ToString(reader["name"]),
                                ClassID = Convert.ToString(reader["class_id"]),
                                Level = Convert.ToInt32(reader["level"]),
                                Appearance = JsonConvert.DeserializeObject<CharacterAppearance>(Convert.ToString(reader["appearance_json"]))
                            };
                        }

                        if (reader["item_id"] != DBNull.Value)
                        {
                            string slotName = Convert.ToString(reader["slot_name"]);
                            if (Enum.TryParse<EquipmentSlot>(slotName, out var slot))
                            {
                                characters[charId].EquippedItems[slot] = Convert.ToString(reader["item_id"]);
                            }
                        }
                    }
                }
            }
        }

        if (account != null)
        {
            account.Characters = characters.Values.ToList();
        }

        return account;
    }

    public async Task<bool> AddCharacterToAccountAsync(string username, Character newCharacter)
    {
        // Esta implementação já estava correta, mas vamos garantir a consistência.
        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var getAccountIdQuery = "SELECT id FROM accounts WHERE username = @username LIMIT 1;";
            int accountId = 0;
            using (var cmd = new MySqlCommand(getAccountIdQuery, connection))
            {
                cmd.Parameters.AddWithValue("@username", username);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    accountId = Convert.ToInt32(result);
                }
            }

            if (accountId == 0)
            {
                Console.WriteLine($"[DB-ERROR] Tentativa de adicionar personagem a uma conta inexistente: {username}");
                return false;
            }

            var insertQuery = @"
                INSERT INTO characters (id, account_id, name, class_id, level, appearance_json)
                VALUES (@id, @account_id, @name, @class_id, @level, @appearance_json);";

            using (var command = new MySqlCommand(insertQuery, connection))
            {
                command.Parameters.AddWithValue("@id", newCharacter.Id);
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Parameters.AddWithValue("@name", newCharacter.Name);
                command.Parameters.AddWithValue("@class_id", newCharacter.ClassID);
                command.Parameters.AddWithValue("@level", newCharacter.Level);
                command.Parameters.AddWithValue("@appearance_json", JsonConvert.SerializeObject(newCharacter.Appearance));

                try
                {
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
                catch (MySqlException ex)
                {
                    if (ex.Number == 1062) return false;
                    Console.WriteLine($"[DB-ERROR] Erro inesperado ao adicionar personagem: {ex.Message}");
                    throw;
                }
            }
        }
    }
}