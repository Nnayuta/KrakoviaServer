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
        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var query = @"
                SELECT
                    a.id as account_id, a.username, a.password_hash,
                    c.id as character_id, c.name, c.class_id, c.level, c.appearance_json
                FROM accounts a
                LEFT JOIN characters c ON a.id = c.account_id
                WHERE a.username = @username;";

            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@username", username);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (account == null)
                        {
                            // <<< CORREÇÃO PRINCIPAL AQUI >>>
                            // Usamos o indexador de objeto (reader["nome_coluna"]) e o Convert
                            // para evitar os erros de tipo.
                            account = new Account
                            {
                                Id = Convert.ToInt32(reader["account_id"]),
                                Username = Convert.ToString(reader["username"]),
                                HashedPassword = Convert.ToString(reader["password_hash"]),
                                Characters = new List<Character>()
                            };
                        }

                        // Verificamos se o personagem existe usando DBNull.Value
                        if (reader["character_id"] != DBNull.Value)
                        {
                            var character = new Character
                            {
                                Id = Convert.ToString(reader["character_id"]),
                                Name = Convert.ToString(reader["name"]),
                                ClassID = Convert.ToString(reader["class_id"]),
                                Level = Convert.ToInt32(reader["level"]),
                                Appearance = JsonConvert.DeserializeObject<CharacterAppearance>(Convert.ToString(reader["appearance_json"]))
                            };
                            account.Characters.Add(character);
                        }
                    }
                }
            }
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