// Servidor/UDPServer.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class UDPServer
{
    public static UDPServer? Instance { get; private set; }

    public DateTime CurrentTimeUtc { get; private set; }

    // Estado Central do Servidor
    public readonly ConcurrentDictionary<string, Player> ConnectedPlayers = new();
    public readonly ConcurrentDictionary<int, Player> PlayersBySessionId = new();


    public readonly ConcurrentDictionary<string, NpcInstance> ActiveNpcs = new();
    public readonly ConcurrentDictionary<int, NpcInstance> NpcsBySessionId = new();
    public readonly ConcurrentDictionary<string, NpcInstance> DeadNpcCor_pses = new();

    // Managers
    public readonly CommandManager CommandManager;
    public readonly NetworkManager NetworkManager;
    public readonly WorldManager WorldManager;
    public readonly NpcAiManager NpcAiManager;
    public readonly CombatManager CombatManager;
    public readonly PlayerEquipmentManager PlayerEquipmentManager;
    public readonly PlayerInventoryManager PlayerInventoryManager;
    public readonly PlayerLifecycleManager PlayerLifecycleManager;
    public readonly PlayerProgressionManager PlayerProgressionManager;
    public readonly QuestManager QuestManager;
    public readonly LootManager LootManager;
    public readonly InterestManager InterestManager;
    public readonly Scheduler Scheduler;
    public readonly SpatialGridManager GridManager;
    public readonly GatherableManager GatherableManager;
    public readonly ChatManager ChatManager;
    public readonly ItemInstanceManager ItemInstanceManager;


    private readonly ICharacterDatabase _characterDb;
    private readonly UdpClient _udpListener;
    private const int TIMEOUT_SECONDS = 30;

    private int _nextSessionId = 0;
    private int _nextNpcSessionId = 0;

    public UDPServer(int port, ICharacterDatabase characterDatabase)
    {
        Instance = this;

        this._characterDb = characterDatabase;
        IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Any, port);
        this._udpListener = new UdpClient(serverEndPoint);
        DataManager.LoadAllData();

        // Instancia todos os managers
        this.NetworkManager = new NetworkManager(this, _udpListener, _characterDb);
        this.WorldManager = new WorldManager(this);
        this.NpcAiManager = new NpcAiManager(this);
        this.InterestManager = new InterestManager(this);
        this.CombatManager = new CombatManager(this);
        this.PlayerEquipmentManager = new PlayerEquipmentManager(this);
        this.PlayerInventoryManager = new PlayerInventoryManager(this);
        this.PlayerLifecycleManager = new PlayerLifecycleManager(this);
        this.PlayerProgressionManager = new PlayerProgressionManager(this);
        this.QuestManager = new QuestManager(this);
        this.LootManager = new LootManager(this);
        this.Scheduler = new Scheduler(this);
        this.GridManager = new SpatialGridManager();
        this.GatherableManager = new GatherableManager(this);
        this.ItemInstanceManager = new ItemInstanceManager(this);

        this.CommandManager = new CommandManager(this);
        this.ChatManager = new ChatManager(this, this.CommandManager);
    }

    public int GetNextSessionId()
    {
        return Interlocked.Increment(ref _nextSessionId);
    }

    public int GetNextNpcSessionId()
    {
        return Interlocked.Increment(ref _nextNpcSessionId);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Servidor [WORLD] iniciado na porta {_udpListener.Client.LocalEndPoint}.");
        WorldManager.InitializeSpawns();
        GatherableManager.InitializeSpawns();


        Console.WriteLine("STARTING NETWORK MANAGER");
        Task listenTask = NetworkManager.ListenForPlayerMessagesAsync(cancellationToken);

        Console.WriteLine("STARTING WORLD MANAGER");
        Task worldTask = WorldManager.WorldManagement_LoopAsync(cancellationToken);

        Console.WriteLine("STARTING NPCAI MANAGER");
        Task fastAiTask = NpcAiManager.NpcAI_FastLoopAsync(cancellationToken);
        Task slowAiTask = NpcAiManager.NpcAI_SlowLoopAsync(cancellationToken);

        Console.WriteLine("STARTING INTEREST MANAGER");
        Task interestAndActivationTask = InterestManager.UpdateInterestAndActivationAsync(cancellationToken);

        Console.WriteLine("STARTING GATHERABLE MANAGER");
        Task gatherableTask = GatherableManager.GatherableLoopAsync(cancellationToken);

        Console.WriteLine("STARTING Scheduler");
        Task schedulerTask = Scheduler.RunAsync(cancellationToken);

        Console.WriteLine("STARTING PLAYER LIFECYCLE MANAGER");
        Task playerLifecycleTask = PlayerLifecycleManager.Action_LoopAsync(cancellationToken);

        Task autoSaveTask = PeriodicAutoSaveAsync(cancellationToken);

        Task timeoutTask = CheckForTimeoutsAsync(cancellationToken);
        Task timeUpdateTask = UpdateServerTimeAsync(cancellationToken);

        // Adiciona a nova tarefa 'playerLifecycleTask' ao WhenAll para que ela
        // seja gerenciada pelo CancellationToken junto com as outras.
        await Task.WhenAll(
            autoSaveTask,
            timeUpdateTask,
            listenTask,
            worldTask,
            fastAiTask,
            slowAiTask,
            interestAndActivationTask,
            playerLifecycleTask,
            schedulerTask,
            timeoutTask,
            gatherableTask
        );
    }

    public void Stop()
    {
        Console.WriteLine("[SERVER] Parando serviços...");
        _udpListener.Close();
    }

    public IWorldEntity? GetWorldEntityById(string id)
    {
        var player = ConnectedPlayers.Values.FirstOrDefault(p => p.Id == id);
        if (player != null) return player;

        if (ActiveNpcs.TryGetValue(id, out var npc)) return npc;
        if (GatherableManager.ActiveGatherables.TryGetValue(id, out var gatherable)) return gatherable;
        return null;
    }

    private async Task UpdateServerTimeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            CurrentTimeUtc = DateTime.UtcNow;

            var players = ConnectedPlayers.Values.ToList();
            foreach (var player in players)
            {
                player.StatusEffectController.Update();
            }

            var npcs = ActiveNpcs.Values.ToList();
            foreach (var npc in npcs)
            {
                npc.StatusEffectController.Update();
            }

            NetworkManager.DispatchQueuedMessages();

            try
            {
                // Atualiza o tempo e despacha mensagens a cada 50ms (20hz)
                await Task.Delay(50, cancellationToken);
            }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task CheckForTimeoutsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(5000, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            // A lógica de encontrar os jogadores timed out permanece a mesma
            var timedOutPlayers = ConnectedPlayers
                    .Where(p => (DateTime.UtcNow - p.Value.LastMessageTime).TotalSeconds > TIMEOUT_SECONDS)
                    .ToList();

            foreach (var playerEntry in timedOutPlayers)
            {
                // playerEntry.Key agora é o ConnectionGuid, que é o que DisconnectPlayer espera.
                // A chamada está correta!
                await DisconnectPlayer(playerEntry.Key, "Timeout");
            }
        }
    }

    /// <summary>
    /// Desconecta um jogador do servidor, removendo-o da lista de conectados
    /// e notificando os outros jogadores e sistemas (como o OnlineStatusManager).
    /// </summary>
    /// <param name="clientKey">A chave do jogador no dicionário (geralmente EndPoint.ToString()).</param>
    // EM UDPServer.cs

    /// <summary>
    /// Desconecta um jogador do servidor, SALVANDO SEUS DADOS, removendo-o da lista de conectados
    /// e notificando os outros jogadores e sistemas.
    /// </summary>
    public async Task DisconnectPlayer(string connectionGuid, string reason = "Conexão perdida.")
    {
        if (ConnectedPlayers.TryRemove(connectionGuid, out Player? disconnectedPlayer))
        {
            if (disconnectedPlayer != null)
            {
                // 1. SALVAR OS DADOS PRIMEIRO
                try
                {
                    var dataToSave = disconnectedPlayer.GetCharacterDataForSaving();
                    await _characterDb.SaveAsync(dataToSave);
                    Console.WriteLine($"[SAVE] Dados de {disconnectedPlayer.Username} salvos devido a desconexão ({reason}).");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SAVE-ERROR] Falha ao salvar dados de {disconnectedPlayer.Username} na desconexão: {ex.Message}");
                }

                // 2. CONTINUAR COM A LÓGICA DE DESCONEXÃO
                Console.WriteLine($"Jogador {disconnectedPlayer.Username} (ID Sessão: {disconnectedPlayer.SessionId}) foi desconectado. Motivo: {reason}");

                PlayersBySessionId.TryRemove(disconnectedPlayer.SessionId, out _);
                OnlineStatusManager.SetOffline(disconnectedPlayer.CharacterId);

                GridManager.RemoveEntity(disconnectedPlayer); // Boa prática adicionar a remoção do grid aqui também

                NetworkManager.BroadcastMessageToRelevantPlayers(disconnectedPlayer.Position, $"PLAYER_LEFT|{disconnectedPlayer.Id}");
                NetworkManager.SendMessageToPlayer(disconnectedPlayer, "FATAL_ERROR|Conexão Perdida");
            }
        }
    }

    /// <summary>
    /// Itera sobre todos os jogadores conectados e salva seus dados no banco de dados.
    /// Projetado para ser chamado durante o desligamento do servidor.
    /// </summary>
    public async Task SaveAllPlayersAsync()
    {
        Console.WriteLine($"[SHUTDOWN-SAVE] Iniciando salvamento final para {ConnectedPlayers.Count} jogador(es)...");

        // Cria uma lista de tarefas de salvamento, uma para cada jogador.
        var saveTasks = new List<Task>();

        // Pega uma cópia da lista de jogadores para iterar com segurança.
        var playersToSave = ConnectedPlayers.Values.ToList();

        foreach (var player in playersToSave)
        {
            // Adiciona a tarefa de salvamento à lista.
            // Não usamos 'await' aqui para que todos os salvamentos possam rodar em paralelo.
            saveTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var dataToSave = player.GetCharacterDataForSaving();
                    await _characterDb.SaveAsync(dataToSave);
                    Console.WriteLine($"[SHUTDOWN-SAVE] Dados de {player.Username} salvos com sucesso.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SHUTDOWN-SAVE-ERROR] Falha ao salvar {player.Username}: {ex.Message}");
                }
            }));
        }

        // Espera que TODAS as tarefas de salvamento terminem.
        await Task.WhenAll(saveTasks);

        Console.WriteLine("[SHUTDOWN-SAVE] Salvamento final de todos os jogadores concluído.");
    }

    private async Task PeriodicAutoSaveAsync(CancellationToken token)
    {
        // Salva a cada 5 minutos. Ajuste conforme necessário.
        var autoSaveInterval = TimeSpan.FromMinutes(5);

        Console.WriteLine($"[AUTOSAVE] Sistema de salvamento periódico iniciado. Intervalo: {autoSaveInterval.TotalMinutes} minutos.");

        while (!token.IsCancellationRequested)
        {
            try
            {
                // Espera pelo intervalo de tempo, mas pode ser cancelado pelo shutdown.
                await Task.Delay(autoSaveInterval, token);

                if (ConnectedPlayers.IsEmpty) continue;

                Console.WriteLine($"[AUTOSAVE] Iniciando salvamento periódico para {ConnectedPlayers.Count} jogador(es)...");

                // Cria uma cópia da lista de jogadores para iterar com segurança
                var playersToSave = ConnectedPlayers.Values.ToList();

                foreach (var player in playersToSave)
                {
                    try
                    {
                        var data = player.GetCharacterDataForSaving();
                        await _characterDb.SaveAsync(data);
                    }
                    catch (Exception ex)
                    {
                        // Se o salvamento de um jogador falhar, apenas registra o erro e continua com os outros.
                        Console.WriteLine($"[AUTOSAVE-ERROR] Falha ao salvar o personagem {player.CharacterId}: {ex.Message}");
                    }
                }
                Console.WriteLine("[AUTOSAVE] Salvamento periódico concluído.");
            }
            catch (TaskCanceledException)
            {
                // Esperado no desligamento
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AUTOSAVE-FATAL] Erro crítico no loop de autosave: {ex.Message}");
            }
        }
        Console.WriteLine("[AUTOSAVE] Sistema de salvamento periódico encerrado.");
    }

    public Player? FindPlayerByNameOrId(string identifier)
    {

        // (NOVO) Tenta encontrar por ID de Sessão primeiro (o mais comum para ADMs)
        if (int.TryParse(identifier, out int sessionId))
        {
            return FindPlayerBySessionId(sessionId);
        }

        var players = ConnectedPlayers.Values.ToList();

        // Tenta encontrar por ID do Personagem (GUID)
        var playerById = players.FirstOrDefault(p => p.CharacterId.Equals(identifier, StringComparison.OrdinalIgnoreCase));
        if (playerById != null)
        {
            return playerById;
        }

        // Por último, tenta encontrar por Nome do Personagem
        var playerByName = players.FirstOrDefault(p => p.CharacterName.Equals(identifier, StringComparison.OrdinalIgnoreCase));
        return playerByName;
    }

    public Player? FindPlayerBySessionId(int sessionId)
    {
        PlayersBySessionId.TryGetValue(sessionId, out var player);
        return player;
    }

    /// <summary>
    /// Encontra um jogador conectado pelo seu ID de sessão (string).
    /// </summary>
    /// <param name="playerId">O ID de sessão do jogador a ser encontrado.</param>
    /// <returns>O objeto Player se encontrado; caso contrário, null.</returns>
    public Player? GetPlayerById(string playerId)
    {
        if (string.IsNullOrEmpty(playerId) || playerId.ToLower() == "null")
        {
            return null;
        }

        // Esta busca assume que seu dicionário ConnectedPlayers usa o EndPoint como chave
        // e que a propriedade Player.Id retorna o SessionId como string.
        return ConnectedPlayers.Values.FirstOrDefault(p => p.Id == playerId);
    }

}