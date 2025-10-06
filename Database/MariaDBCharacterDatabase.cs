// Data/MariaDBCharacterDatabase.cs
using System;
using System.Text; // Para o StringBuilder
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System.Linq; // Para o .Any()

public class MariaDBCharacterDatabase : ICharacterDatabase
{
    private readonly string _connectionString;

    public MariaDBCharacterDatabase(string connectionString)
    {
        _connectionString = connectionString;
    }

    // =====================================================================
    // MÉTODO DE SALVAR
    // =====================================================================
    public async Task SaveAsync(CharacterData dataToSave)
    {
        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            // Usar uma transação é CRUCIAL para garantir a integridade dos dados.
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                try
                {
                    // 1. Atualiza os dados simples na tabela principal 'characters'
                    var updateCharacterCmd = new MySqlCommand(
                        @"UPDATE characters SET
                            level = @level,
                            experience = @experience,
                            bronze = @bronze,
                            position = @position
                          WHERE id = @id;",
                        connection, transaction);

                    updateCharacterCmd.Parameters.AddWithValue("@level", dataToSave.Level);
                    updateCharacterCmd.Parameters.AddWithValue("@experience", dataToSave.CurrentExperience);
                    updateCharacterCmd.Parameters.AddWithValue("@bronze", dataToSave.TotalBronze);
                    updateCharacterCmd.Parameters.AddWithValue("@position", dataToSave.Position);
                    updateCharacterCmd.Parameters.AddWithValue("@id", dataToSave.CharacterId);
                    await updateCharacterCmd.ExecuteNonQueryAsync();

                    // 2. Limpa os dados antigos de inventário, equipamento e barra de ações
                    var deleteCmdText = @"
                        DELETE FROM character_inventory WHERE character_id = @id;
                        DELETE FROM character_equipment WHERE character_id = @id;
                        DELETE FROM character_actionbar WHERE character_id = @id;";
                    var deleteCmd = new MySqlCommand(deleteCmdText, connection, transaction);
                    deleteCmd.Parameters.AddWithValue("@id", dataToSave.CharacterId);
                    await deleteCmd.ExecuteNonQueryAsync();

                    // 3. Insere os novos dados (usando bulk insert para eficiência)
                    await BulkInsertInventoryAsync(dataToSave, connection, transaction);
                    await BulkInsertEquipmentAsync(dataToSave, connection, transaction);
                    await BulkInsertActionBarAsync(dataToSave, connection, transaction);

                    // 4. Se tudo deu certo, confirma a transação
                    await transaction.CommitAsync();
                    Console.WriteLine($"[DB-SAVE] Dados para o personagem {dataToSave.CharacterId} salvos com sucesso.");
                }
                catch (Exception ex)
                {
                    // 5. Se algo deu errado, desfaz tudo (rollback)
                    await transaction.RollbackAsync();
                    Console.WriteLine($"[DB-ERROR] Falha ao salvar dados do personagem {dataToSave.CharacterId}. Rollback executado. Erro: {ex.Message}");
                    throw; // Lança a exceção para cima para que o servidor saiba que algo deu errado.
                }
            }
        }
    }

    public async Task<CharacterData> LoadOrCreateAsync(AuthenticatedPlayerInfo authInfo)
    {
        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            // 1. Tenta carregar os dados principais do personagem
            CharacterData? characterData = await LoadCharacterBaseDataAsync(authInfo, connection);

            // Cenário 1: Personagem nem existe na tabela principal (não deveria acontecer no seu fluxo atual, mas ok)
            if (characterData == null)
            {
                characterData = await CreateNewCharacterAsync(authInfo, connection); // Cria tudo e salva
            }
            else
            {
                // Cenário 2: Personagem existe. Vamos carregar seus itens.
                await LoadInventoryAsync(characterData, connection);
                await LoadEquipmentAsync(characterData, connection);
                await LoadActionBarAsync(characterData, connection);

                // <<< A CORREÇÃO >>>
                // Verifica se é um personagem "recém-criado" (Nível 1 e sem equipamentos).
                bool hasNoItems = !characterData.PlayerEquipment.equippedItems.Any(kvp => kvp.Value != null);
                if (characterData.Level == 1 && hasNoItems)
                {
                    Console.WriteLine($"[DB-INIT] Detectado personagem Nível 1 sem itens ({characterData.CharacterId}). Populando itens iniciais...");

                    // 1. Popula o objeto EM MEMÓRIA
                    PopulateStartingItems(characterData);

                    // 2. Salva os novos itens NO BANCO DE DADOS imediatamente.
                    //    Usamos uma transação para garantir a consistência.
                    await using (var transaction = await connection.BeginTransactionAsync())
                    {
                        // Não precisamos atualizar a tabela 'characters', só as de itens.
                        await BulkInsertInventoryAsync(characterData, connection, transaction);
                        await BulkInsertEquipmentAsync(characterData, connection, transaction);
                        // Barra de ações também, por garantia.
                        await BulkInsertActionBarAsync(characterData, connection, transaction);

                        await transaction.CommitAsync();
                        Console.WriteLine($"[DB-INIT] Itens iniciais salvos no banco para {characterData.CharacterId}.");
                    }
                }
            }

            return characterData;
        }
    }


    private async Task<CharacterData?> LoadCharacterBaseDataAsync(AuthenticatedPlayerInfo authInfo, MySqlConnection connection)
    {
        var selectCmd = new MySqlCommand("SELECT * FROM characters WHERE id = @id", connection);
        selectCmd.Parameters.AddWithValue("@id", authInfo.CharacterId);

        using (var reader = await selectCmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                // Personagem EXISTE, carrega os dados
                return new CharacterData(
                    authInfo.CharacterId,
                    Convert.ToString(reader["class_id"]),
                    Convert.ToInt32(reader["level"]),
                    JsonConvert.DeserializeObject<CharacterAppearance>(Convert.ToString(reader["appearance_json"]))
                )
                {
                    CurrentExperience = Convert.ToInt64(reader["experience"]),
                    TotalBronze = Convert.ToInt64(reader["bronze"]),
                    Position = Convert.ToString(reader["position"])
                };
            }
        }
        return null;
    }

    private async Task<CharacterData> CreateNewCharacterAsync(AuthenticatedPlayerInfo authInfo, MySqlConnection connection)
    {
        // 1. Cria o objeto de dados em memória
        var newCharData = new CharacterData(authInfo.CharacterId, authInfo.ClassID, authInfo.Level, authInfo.Appearance);

        // 2. <<< A LÓGICA QUE FALTAVA >>> Popula o objeto com os itens iniciais
        PopulateStartingItems(newCharData);

        // 3. Salva TUDO no banco de dados dentro de uma transação segura
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            try
            {
                // Insere o registro principal na tabela 'characters'
                var insertCmd = new MySqlCommand(
                    "INSERT INTO characters (id, account_id, name, class_id, level, appearance_json, experience, bronze, position) " +
                    "SELECT @id, a.id, @name, @class_id, @level, @appearance, @exp, @bronze, @pos " +
                    "FROM accounts a WHERE a.username = @username;",
                    connection, transaction);

                insertCmd.Parameters.AddWithValue("@id", authInfo.CharacterId);
                insertCmd.Parameters.AddWithValue("@name", authInfo.CharacterName); // Supondo que você adicionou CharacterName ao AuthenticatedPlayerInfo
                insertCmd.Parameters.AddWithValue("@class_id", authInfo.ClassID);
                insertCmd.Parameters.AddWithValue("@level", authInfo.Level);
                insertCmd.Parameters.AddWithValue("@appearance", JsonConvert.SerializeObject(authInfo.Appearance));
                insertCmd.Parameters.AddWithValue("@exp", newCharData.CurrentExperience);
                insertCmd.Parameters.AddWithValue("@bronze", newCharData.TotalBronze);
                insertCmd.Parameters.AddWithValue("@pos", newCharData.Position);
                insertCmd.Parameters.AddWithValue("@username", authInfo.Username);
                await insertCmd.ExecuteNonQueryAsync();

                // Insere os itens de inventário e equipamento
                await BulkInsertInventoryAsync(newCharData, connection, transaction);
                await BulkInsertEquipmentAsync(newCharData, connection, transaction);
                // A barra de ações inicial geralmente está vazia, mas podemos chamar por consistência
                await BulkInsertActionBarAsync(newCharData, connection, transaction);

                await transaction.CommitAsync();
                Console.WriteLine($"[DB-CREATE] Novo personagem {authInfo.CharacterId} com itens iniciais criado no banco de dados.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[DB-ERROR] Falha ao criar novo personagem {authInfo.CharacterId}. Rollback executado. Erro: {ex.Message}");
                throw;
            }
        }
        return newCharData;
    }

    // <<< NOVO MÉTODO AUXILIAR PARA REUTILIZAR A LÓGICA DE ITENS INICIAIS >>>
    private static void PopulateStartingItems(CharacterData characterData)
    {
        if (!DataManager.Classes.TryGetValue(characterData.ClassID, out var classData)) return;

        foreach (string itemID in classData.StartingEquipmentIDs)
        {
            if (DataManager.Items.TryGetValue(itemID, out var itemData) && itemData is ServerEquipmentData eqData)
            {
                characterData.PlayerEquipment.equippedItems[eqData.equipmentSlot] = new ItemStack(itemID, 1);
            }
        }

        // Adiciona itens de inventário
        foreach (string itemID in classData.StartingInventoryIDs)
        {
            // Usa o método AddItem do inventário do CharacterData
            characterData.PlayerInventory.AddItem(itemID);
        }
    }

    #region Métodos Auxiliares de Bulk Insert

    private async Task BulkInsertInventoryAsync(CharacterData data, MySqlConnection conn, MySqlTransaction tr)
    {
        if (!data.PlayerInventory.slots.Any(s => s != null)) return;

        var sb = new StringBuilder("INSERT INTO character_inventory (character_id, slot_index, item_id, quantity, instance_id) VALUES ");
        var parameters = new List<MySqlParameter>();
        int paramIndex = 0;

        for (int i = 0; i < data.PlayerInventory.slots.Count; i++)
        {
            var item = data.PlayerInventory.slots[i];
            if (item != null)
            {
                sb.Append($"(@charId{paramIndex}, @slot{paramIndex}, @itemId{paramIndex}, @qty{paramIndex}, @instId{paramIndex}),");
                parameters.Add(new MySqlParameter($"@charId{paramIndex}", data.CharacterId));
                parameters.Add(new MySqlParameter($"@slot{paramIndex}", i));
                parameters.Add(new MySqlParameter($"@itemId{paramIndex}", item.ItemID));
                parameters.Add(new MySqlParameter($"@qty{paramIndex}", item.Quantity));
                parameters.Add(new MySqlParameter($"@instId{paramIndex}", item.InstanceID));
                paramIndex++;
            }
        }

        sb.Length--; // Remove a última vírgula
        var cmd = new MySqlCommand(sb.ToString(), conn, tr);
        cmd.Parameters.AddRange(parameters.ToArray());
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task BulkInsertEquipmentAsync(CharacterData data, MySqlConnection conn, MySqlTransaction tr)
    {
        if (!data.PlayerEquipment.equippedItems.Any(kvp => kvp.Value != null)) return;

        var sb = new StringBuilder("INSERT INTO character_equipment (character_id, slot_name, item_id, instance_id) VALUES ");
        var parameters = new List<MySqlParameter>();
        int paramIndex = 0;

        foreach (var pair in data.PlayerEquipment.equippedItems)
        {
            if (pair.Value != null)
            {
                sb.Append($"(@charId{paramIndex}, @slotName{paramIndex}, @itemId{paramIndex}, @instId{paramIndex}),");
                parameters.Add(new MySqlParameter($"@charId{paramIndex}", data.CharacterId));
                parameters.Add(new MySqlParameter($"@slotName{paramIndex}", pair.Key.ToString()));
                parameters.Add(new MySqlParameter($"@itemId{paramIndex}", pair.Value.ItemID));
                parameters.Add(new MySqlParameter($"@instId{paramIndex}", pair.Value.InstanceID));
                paramIndex++;
            }
        }

        sb.Length--; // Remove a última vírgula
        var cmd = new MySqlCommand(sb.ToString(), conn, tr);
        cmd.Parameters.AddRange(parameters.ToArray());
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task BulkInsertActionBarAsync(CharacterData data, MySqlConnection conn, MySqlTransaction tr)
    {
        // A condição de verificação agora procura por qualquer slot que não seja do tipo 'None'.
        if (!data.PlayerActionBar.Slots.Any(s => s.ContentType != ActionBarContentType.None)) return;

        var sb = new StringBuilder("INSERT INTO character_actionbar (character_id, slot_index, content_type, content_id) VALUES ");
        var parameters = new List<MySqlParameter>();
        int paramIndex = 0;

        // <<< CORREÇÃO: Usamos um loop 'for' para ter acesso ao índice 'i' >>>
        for (int i = 0; i < data.PlayerActionBar.Slots.Count; i++)
        {
            var slotData = data.PlayerActionBar.Slots[i];

            // Só salvamos slots que têm conteúdo.
            if (slotData != null && slotData.ContentType != ActionBarContentType.None)
            {
                sb.Append($"(@charId{paramIndex}, @slot{paramIndex}, @type{paramIndex}, @contentId{paramIndex}),");
                parameters.Add(new MySqlParameter($"@charId{paramIndex}", data.CharacterId));

                // Usamos o índice 'i' do loop.
                parameters.Add(new MySqlParameter($"@slot{paramIndex}", i));

                // Acessamos as propriedades diretamente do objeto 'slotData'.
                parameters.Add(new MySqlParameter($"@type{paramIndex}", slotData.ContentType.ToString()));
                parameters.Add(new MySqlParameter($"@contentId{paramIndex}", slotData.ContentID));
                paramIndex++;
            }
        }

        // Se não adicionamos nenhum slot (todos estavam vazios), não fazemos nada.
        if (paramIndex == 0) return;

        sb.Length--; // Remove a última vírgula
        var cmd = new MySqlCommand(sb.ToString(), conn, tr);
        cmd.Parameters.AddRange(parameters.ToArray());
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Métodos Auxiliares de Carregamento

    private async Task LoadInventoryAsync(CharacterData data, MySqlConnection conn)
    {
        var cmd = new MySqlCommand("SELECT slot_index, item_id, quantity, instance_id FROM character_inventory WHERE character_id = @id", conn);
        cmd.Parameters.AddWithValue("@id", data.CharacterId);
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                int slotIndex = Convert.ToInt32(reader["slot_index"]);
                if (slotIndex < data.PlayerInventory.slots.Count)
                {
                    data.PlayerInventory.slots[slotIndex] = new ItemStack(
                        Convert.ToString(reader["item_id"]),
                        Convert.ToInt32(reader["quantity"])
                    )
                    { InstanceID = Convert.ToString(reader["instance_id"]) };
                }
            }
        }
    }

    private async Task LoadEquipmentAsync(CharacterData data, MySqlConnection conn)
    {
        var cmd = new MySqlCommand("SELECT slot_name, item_id, instance_id FROM character_equipment WHERE character_id = @id", conn);
        cmd.Parameters.AddWithValue("@id", data.CharacterId);
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (Enum.TryParse<EquipmentSlot>(Convert.ToString(reader["slot_name"]), out var slot))
                {
                    data.PlayerEquipment.equippedItems[slot] = new ItemStack(
                        Convert.ToString(reader["item_id"]),
                        1 // Equipamentos são sempre quantidade 1
                    )
                    { InstanceID = Convert.ToString(reader["instance_id"]) };
                }
            }
        }
    }

    private async Task LoadActionBarAsync(CharacterData data, MySqlConnection conn)
    {
        var cmd = new MySqlCommand("SELECT slot_index, content_type, content_id FROM character_actionbar WHERE character_id = @id", conn);
        cmd.Parameters.AddWithValue("@id", data.CharacterId);
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                int slotIndex = Convert.ToInt32(reader["slot_index"]);

                // <<< CORREÇÃO: Verificamos se o índice é válido para a lista. >>>
                if (slotIndex >= 0 && slotIndex < data.PlayerActionBar.Slots.Count)
                {
                    data.PlayerActionBar.Slots[slotIndex] = new ActionBarSlotData
                    {
                        ContentType = Enum.Parse<ActionBarContentType>(Convert.ToString(reader["content_type"])),
                        ContentID = Convert.ToString(reader["content_id"])
                    };
                }
            }
        }
    }

    #endregion
}