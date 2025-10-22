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

/// <summary>
/// Representa um item de vendedor com seu preço de compra já calculado para um jogador específico.
/// </summary>
public class VendorItemForClient
{
    public string ItemID { get; set; }
    public int BuyPrice { get; set; } // << RENOMEADO para BuyPrice
}

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
                _ = Task.Run(() => HandlePlayerMessageAsync(result.Buffer, result.RemoteEndPoint), cancellationToken);
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
    public async Task DispatchQueuedMessages()
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

                if (packageStream.Position > 0)
                {
                    try
                    {
                        byte[] finalPackage = packageStream.ToArray();
                        // --- A MUDANÇA CRÍTICA ESTÁ AQUI ---
                        // Usamos a versão assíncrona para não bloquear o tick do servidor.
                        await _udpListener.SendAsync(finalPackage, finalPackage.Length, player.EndPoint);
                    }
                    catch (Exception ex)
                    {
                        // Adicionar um log aqui é útil para depurar problemas de envio.
                        Console.WriteLine($"[Dispatch] Erro ao enviar pacote para {player.Username}: {ex.Message}");
                    }
                }
            }
        }
    }

    public void SendFullStateToPlayer(Player player)
    {
        // Envia todas as mensagens de estado imediatamente.
        SendInventoryUpdate(player, true);
        SendEquipmentUpdate(player, true);
        SendCurrencyUpdate(player, true);
        SendStatsUpdate(player, true);
        SendFullQuestLog(player, true);
        SendVitalsUpdate(player, true);
        player.StatusEffectController.SendFullEffectListToClient(); // Este já envia direto, está ok.
    }

    public void SendFullQuestLog(Player player, bool immediate = false)
    {
        var fullLog = player.QuestLog.AllQuests.Values.ToList();
        string json = JsonConvert.SerializeObject(fullLog);
        string message = $"QUEST_LOG_INIT|{json}";

        if (immediate)
            SendImmediateMessageToEndpoint(message, player.EndPoint);
        else
            SendMessageToPlayer(player, message);
    }

    public void SendQuestUpdate(Player player, QuestProgress progress)
    {
        string json = JsonConvert.SerializeObject(progress);
        SendMessageToPlayer(player, $"QUEST_UPDATE|{json}");
    }

    public void SendStatsUpdate(Player player, bool immediate = false)
    {
        var allStatTypes = Enum.GetValues(typeof(StatType)).Cast<StatType>();
        var statParts = allStatTypes.Select(stat =>
        {
            float value = player.Stats.GetStatValue(stat);
            return $"{(int)stat},{value.ToString(CultureInfo.InvariantCulture)}";
        });
        string message = $"STATS_UPDATE|{player.Id}|{string.Join("|", statParts)}";

        if (immediate)
            SendImmediateMessageToEndpoint(message, player.EndPoint);
        else
            SendMessageToPlayer(player, message);
    }

    public void SendVitalsUpdate(Player player, bool immediate = false)
    {
        string message = string.Format(CultureInfo.InvariantCulture, "PLAYER_VITALS_UPDATE|{0:F0}|{1:F0}|{2:F0}|{3:F0}",
            player.CurrentHealth, player.MaxHealth, player.CurrentResource, player.MaxResource);

        if (immediate)
            SendImmediateMessageToEndpoint(message, player.EndPoint);
        else
            SendMessageToPlayer(player, message);
    }

    public void SendInventoryUpdate(Player player, bool immediate = false)
    {
        var inventoryParts = player.PlayerInventory.slots.Select(stack =>
            stack == null ? "null" : $"{stack.InstanceID},{stack.ItemID},{stack.Quantity}"
        );
        string inventoryMessage = "INVENTORY_UPDATE|" + string.Join("|", inventoryParts);

        if (immediate)
            SendImmediateMessageToEndpoint(inventoryMessage, player.EndPoint);
        else
            SendMessageToPlayer(player, inventoryMessage);
    }

    public void SendEquipmentUpdate(Player player, bool immediate = false)
    {
        var equipmentParts = player.PlayerEquipment.equippedItems.Select(pair =>
        {
            EquipmentSlot slot = pair.Key;
            ItemStack stack = pair.Value;
            return stack == null ? $"{slot},null" : $"{slot},{stack.InstanceID},{stack.ItemID},{stack.Quantity}";
        });
        string message = "EQUIPMENT_UPDATE|" + string.Join("|", equipmentParts);

        if (immediate)
            SendImmediateMessageToEndpoint(message, player.EndPoint);
        else
            SendMessageToPlayer(player, message);
    }

    public void SendCurrencyUpdate(Player player, bool immediate = false)
    {
        string message = $"CURRENCY_UPDATE|{player.TotalBronze}";

        if (immediate)
            SendImmediateMessageToEndpoint(message, player.EndPoint);
        else
            SendMessageToPlayer(player, message);
    }

    private async Task HandlePlayerMessageAsync(byte[] buffer, IPEndPoint clientEndPoint)
    {
        // --- A MUDANÇA CRÍTICA ESTÁ AQUI ---
        // Um try-catch geral para garantir que uma mensagem ruim não mate a tarefa silenciosamente.
        try
        {
            using (var packageStream = new MemoryStream(buffer))
            using (var reader = new BinaryReader(packageStream))
            {
                while (packageStream.Position < packageStream.Length)
                {
                    try
                    {
                        ushort messageSize = reader.ReadUInt16();
                        if (packageStream.Position + messageSize > packageStream.Length)
                        {
                            Console.WriteLine($"[NetworkManager] Erro: Pacote malformado de {clientEndPoint}. Tamanho de mensagem inválido.");
                            break;
                        }

                        byte[] messageBytes = reader.ReadBytes(messageSize);
                        await ProcessSingleMessage(messageBytes, clientEndPoint);
                    }
                    catch (EndOfStreamException)
                    {
                        Console.WriteLine($"[NetworkManager] Aviso: Fim do stream atingido ao desempacotar mensagem de {clientEndPoint}.");
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NetworkManager] Erro CRÍTICO ao processar pacote de {clientEndPoint}. Erro: {ex.Message}\nStack: {ex.StackTrace}");
        }
    }

    // ESTE NOVO MÉTODO CONTÉM A LÓGICA QUE VOCÊ JÁ TINHA
    private async Task ProcessSingleMessage(byte[] messageBuffer, IPEndPoint clientEndPoint)
    {
        var message = Encoding.UTF8.GetString(messageBuffer).TrimEnd('\0');
        if (string.IsNullOrEmpty(message)) return;

        string[] parts = message.Split('|');
        string command = parts[0];

        // --- LÓGICA DE CONEXÃO (JÁ ESTÁ CORRETA, APENAS VERIFICAR) ---
        if (command == "CONNECT")
        {
            if (parts.Length < 3) // Espera CONNECT|Token|Guid
            {
                SendImmediateMessageToEndpoint("ERROR|Token de acesso ausente.", clientEndPoint);
                return;
            }
            string receivedToken = parts[1];
            string connectionGuid = parts[2];
            await HandleNewPlayerConnection(connectionGuid, clientEndPoint, receivedToken);
            return; // Encerra o processamento para a mensagem CONNECT
        }

        // --- NOVA LÓGICA PARA MENSAGENS PÓS-CONEXÃO ---

        // 1. Extrai o ConnectionGuid da mensagem. Ele é sempre o último parâmetro.
        if (parts.Length < 2) return; // Mensagem inválida sem comando e GUID
        string receivedConnectionGuid = parts.Last();

        // 2. Busca o jogador usando o GUID como chave.
        if (_server.ConnectedPlayers.TryGetValue(receivedConnectionGuid, out Player? player) && player != null)
        {
            // O jogador foi encontrado! Prossiga com a lógica.
            player.LastMessageTime = _server.CurrentTimeUtc;

            // Recria o array 'parts' sem o GUID no final para que o resto do código funcione como antes.
            string[] originalParts = new string[parts.Length - 1];
            Array.Copy(parts, originalParts, originalParts.Length);

            // Agora, use 'originalParts' em seu switch.
            string originalCommand = originalParts[0];


            switch (originalCommand)
            {
                case "HEARTBEAT":
                    // Console.WriteLine($"HEARTBEAT: PLAYER: {player.Id}");
                    SendImmediateMessageToEndpoint($"PONG|{parts[1]}", clientEndPoint);
                    break;

                case "PLAYER_QUITTING":
                    await HandlePlayerQuitting(player.ConnectionGuid, player);
                    break;

                case "POS_ROT":
                    HandlePositionRotation(originalParts, player);
                    break;

                case "REQUEST_OPEN_SHOP":
                    if (originalParts.Length >= 2)
                    {
                        HandleOpenShopRequest(player, originalParts[1]);
                    }
                    break;

                case "REQUEST_USE_ABILITY":
                    if (originalParts.Length >= 3)
                    {
                        _server.CombatManager.ProcessAbilityRequest(player, originalParts[1], originalParts[2]);
                    }
                    break;
                case "EQUIP_ITEM":
                    if (originalParts.Length >= 3 &&
                        int.TryParse(originalParts[1], out int invSlotToEquip) &&
                        Enum.TryParse<EquipmentSlot>(originalParts[2], true, out var eqSlotToEquip))
                    {
                        _server.PlayerEquipmentManager.HandleEquipItemRequest(player, invSlotToEquip, eqSlotToEquip);
                    }
                    break;
                case "UPDATE_ACTIONBAR":
                    HandleActionBarUpdate(player, originalParts);
                    break;

                case "UNEQUIP_ITEM":
                    if (originalParts.Length >= 2 &&
                        Enum.TryParse<EquipmentSlot>(originalParts[1], true, out var eqSlotToUnequip))
                    {
                        _server.PlayerEquipmentManager.HandleUnequipItemRequest(player, eqSlotToUnequip);
                    }
                    break;

                case "REQUEST_MOVE_ITEM": // Formato: REQUEST_MOVE_ITEM|fromSlot|toSlot
                    if (originalParts.Length >= 3 && int.TryParse(originalParts[1], out int from) && int.TryParse(originalParts[2], out int to))
                    {
                        _server.PlayerInventoryManager.HandleMoveItemRequest(player, from, to);
                    }
                    break;
                case "REQUEST_BUY_ITEM": // Formato: REQUEST_BUY_ITEM|npcId|itemId|quantity
                    if (originalParts.Length >= 4 && int.TryParse(originalParts[3], out int buyQty))
                    {
                        _server.PlayerInventoryManager.HandleBuyItemRequest(player, originalParts[1], originalParts[2], buyQty);
                    }
                    break;
                case "REQUEST_SELL_ITEM": // Formato: REQUEST_SELL_ITEM|npcId|inventorySlot|quantity
                    if (originalParts.Length >= 4 && int.TryParse(originalParts[2], out int sellSlot) && int.TryParse(originalParts[3], out int sellQty))
                    {
                        _server.PlayerInventoryManager.HandleSellItemRequest(player, originalParts[1], sellSlot, sellQty);
                    }
                    break;
                case "REQUEST_CANCEL_CAST":
                    _server.CombatManager.HandleCancelCastRequest(player);
                    break;
                case "REQUEST_RESPAWN":
                    _server.PlayerLifecycleManager.HandleRespawnRequest(player);
                    break;
                case "REQUEST_LOOT":
                    if (originalParts.Length >= 2)
                    {
                        _server.CombatManager.HandleLootRequest(player, originalParts[1]);
                    }
                    break;
                case "REQUEST_GATHER":
                    if (originalParts.Length >= 2)
                    {
                        // Encaminha a solicitação para o GatherableManager processar.
                        _server.GatherableManager.OnPlayerAttemptGather(player, originalParts[1]);
                    }
                    break;
                case "REQUEST_ACCEPT_QUEST":
                    if (originalParts.Length >= 2)
                    {
                        _server.QuestManager.HandleAcceptQuestRequest(player, originalParts[1]);
                    }
                    break;

                case "REQUEST_ABANDON_QUEST":
                    if (originalParts.Length >= 2)
                    {
                        _server.QuestManager.HandleAbandonQuestRequest(player, originalParts[1]);
                    }
                    break;

                case "REQUEST_COMPLETE_QUEST":
                    if (originalParts.Length >= 2)
                    {
                        _server.QuestManager.HandleCompleteQuestRequest(player, originalParts[1]);
                    }
                    break;
                case "SEND_CHAT_MSG":
                    if (originalParts.Length > 1)
                    {
                        string chatMessage = string.Join("|", originalParts.Skip(1)); // Remonta a mensagem caso ela tenha '|'
                        _server.ChatManager.ProcessChatMessage(player, chatMessage);
                    }
                    break;
                case "REQUEST_USE_ITEM": // Formato: REQUEST_USE_ITEM|inventorySlot
                    if (originalParts.Length >= 2 && int.TryParse(originalParts[1], out int useSlot))
                    {
                        _server.PlayerInventoryManager.HandleUseItemRequest(player, useSlot);
                    }
                    break;

                case "ANIM":
                    if (originalParts.Length < 3) break; // comando inválido
                    string animType = originalParts[1]; // TRIGGER ou BOOL
                    string animParam = originalParts[2]; // nome do trigger/boolean
                    string animValue = originalParts.Length > 3 ? originalParts[3] : "";

                    // Cria a mensagem para broadcast
                    string messageToBroadcast = $"PLAYER_ANIM|{player.Id}|{animType}|{animParam}|{animValue}";
                    BroadcastMessageToRelevantPlayers(player.Position, messageToBroadcast, player);
                    break;
            }
        }
    }

    private async Task HandleNewPlayerConnection(string connectionGuid, IPEndPoint clientEndPoint, string token)
    {
        // Usa o GUID para verificar se já existe uma conexão com esse "crachá"
        if (_server.ConnectedPlayers.ContainsKey(connectionGuid)) return;

        if (AuthTokenManager.IsTokenValid(token, out AuthenticatedPlayerInfo? playerInfo) && playerInfo != null)
        {
            var existingPlayer = _server.ConnectedPlayers.Values.FirstOrDefault(p => p.CharacterId == playerInfo.CharacterId);
            if (existingPlayer != null)
            {
                Console.WriteLine($"[Conexão Duplicada] Personagem {playerInfo.CharacterId} já estava online. Desconectando a sessão antiga.");
                SendMessageToPlayer(existingPlayer, "FATAL_ERROR|Sua conta foi conectada de outro local.");

                // Desconecta o jogador antigo usando o ConnectionGuid dele.
                await _server.DisconnectPlayer(existingPlayer.ConnectionGuid, "Conectado de outra localidade.");
            }

            Console.WriteLine($"[Conexão] Autenticado: {playerInfo.Username} | Perm: {playerInfo.PermissionLevel}, Personagem: {playerInfo.CharacterName}...");

            CharacterData characterData = await _characterDb.LoadOrCreateAsync(playerInfo);

            // Passa o connectionGuid para o construtor do Player
            var newPlayer = new Player(connectionGuid, clientEndPoint, playerInfo, _server, characterData);

            newPlayer.IsPendingInitialization = true;
            newPlayer.LastMessageTime = _server.CurrentTimeUtc;

            // Usa o connectionGuid como chave
            if (_server.ConnectedPlayers.TryAdd(connectionGuid, newPlayer))
            {
                _server.PlayersBySessionId.TryAdd(newPlayer.SessionId, newPlayer);
                OnlineStatusManager.SetOnline(newPlayer.CharacterId);

                newPlayer.FinalizeInitialization();

                Console.WriteLine($"[Network] Jogador '{newPlayer.Username}' inicializado. Enviando estado para o cliente...");

                string assignIdMessage = $"ASSIGN_ID|{newPlayer.CharacterId}|{newPlayer.SessionId}";
                SendImmediateMessageToEndpoint(assignIdMessage, clientEndPoint);

                string spawnMessage = newPlayer.GetSpawnMessage();
                SendImmediateMessageToEndpoint(spawnMessage, clientEndPoint);

                SendFullStateToPlayer(newPlayer);

                if (characterData.GeneratedItems.Any())
                {
                    foreach (var pair in characterData.GeneratedItems)
                    {
                        SendItemInstanceData(newPlayer, pair.Key, pair.Value);
                    }
                }

                _server.InterestManager.OnPlayerEnteredWorld(newPlayer);

                Console.WriteLine($"[Network] Estado inicial completo enviado para '{newPlayer.Username}'.");
            }
        }
        else
        {
            SendImmediateMessageToEndpoint("ERROR|Token de acesso inválido ou expirado.", clientEndPoint);
        }
    }

    // Em Managers/NetworkManager.cs

    private void HandleOpenShopRequest(Player player, string npcId)
    {
        if (!_server.ActiveNpcs.TryGetValue(npcId, out var npcInstance) ||
            !DataManager.Vendors.TryGetValue(npcInstance.BaseData.TypeId, out var vendorData))
        {
            _server.NetworkManager.SendMessageToPlayer(player, "ERROR|Este NPC não é um vendedor.");
            return;
        }

        // <<< A NOVA LÓGICA ESTÁ AQUI >>>

        // 1. Cria uma nova lista para os itens com preços calculados.
        var itemsForClient = new List<VendorItemForClient>();

        // 2. Itera sobre os itens que o vendedor tem.
        foreach (var vendorItem in vendorData.Items)
        {
            if (DataManager.Items.TryGetValue(vendorItem.ItemID, out var itemTemplate))
            {
                int finalPrice;
                // Se for um equipamento, calcula o preço dinâmico.
                if (itemTemplate is ServerEquipmentData eqTemplate)
                {
                    finalPrice = ServerStatAllocator.CalculateBuyPrice(eqTemplate, player.Level);
                }
                else // Senão, usa o preço fixo.
                {
                    finalPrice = vendorItem.BuyPrice;
                }
                itemsForClient.Add(new VendorItemForClient
                {
                    ItemID = vendorItem.ItemID,
                    BuyPrice = finalPrice // << RENOMEADO para BuyPrice
                });
            }
        }

        // 3. Serializa a NOVA lista e a envia para o cliente.
        string vendorPayloadJson = JsonConvert.SerializeObject(itemsForClient);
        _server.NetworkManager.SendMessageToPlayer(player, $"OPEN_SHOP_WINDOW|{npcId}|{vendorPayloadJson}");
        Console.WriteLine($"[Loja] Jogador {player.Username} abriu a loja do NPC {npcId} com preços dinâmicos.");
    }

    public async Task HandlePlayerQuitting(string connectionGuid, Player player)
    {
        Console.WriteLine($"Jogador {player.Username} informou que está desconectando.");

        CharacterData dataToSave = player.GetCharacterDataForSaving();
        await _characterDb.SaveAsync(dataToSave);

        // A remoção agora é feita pelo ConnectionGuid, não mais pelo clientKey (EndPoint)
        if (_server.ConnectedPlayers.TryRemove(connectionGuid, out _))
        {
            _server.PlayersBySessionId.TryRemove(player.SessionId, out _);
            _server.GridManager.RemoveEntity(player);
            OnlineStatusManager.SetOffline(player.CharacterId);
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
        // Console.WriteLine($"[ActionBar] Barra de ações do jogador '{player.Username}' foi atualizada no servidor.");
    }

    /// <summary>
    /// Envia os stats gerados de uma instância de item específica para o jogador.
    /// Formato: ITEM_INSTANCE_DATA|InstanceID|StatType1,Value1;StatType2,Value2;...
    /// </summary>
    public void SendItemInstanceData(Player player, string instanceId, ItemInstanceData data)
    {
        if (data == null) return;

        // Serializa o objeto inteiro em JSON. É a forma mais fácil e flexível.
        string dataJson = JsonConvert.SerializeObject(data);

        string message = $"ITEM_INSTANCE_DATA|{instanceId}|{dataJson}";

        SendMessageToPlayer(player, message);
    }

    private void HandlePositionRotation(string[] parts, Player player)
    {
        if (parts.Length < 7) return;

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
        var candidateEntities = _server.GridManager.GetEntitiesInRadius(centerPosition, visibilityRange);
        var playersToSendTo = candidateEntities.OfType<Player>();

        foreach (var player in playersToSendTo)
        {
            if (excludePlayer != null && player.Id == excludePlayer.Id)
            {
                continue;
            }

            // A verificação `ContainsKey` agora usa o ConnectionGuid do jogador.
            if (_server.ConnectedPlayers.ContainsKey(player.ConnectionGuid))
            {
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
        if (player == null)
        {
            Console.WriteLine($"Player não encontrado por: {message}");
            return;
        }

        byte[] data = Encoding.UTF8.GetBytes(message);
        player.EnqueueMessage(data);
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