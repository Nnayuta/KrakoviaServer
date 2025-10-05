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
        Account? account = null;
        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = "SELECT id, password_hash FROM accounts WHERE username = @username LIMIT 1;";
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@username", username);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        string storedHash = Convert.ToString(reader["password_hash"]);

                        if (!string.IsNullOrEmpty(storedHash) && BCrypt.Net.BCrypt.Verify(password, storedHash))
                        {
                            account = new Account();
                        }
                    }
                }
            }
        }

        if (account != null)
        {
            return await GetAccountByUsernameAsync(username);
        }

        return null;
    }

    public async Task<Account?> GetAccountByUsernameAsync(string username)
    {
        Account? account = null;
        // Usamos um dicionário para montar os personagens, evitando duplicatas.
        var characters = new Dictionary<string, Character>();

        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            // Query principal para buscar a conta e os personagens
            var accountQuery = @"
                SELECT id as account_id, username, password_hash
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
                        Characters = new List<Character>()
                    };
                }
            }

            // Se a conta existe, buscamos os personagens e seus equipamentos
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

                        // Se é a primeira vez que vemos este personagem, criamos o objeto.
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

                        // Adiciona o item equipado, se houver um.
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

        // Adiciona a lista de personagens montada à conta
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