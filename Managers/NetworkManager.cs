// Managers/NetworkManager.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private const int SAFE_MTU = 1300;

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
    public async Task ListenForPlayerMessagesAsync(CancellationToken cancellationToken)
    {
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("[NetworkManager] Iniciando listener de mensagens UDP...");
        Console.ResetColor();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
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

    /// <summary>
    /// Este método deve ser chamado a cada tick do servidor (ex: 20 vezes por segundo).
    /// Ele processa a fila de saída de cada jogador e envia os pacotes agrupados.
    /// </summary>
    public void DispatchQueuedMessages()
    {
        // Itera sobre uma cópia da lista de jogadores para evitar problemas de concorrência
        // se um jogador se conectar/desconectar durante o loop.
        var allPlayers = _server.ConnectedPlayers.Values.ToList();

        foreach (var player in allPlayers)
        {
            var queue = player.GetMessageQueue();
            var queueLock = player.GetQueueLock();

            // Se não há nada para enviar para este jogador, pulamos.
            if (queue.Count == 0) continue;

            // Usamos um MemoryStream para construir nosso pacote grande
            using (var packageStream = new MemoryStream(SAFE_MTU))
            using (var writer = new BinaryWriter(packageStream))
            {
                lock (queueLock)
                {
                    while (queue.Count > 0)
                    {
                        byte[] message = queue.Peek(); // Apenas olhamos, não removemos ainda

                        // O formato do pacote será: [tamanho da msg (2 bytes)][msg][tamanho da msg][msg]...
                        // +2 bytes para o tamanho
                        if (packageStream.Position + message.Length + 2 > SAFE_MTU)
                        {
                            // O pacote atual está cheio. Envia e começa um novo.
                            break;
                        }

                        // Se coube, removemos da fila e escrevemos no nosso "pacotão"
                        queue.Dequeue();

                        // Escreve o tamanho da mensagem (como um ushort = 2 bytes)
                        writer.Write((ushort)message.Length);
                        // Escreve a mensagem em si
                        writer.Write(message);
                    }
                }

                // Se montamos um pacote com pelo menos uma mensagem, enviamos.
                if (packageStream.Position > 0)
                {
                    byte[] finalPackage = packageStream.ToArray();
                    _udpListener.Send(finalPackage, finalPackage.Length, player.EndPoint);
                }
            }
        }
    }

    public void SendFullStateToPlayer(Player player)
    {
        SendInventoryUpdate(player);
        SendEquipmentUpdate(player);
        SendCurrencyUpdate(player);
        SendStatsUpdate(player);
        SendFullQuestLog(player);
        SendVitalsUpdate(player);
        player.StatusEffectController.SendFullEffectListToClient();
    }

    public void SendFullQuestLog(Player player)
    {
        var fullLog = player.QuestLog.AllQuests.Values.ToList();

        string json = JsonConvert.SerializeObject(fullLog);
        SendMessageToPlayer(player, $"QUEST_LOG_INIT|{json}");
    }

    public void SendQuestUpdate(Player player, QuestProgress progress)
    {
        string json = JsonConvert.SerializeObject(progress);
        SendMessageToPlayer(player, $"QUEST_UPDATE|{json}");
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

        SendMessageToPlayer(player, message);
    }

    public void SendVitalsUpdate(Player player)
    {
        string message = string.Format(CultureInfo.InvariantCulture, "PLAYER_VITALS_UPDATE|{0:F0}|{1:F0}|{2:F0}|{3:F0}",
            player.CurrentHealth,
            player.MaxHealth,
            player.CurrentResource,
            player.MaxResource
        );
        SendMessageToPlayer(player, message);
    }

    public void SendInventoryUpdate(Player player)
    {
        // O novo formato inclui o InstanceID
        var inventoryParts = player.PlayerInventory.slots.Select(stack =>
            stack == null ? "null" : $"{stack.InstanceID},{stack.ItemID},{stack.Quantity}"
        );
        string inventoryMessage = "INVENTORY_UPDATE|" + string.Join("|", inventoryParts);
        SendMessageToPlayer(player, inventoryMessage);
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
        SendMessageToPlayer(player, message);
    }

    public void SendCurrencyUpdate(Player player)
    {
        string message = $"CURRENCY_UPDATE|{player.TotalBronze}";
        SendMessageToPlayer(player, message);
    }

    // Em NetworkManager.cs

    // SUBSTITUA SEU MÉTODO ATUAL HandlePlayerMessageAsync POR ESTES DOIS:

    private async Task HandlePlayerMessageAsync(byte[] buffer, IPEndPoint clientEndPoint)
    {
        //Console.WriteLine($"[SERVER] {buffer.Length} bytes recebidos de {clientEndPoint}");

        // Usamos um MemoryStream para ler o pacote recebido
        using (var packageStream = new MemoryStream(buffer))
        using (var reader = new BinaryReader(packageStream))
        {
            // Enquanto houver dados para ler...
            while (packageStream.Position < packageStream.Length)
            {
                try
                {
                    // Lê o tamanho da próxima mensagem (ushort = 2 bytes)
                    ushort messageSize = reader.ReadUInt16();

                    // Garante que o buffer contém a mensagem inteira
                    if (packageStream.Position + messageSize > packageStream.Length)
                    {
                        Console.WriteLine($"[NetworkManager] Erro: Pacote malformado de {clientEndPoint}. Tamanho de mensagem inválido.");
                        break; // Sai do loop se o pacote estiver corrompido
                    }

                    // Lê os bytes da mensagem
                    byte[] messageBytes = reader.ReadBytes(messageSize);

                    // Agora, processa a mensagem individual como antes
                    await ProcessSingleMessage(messageBytes, clientEndPoint);
                }
                catch (EndOfStreamException)
                {
                    // Acontece se o pacote terminar inesperadamente. Apenas paramos de ler.
                    Console.WriteLine($"[NetworkManager] Aviso: Fim do stream atingido ao desempacotar mensagem de {clientEndPoint}.");
                    break;
                }
            }
        }
    }

    // ESTE NOVO MÉTODO CONTÉM A LÓGICA QUE VOCÊ JÁ TINHA
    private async Task ProcessSingleMessage(byte[] messageBuffer, IPEndPoint clientEndPoint)
    {
        var message = Encoding.UTF8.GetString(messageBuffer).TrimEnd('\0');
        if (string.IsNullOrEmpty(message)) return;

        string[] parts = message.Split('|');
        string command = parts[0];
        string clientKey = clientEndPoint.ToString();
        //Console.WriteLine(command);

        if (command == "CONNECT")
        {
            if (parts.Length < 2)
            {
                SendImmediateMessageToEndpoint("ERROR|Token de acesso ausente.", clientEndPoint);
                return;
            }
            string receivedToken = parts[1];
            //Console.WriteLine(receivedToken);
            await HandleNewPlayerConnection(clientKey, clientEndPoint, receivedToken);
        }
        else if (_server.ConnectedPlayers.TryGetValue(clientKey, out Player? player) && player != null)
        {
            player.LastMessageTime = _server.CurrentTimeUtc;
            string? messageToBroadcast = null;


            switch (command)
            {
                case "HEARTBEAT":
                    SendMessageToPlayer(player, $"PONG|{parts[1]}");
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
                case "REQUEST_USE_ITEM": // Formato: REQUEST_USE_ITEM|inventorySlot
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int useSlot))
                    {
                        _server.PlayerInventoryManager.HandleUseItemRequest(player, useSlot);
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
                SendMessageToPlayer(existingPlayer, "FATAL_ERROR|Sua conta foi conectada de outro local.");
                _server.DisconnectPlayer(existingPlayer.EndPoint.ToString(), "Conectado de outra localidade.");
            }

            Console.WriteLine($"[Conexão] Autenticado: {playerInfo.Username} | Perm: {playerInfo.PermissionLevel}, Personagem: {playerInfo.CharacterName} (Classe: {playerInfo.ClassID}, Nível: {playerInfo.Level})");

            CharacterData characterData = await _characterDb.LoadOrCreateAsync(playerInfo);
            var newPlayer = new Player(clientEndPoint, playerInfo, _server, characterData)
            {
                IsPendingInitialization = true
            };

            if (_server.ConnectedPlayers.TryAdd(clientKey, newPlayer))
            {
                _server.PlayersBySessionId.TryAdd(newPlayer.SessionId, newPlayer);
                Console.WriteLine($"Novo jogador ({newPlayer.SessionId}) conectado de {clientEndPoint}. Total: {_server.ConnectedPlayers.Count}");
                OnlineStatusManager.SetOnline(newPlayer.CharacterId); // Usa o ID permanente para status global

                string assignIdMessage = $"ASSIGN_ID|{newPlayer.CharacterId}|{newPlayer.SessionId}";
                SendMessageToPlayer(newPlayer, assignIdMessage);
                string spawnMessage = newPlayer.GetSpawnMessage();
                SendMessageToPlayer(newPlayer, spawnMessage);
            }
        }
        else
        {
            SendImmediateMessageToEndpoint("ERROR|Token de acesso inválido ou expirado.", clientEndPoint);
        }
    }

    private void HandleOpenShopRequest(Player player, string npcId)
    {
        // Verifica se o NPC existe e se ele é um vendedor registrado
        if (!_server.ActiveNpcs.TryGetValue(npcId, out var npcInstance) ||
            !DataManager.Vendors.TryGetValue(npcInstance.BaseData.TypeId, out var vendorData))
        {
            // Envia uma mensagem de falha se o NPC não for um vendedor
            SendMessageToPlayer(player, "ERROR|Este NPC não é um vendedor.");
            return;
        }

        // Serializa a lista de itens do vendedor para enviar ao cliente
        string vendorPayloadJson = JsonConvert.SerializeObject(vendorData.Items);

        // Envia a mensagem para o cliente abrir a janela da loja
        SendMessageToPlayer(player, $"OPEN_SHOP_WINDOW|{npcId}|{vendorPayloadJson}");
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
        // Não precisa mais converter para byte[] aqui
        var candidateEntities = _server.GridManager.GetEntitiesInRadius(centerPosition, visibilityRange);
        var playersToSendTo = candidateEntities.OfType<Player>();

        foreach (var player in playersToSendTo)
        {
            if (excludePlayer != null && player.Id == excludePlayer.Id)
            {
                continue;
            }

            if (_server.ConnectedPlayers.ContainsKey(player.EndPoint.ToString()))
            {
                // Em vez de enviar, enfileiramos!
                SendMessageToPlayer(player, message);
            }
        }
    }

    public void BroadcastMessageToAll(string message)
    {
        foreach (var player in _server.ConnectedPlayers.Values)
        {
            // Em vez de enviar, enfileiramos!
            SendMessageToPlayer(player, message);
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
                SendMessageToPlayer(player, message);
            }
        }
    }

    public void BroadcastMessage(string message, string senderSessionId)
    {
        foreach (var player in _server.ConnectedPlayers.Values.Where(p => p.GuidSessionId != senderSessionId))
        {
            // Em vez de enviar, enfileiramos!
            SendMessageToPlayer(player, message);
        }
    }
    public void SendMessageToPlayer(Player player, string message)
    {
        if (player == null) return;

        byte[] data = Encoding.UTF8.GetBytes(message);
        player.EnqueueMessage(data); // Apenas enfileira!
    }

    public void SendImmediateMessageToEndpoint(string message, IPEndPoint endPoint)
    {
        // Este método NÃO usa a fila. Ele envia um pacote diretamente.
        // É útil para erros de conexão antes que um objeto Player seja criado.
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);

            // CRUCIAL: Precisamos construir o pacote no formato que o cliente espera: [tamanho][dados]
            // Mesmo que seja apenas uma mensagem, ela precisa seguir o formato do pacote.
            using (var packageStream = new MemoryStream())
            using (var writer = new BinaryWriter(packageStream))
            {
                writer.Write((ushort)data.Length); // Escreve o tamanho
                writer.Write(data);                // Escreve a mensagem

                byte[] finalPackage = packageStream.ToArray();
                _udpListener.Send(finalPackage, finalPackage.Length, endPoint);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[SendImmediate] Erro ao enviar mensagem para {endPoint}: {e.Message}");
        }
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