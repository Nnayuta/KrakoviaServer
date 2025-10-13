// Managers/NetworkManager.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading; // Adicionado para CancellationToken
using System.Threading.Tasks;
using Newtonsoft.Json;

public class NetworkManager
{
    private readonly UDPServer _server;
    private readonly UdpClient _udpListener;
    
    private readonly ICharacterDatabase _characterDb;

    public NetworkManager(UDPServer server, UdpClient udpListener, ICharacterDatabase characterDatabase)
    {
        _server = server;
        _udpListener = udpListener;
        _characterDb = characterDatabase;
    }


    /// <summary>
    /// Escuta por mensagens de jogadores de forma assíncrona até que o cancelamento seja solicitado.
    /// </summary>
    /// <param name="cancellationToken">O token para monitorar solicitações de cancelamento.</param>
    // Em NetworkManager.cs

    public async Task ListenForPlayerMessagesAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[NetworkManager] Iniciando listener de mensagens UDP...");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // (MUDANÇA) Removemos o CancellationToken daqui.
                // A tarefa agora só pode ser interrompida por um erro de socket.
                var result = await _udpListener.ReceiveAsync();
                await HandlePlayerMessageAsync(result.Buffer, result.RemoteEndPoint);
            }
            catch (ObjectDisposedException)
            {
                // Esta exceção é esperada quando _udpListener.Close() é chamado.
                // Significa que estamos desligando, então saímos do loop.
                Console.WriteLine("[NetworkManager] Listener de UDP foi fechado para shutdown.");
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted || ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // Essas são exceções normais que podem acontecer. Interrupted acontece no shutdown em Linux.
                // ConnectionReset acontece se um cliente "some". Apenas continuamos.
            }
            catch (Exception e)
            {
                // Se o token foi cancelado, é um shutdown normal. Senão, é um erro real.
                if (!cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine($"[NetworkManager] Erro inesperado ao receber mensagem: {e.Message}");
                }
                else
                {
                    // Se o token foi cancelado e chegamos aqui, apenas saímos.
                    break;
                }
            }
        }
        Console.WriteLine("[NetworkManager] Listener de mensagens UDP finalizado.");
    }

    public void SendFullStateToPlayer(Player player)
    {
        SendInventoryUpdate(player);
        SendEquipmentUpdate(player);
        SendCurrencyUpdate(player);
        SendStatsUpdate(player);
        SendFullQuestLog(player);
        SendVitalsUpdate(player);
    }

    public void SendFullQuestLog(Player player)
    {
        var fullLog = player.QuestLog.AllQuests.Values.ToList();

        string json = JsonConvert.SerializeObject(fullLog);
        SendMessageToClient($"QUEST_LOG_INIT|{json}", player.EndPoint);
    }

    public void SendQuestUpdate(Player player, QuestProgress progress)
    {
        string json = JsonConvert.SerializeObject(progress);
        SendMessageToClient($"QUEST_UPDATE|{json}", player.EndPoint);
    }

    public void SendStatsUpdate(Player player)
    {
        // Pega todos os valores possíveis do enum StatType
        var allStatTypes = Enum.GetValues(typeof(StatType)).Cast<StatType>();

        // Constrói as partes da mensagem "StatTypeInt,Value"
        var statParts = allStatTypes.Select(stat =>
        {
            // Pega o valor final calculado do sistema de stats do jogador
            float value = player.Stats.GetStatValue(stat);
            // Formata como "int,float" (ex: "0,150.5")
            return $"{(int)stat},{value.ToString(CultureInfo.InvariantCulture)}";
        });

        // Junta tudo na mensagem final
        string message = $"STATS_UPDATE|{player.Id}|{string.Join("|", statParts)}";

        SendMessageToClient(message, player.EndPoint);
    }

    public void SendVitalsUpdate(Player player)
    {
        string message = string.Format(CultureInfo.InvariantCulture, "PLAYER_VITALS_UPDATE|{0:F0}|{1:F0}|{2:F0}|{3:F0}",
            player.CurrentHealth,
            player.MaxHealth,
            player.CurrentResource,
            player.MaxResource
        );
        SendMessageToClient(message, player.EndPoint);
    }

    public void SendInventoryUpdate(Player player)
    {
        // O novo formato inclui o InstanceID
        var inventoryParts = player.PlayerInventory.slots.Select(stack =>
            stack == null ? "null" : $"{stack.InstanceID},{stack.ItemID},{stack.Quantity}"
        );
        string inventoryMessage = "INVENTORY_UPDATE|" + string.Join("|", inventoryParts);
        SendMessageToClient(inventoryMessage, player.EndPoint);
    }

    public void SendEquipmentUpdate(Player player)
    {
        // Formato: EQUIP_UPDATE|Slot1,InstanceID,ItemID,Qty|Slot2,null|...
        var equipmentParts = player.PlayerEquipment.equippedItems.Select(pair =>
        {
            EquipmentSlot slot = pair.Key;
            ItemStack stack = pair.Value;
            if (stack == null)
            {
                return $"{slot},null";
            }
            return $"{slot},{stack.InstanceID},{stack.ItemID},{stack.Quantity}";
        });

        string message = "EQUIPMENT_UPDATE|" + string.Join("|", equipmentParts);
        SendMessageToClient(message, player.EndPoint);
    }

    public void SendCurrencyUpdate(Player player)
    {
        string message = $"CURRENCY_UPDATE|{player.TotalBronze}";
        SendMessageToClient(message, player.EndPoint);
    }

    private async Task HandlePlayerMessageAsync(byte[] buffer, IPEndPoint clientEndPoint)
    {
        var message = Encoding.UTF8.GetString(buffer).TrimEnd('\0');
        if (string.IsNullOrEmpty(message)) return;

        string[] parts = message.Split('|');
        string command = parts[0];
        string clientKey = clientEndPoint.ToString();

        if (command == "CONNECT")
        {
            if (parts.Length < 2)
            {
                SendMessageToClient("ERROR|Token de acesso ausente.", clientEndPoint);
                return;
            }
            string receivedToken = parts[1];
            // <<< MUDANÇA 5: Agora usamos 'await' para esperar a conexão do jogador ser processada.
            await HandleNewPlayerConnection(clientKey, clientEndPoint, receivedToken);
        }
        else if (_server.ConnectedPlayers.TryGetValue(clientKey, out Player? player) && player != null)
        {
            player.LastMessageTime = _server.CurrentTimeUtc;
            string? messageToBroadcast = null;

            switch (command)
            {
                case "HEARTBEAT":
                    SendMessageToClient($"PONG|{parts[1]}", clientEndPoint);
                    break;

                case "PLAYER_QUITTING":
                    await HandlePlayerQuitting(clientKey, player);
                    break;

                case "POS_ROT":
                    HandlePositionRotation(parts, player);
                    break;

                case "REQUEST_OPEN_SHOP":
                    if (parts.Length >= 2)
                    {
                        HandleOpenShopRequest(player, parts[1]);
                    }
                    break;

                case "REQUEST_USE_ABILITY":
                    if (parts.Length >= 3)
                    {
                        _server.CombatManager.ProcessAbilityRequest(player, parts[1], parts[2]);
                    }
                    break;
                case "EQUIP_ITEM":
                    if (parts.Length >= 3 &&
                        int.TryParse(parts[1], out int invSlotToEquip) &&
                        Enum.TryParse<EquipmentSlot>(parts[2], true, out var eqSlotToEquip))
                    {
                        _server.PlayerEquipmentManager.HandleEquipItemRequest(player, invSlotToEquip, eqSlotToEquip);
                    }
                    break;
                case "UPDATE_ACTIONBAR":
                    HandleActionBarUpdate(player, parts);
                    break;

                case "UNEQUIP_ITEM":
                    if (parts.Length >= 2 &&
                        Enum.TryParse<EquipmentSlot>(parts[1], true, out var eqSlotToUnequip))
                    {
                        _server.PlayerEquipmentManager.HandleUnequipItemRequest(player, eqSlotToUnequip);
                    }
                    break;

                case "REQUEST_MOVE_ITEM": // Formato: REQUEST_MOVE_ITEM|fromSlot|toSlot
                    if (parts.Length >= 3 && int.TryParse(parts[1], out int from) && int.TryParse(parts[2], out int to))
                    {
                        _server.PlayerInventoryManager.HandleMoveItemRequest(player, from, to);
                    }
                    break;
                case "REQUEST_BUY_ITEM": // Formato: REQUEST_BUY_ITEM|npcId|itemId|quantity
                    if (parts.Length >= 4 && int.TryParse(parts[3], out int buyQty))
                    {
                        _server.PlayerInventoryManager.HandleBuyItemRequest(player, parts[1], parts[2], buyQty);
                    }
                    break;
                case "REQUEST_SELL_ITEM": // Formato: REQUEST_SELL_ITEM|npcId|inventorySlot|quantity
                    if (parts.Length >= 4 && int.TryParse(parts[2], out int sellSlot) && int.TryParse(parts[3], out int sellQty))
                    {
                        _server.PlayerInventoryManager.HandleSellItemRequest(player, parts[1], sellSlot, sellQty);
                    }
                    break;
                case "REQUEST_CANCEL_CAST":
                    _server.CombatManager.HandleCancelCastRequest(player);
                    break;
                case "REQUEST_RESPAWN":
                    _server.PlayerLifecycleManager.HandleRespawnRequest(player);
                    break;
                case "REQUEST_LOOT":
                    if (parts.Length >= 2)
                    {
                        _server.CombatManager.HandleLootRequest(player, parts[1]);
                    }
                    break;
                case "REQUEST_GATHER":
                    if (parts.Length >= 2)
                    {
                        // Encaminha a solicitação para o GatherableManager processar.
                        _server.GatherableManager.OnPlayerAttemptGather(player, parts[1]);
                    }
                    break;
                case "REQUEST_ACCEPT_QUEST":
                    if (parts.Length >= 2)
                    {
                        _server.QuestManager.HandleAcceptQuestRequest(player, parts[1]);
                    }
                    break;

                case "REQUEST_ABANDON_QUEST":
                    if (parts.Length >= 2)
                    {
                        _server.QuestManager.HandleAbandonQuestRequest(player, parts[1]);
                    }
                    break;

                case "REQUEST_COMPLETE_QUEST":
                    if (parts.Length >= 2)
                    {
                        _server.QuestManager.HandleCompleteQuestRequest(player, parts[1]);
                    }
                    break;
                case "SEND_CHAT_MSG":
                    if (parts.Length > 1)
                    {
                        string chatMessage = string.Join("|", parts.Skip(1)); // Remonta a mensagem caso ela tenha '|'
                        _server.ChatManager.ProcessChatMessage(player, chatMessage);
                    }
                    break;
            }

            if (messageToBroadcast != null)
            {
                // Passa o GUID, que é o que a função espera.
                BroadcastMessage(messageToBroadcast, player.GuidSessionId);
            }
        }
    }

    public void SendInitialStateToPlayer(Player player)
    {
        SendFullStateToPlayer(player);
    }

    private async Task HandleNewPlayerConnection(string clientKey, IPEndPoint clientEndPoint, string token)
    {
        if (_server.ConnectedPlayers.ContainsKey(clientKey)) return;

        if (AuthTokenManager.IsTokenValid(token, out AuthenticatedPlayerInfo? playerInfo) && playerInfo != null)
        {
            // Lógica para desconectar sessão antiga (está correta)
            var existingPlayer = _server.ConnectedPlayers.Values.FirstOrDefault(p => p.CharacterId == playerInfo.CharacterId);
            if (existingPlayer != null)
            {
                Console.WriteLine($"[Conexão Duplicada] Personagem {playerInfo.CharacterId} já estava online. Desconectando a sessão antiga.");
                SendMessageToClient("FATAL_ERROR|Sua conta foi conectada de outro local.", existingPlayer.EndPoint);
                _server.DisconnectPlayer(existingPlayer.EndPoint.ToString(), "Conectado de outra localidade.");
            }

            Console.WriteLine($"[Conexão] Autenticado: {playerInfo.Username} | Perm: {playerInfo.PermissionLevel}, Personagem: {playerInfo.CharacterName} (Classe: {playerInfo.ClassID}, Nível: {playerInfo.Level})");

            CharacterData characterData = await _characterDb.LoadOrCreateAsync(playerInfo);
            var newPlayer = new Player(clientEndPoint, playerInfo, _server, characterData);
            newPlayer.IsPendingInitialization = true;

            if (_server.ConnectedPlayers.TryAdd(clientKey, newPlayer))
            {
                _server.PlayersBySessionId.TryAdd(newPlayer.SessionId, newPlayer);
                Console.WriteLine($"Novo jogador ({newPlayer.SessionId}) conectado de {clientEndPoint}. Total: {_server.ConnectedPlayers.Count}");
                OnlineStatusManager.SetOnline(newPlayer.CharacterId); // Usa o ID permanente para status global

                // --- [CORREÇÃO 1] ---
                // A mensagem ASSIGN_ID precisa enviar AMBOS os IDs.
                // O CharacterId (GUID) para o cliente saber "quem eu sou".
                // O SessionId (int) para o cliente saber o seu próprio ID de sessão.
                string assignIdMessage = $"ASSIGN_ID|{newPlayer.CharacterId}|{newPlayer.SessionId}";
                SendMessageToClient(assignIdMessage, clientEndPoint);

                // --- [CORREÇÃO 2] ---
                // A mensagem de spawn do PRÓPRIO jogador também precisa de ambos os IDs.
                // O cliente usa isso para se instanciar.
                string spawnMessage = newPlayer.GetSpawnMessage();
                SendMessageToClient(spawnMessage, clientEndPoint);
            }
        }
        else
        {
            SendMessageToClient("ERROR|Token de acesso inválido ou expirado.", clientEndPoint);
        }
    }

    private void HandleOpenShopRequest(Player player, string npcId)
    {
        // Verifica se o NPC existe e se ele é um vendedor registrado
        if (!_server.ActiveNpcs.TryGetValue(npcId, out var npcInstance) ||
            !DataManager.Vendors.TryGetValue(npcInstance.BaseData.TypeId, out var vendorData))
        {
            // Envia uma mensagem de falha se o NPC não for um vendedor
            SendMessageToClient("ERROR|Este NPC não é um vendedor.", player.EndPoint);
            return;
        }

        // Serializa a lista de itens do vendedor para enviar ao cliente
        string vendorPayloadJson = JsonConvert.SerializeObject(vendorData.Items);

        // Envia a mensagem para o cliente abrir a janela da loja
        SendMessageToClient($"OPEN_SHOP_WINDOW|{npcId}|{vendorPayloadJson}", player.EndPoint);
        Console.WriteLine($"[Loja] Jogador {player.Username} abriu a loja do NPC {npcId}.");
    }

    public async Task HandlePlayerQuitting(string clientKey, Player player)
    {
        Console.WriteLine($"Jogador {player.Username} informou que está desconectando.");

        CharacterData dataToSave = player.GetCharacterDataForSaving();
        await _characterDb.SaveAsync(dataToSave);

        if (_server.ConnectedPlayers.TryRemove(clientKey, out _))
        {
            // (CORREÇÃO) Ao sair, removemos dos dois dicionários
            _server.PlayersBySessionId.TryRemove(player.SessionId, out _);

            _server.GridManager.RemoveEntity(player);
            OnlineStatusManager.SetOffline(player.CharacterId); // Usa o ID permanente

            // A mensagem PLAYER_LEFT deve usar o SessionId, pois é para outros jogadores em tempo real
            BroadcastMessageToRelevantPlayers(player.Position, $"PLAYER_LEFT|{player.Id}");
        }
    }

    private void HandleActionBarUpdate(Player player, string[] parts)
    {
        // Limpa a barra de ações antiga para preencher com os novos dados
        player.PlayerActionBar.Clear();

        // Pula o comando 'UPDATE_ACTIONBAR' (índice 0)
        for (int i = 1; i < parts.Length; i++)
        {
            string[] data = parts[i].Split(',');
            if (data.Length == 3 && int.TryParse(data[0], out int index))
            {
                // O ideal é que ActionBarData no servidor também use SlotData.
                player.PlayerActionBar.Slots[index] = new ActionBarSlotData
                {
                    ContentType = (ActionBarContentType)Enum.Parse(typeof(ActionBarContentType), data[1]),
                    ContentID = data[2]
                };
            }
        }
        Console.WriteLine($"[ActionBar] Barra de ações do jogador '{player.Username}' foi atualizada no servidor.");
    }

    private void HandlePositionRotation(string[] parts, Player player)
    {
        if (parts.Length < 7) return;

        if (player.IsPendingInitialization)
        {
            Console.WriteLine($"[Sync] Recebida primeira POS_ROT de {player.Username}. Iniciando sincronização de estado e interesse.");
            player.IsPendingInitialization = false;

            SendInitialStateToPlayer(player);
            _server.InterestManager.OnPlayerEnteredWorld(player);
        }

        player.Position = new Vector3(
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture),
            float.Parse(parts[3], CultureInfo.InvariantCulture)
        );

        _server.GridManager.UpdateEntity(player);

        string messageToBroadcast = $"{parts[0]}|{player.Id}|{string.Join("|", parts.Skip(1))}";
        BroadcastMessageToRelevantPlayers(player.Position, messageToBroadcast, player);
    }

    /// <summary>
    /// Envia uma mensagem para todos os jogadores que estão dentro de um raio de visibilidade de uma posição central.
    /// </summary>
    /// <param name="centerPosition">O ponto onde a ação aconteceu.</param>
    /// <param name="message">A mensagem a ser enviada.</param>
    /// <param name="excludePlayer"> (Opcional) O jogador que originou a ação e não deve receber a mensagem de volta.</param>
    /// <param name="visibilityRange">O raio de envio.</param>
    // Managers/NetworkManager.cs

    public void BroadcastMessageToRelevantPlayers(Vector3 centerPosition, string message, Player? excludePlayer = null, float visibilityRange = 80f)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);

        // 1. Pega todas as entidades próximas.
        var candidateEntities = _server.GridManager.GetEntitiesInRadius(centerPosition, visibilityRange);

        // 2. Filtra para pegar apenas os jogadores.
        var playersToSendTo = candidateEntities.OfType<Player>();

        // 3. Itera diretamente sobre a lista de jogadores encontrados.
        foreach (var player in playersToSendTo)
        {
            // A condição de exclusão: Não envia se o jogador da lista for o mesmo
            // que originou a mensagem (o excludePlayer).
            if (excludePlayer != null && player.Id == excludePlayer.Id)
            {
                continue; // Pula para o próximo jogador
            }

            // Garante que o jogador ainda está conectado.
            // A chave do seu dicionário é o EndPoint.ToString().
            if (_server.ConnectedPlayers.ContainsKey(player.EndPoint.ToString()))
            {
                _udpListener.Send(data, data.Length, player.EndPoint);
            }
        }
    }

    public void BroadcastMessageToAll(string message)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);
        foreach (var player in _server.ConnectedPlayers.Values)
        {
            _udpListener.Send(data, data.Length, player.EndPoint);
        }
    }

    /// <summary>
    /// Envia uma mensagem para todos os jogadores conectados, EXCETO para o jogador de origem.
    /// </summary>
    /// <param name="sourcePlayer">O jogador que não deve receber a mensagem.</param>
    /// <param name="message">A mensagem a ser enviada.</param>
    public void BroadcastMessageToOthers(Player sourcePlayer, string message)
    {
        // Pega uma cópia da lista de EndPoints de todos os jogadores conectados
        var allPlayers = _server.ConnectedPlayers.Values.ToList();

        foreach (var player in allPlayers)
        {
            // A condição crucial: só envia se o ID do jogador no loop for diferente do ID do jogador de origem.
            if (player.Id != sourcePlayer.Id)
            {
                SendMessageToClient(message, player.EndPoint);
            }
        }
    }

    public void BroadcastMessage(string message, string senderSessionId)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);
        foreach (var player in _server.ConnectedPlayers.Values.Where(p => p.GuidSessionId != senderSessionId))
        {
            _udpListener.Send(data, data.Length, player.EndPoint);
        }
    }

    public void SendMessageToClient(string message, IPEndPoint endPoint)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);
        _udpListener.Send(data, data.Length, endPoint);
    }

    public Vector3 ToEulerAngles(Quaternion q)
    {
        Vector3 angles = new();

        // Roll (eixo x)
        double sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        double cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        angles.X = (float)Math.Atan2(sinr_cosp, cosr_cosp);

        // Pitch (eixo y)
        double sinp = 2 * (q.W * q.Y - q.Z * q.X);
        if (Math.Abs(sinp) >= 1)
            angles.Y = (float)Math.CopySign(Math.PI / 2, sinp); // Use 90 graus se estiver olhando para cima/baixo
        else
            angles.Y = (float)Math.Asin(sinp);

        // Yaw (eixo z)
        double siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        double cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        angles.Z = (float)Math.Atan2(siny_cosp, cosy_cosp);

        // Converte de radianos para graus para ser compatível com o Unity
        angles.X *= (float)(180.0 / Math.PI);
        angles.Y *= (float)(180.0 / Math.PI);
        angles.Z *= (float)(180.0 / Math.PI);

        return angles;
    }
}