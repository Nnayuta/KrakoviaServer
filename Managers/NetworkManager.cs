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
    // <<< MUDANÇA 1: Adicionamos um campo para a nossa interface de banco de dados de personagens.
    private readonly ICharacterDatabase _characterDb;

    public NetworkManager(UDPServer server, UdpClient udpListener, ICharacterDatabase characterDatabase)
    {
        _server = server;
        _udpListener = udpListener;
        _characterDb = characterDatabase; // Armazenamos a referência.
    }


    /// <summary>
    /// Escuta por mensagens de jogadores de forma assíncrona até que o cancelamento seja solicitado.
    /// </summary>
    /// <param name="cancellationToken">O token para monitorar solicitações de cancelamento.</param>
    public async Task ListenForPlayerMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpListener.ReceiveAsync(cancellationToken);
                // <<< MUDANÇA 3: Chamamos o handler de forma assíncrona, mas não esperamos por ele (fire-and-forget)
                // para que o loop de escuta não seja bloqueado por um único processamento de mensagem.
                _ = HandlePlayerMessageAsync(result.Buffer, result.RemoteEndPoint);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[NetworkManager] Listener loop cancelled for shutdown.");
                break;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionReset) { /* Ignorar */ }
            catch (Exception e)
            {
                Console.WriteLine($"[NetworkManager] Erro inesperado ao receber mensagem: {e.Message}");
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
    }

    public void SendFullQuestLog(Player player)
    {
        // A nova estrutura `AllQuests` já contém tudo que precisamos.
        // Nós apenas pegamos todos os valores e os enviamos.
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
        var message = Encoding.ASCII.GetString(buffer).TrimEnd('\0');
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
            }

            if (messageToBroadcast != null)
            {
                BroadcastMessage(messageToBroadcast, player.SessionId);
            }
        }
    }

    private async Task HandleNewPlayerConnection(string clientKey, IPEndPoint clientEndPoint, string token)
    {
        if (_server.ConnectedPlayers.ContainsKey(clientKey)) return;

        if (AuthTokenManager.IsTokenValid(token, out AuthenticatedPlayerInfo? playerInfo) && playerInfo != null)
        {
            var existingPlayer = _server.ConnectedPlayers.Values.FirstOrDefault(p => p.Id == playerInfo.CharacterId);
            if (existingPlayer != null)
            {
                Console.WriteLine($"[Conexão Duplicada] Personagem {playerInfo.CharacterId} já estava online. Desconectando a sessão antiga.");
                SendMessageToClient("FATAL_ERROR|Sua conta foi conectada de outro local.", existingPlayer.EndPoint);
                _server.DisconnectPlayer(existingPlayer.EndPoint.ToString());
            }

            Console.WriteLine($"[Conexão] Autenticado: {playerInfo.Username} | Perm: {playerInfo.PermissionLevel}, Personagem: {playerInfo.CharacterName} (Classe: {playerInfo.ClassID}, Nível: {playerInfo.Level})");

            CharacterData characterData = await _characterDb.LoadOrCreateAsync(playerInfo);
            var newPlayer = new Player(clientEndPoint, playerInfo, _server, characterData);

            if (_server.ConnectedPlayers.TryAdd(clientKey, newPlayer))
            {
                Console.WriteLine($"Novo jogador ({newPlayer.SessionId}) conectado de {clientEndPoint}. Total: {_server.ConnectedPlayers.Count}");
                OnlineStatusManager.SetOnline(newPlayer.Id);

                // 1. Mensagem de atribuição de ID (necessária para o cliente saber "quem ele é").
                string assignIdMessage = $"ASSIGN_ID|{newPlayer.Id}";
                SendMessageToClient(assignIdMessage, clientEndPoint);

                // 2. A mensagem de spawn completa, para o jogador renderizar a si mesmo.
                string spawnMessage = newPlayer.GetSpawnMessage();
                SendMessageToClient(spawnMessage, clientEndPoint);

                _server.InterestManager.OnPlayerConnected(newPlayer);
            }
        }
        else
        {
            SendMessageToClient("ERROR|Token de acesso inválido ou expirado.", clientEndPoint);
        }
    }

    public async Task HandlePlayerQuitting(string clientKey, Player player)
    {
        Console.WriteLine($"Jogador {player.Username} informou que está desconectando.");

        // 1. Pega a "foto" atual dos dados do jogador.
        CharacterData dataToSave = player.GetCharacterDataForSaving();

        // 2. Manda a base de dados (seja em memória ou MariaDB) salvar essa "foto".
        await _characterDb.SaveAsync(dataToSave);

        // 3. Continua com a lógica de remover o jogador do servidor.
        if (_server.ConnectedPlayers.TryRemove(clientKey, out _))
        {
            OnlineStatusManager.SetOffline(player.Id);
            BroadcastMessage($"PLAYER_LEFT|{player.Id}", player.Id);
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

        // Atualiza o estado do jogador no servidor
        player.State.Position = $"{parts[1]},{parts[2]},{parts[3]}";
        player.State.RotationY = parts[4];
        player.State.VelocityX = parts[5];
        player.State.VelocityY = parts[6];

        string messageToBroadcast = $"{parts[0]}|{player.Id}|{string.Join("|", parts.Skip(1))}";
        BroadcastMessage(messageToBroadcast, player.Id);
    }


    public void BroadcastMessageToAll(string message)
    {
        byte[] data = Encoding.ASCII.GetBytes(message);
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
        byte[] data = Encoding.ASCII.GetBytes(message);
        foreach (var player in _server.ConnectedPlayers.Values.Where(p => p.SessionId != senderSessionId))
        {
            _udpListener.Send(data, data.Length, player.EndPoint);
        }
    }

    public void SendMessageToClient(string message, IPEndPoint endPoint)
    {
        byte[] data = Encoding.ASCII.GetBytes(message);
        _udpListener.Send(data, data.Length, endPoint);
    }
}