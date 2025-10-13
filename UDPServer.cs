// Servidor/UDPServer.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class UDPServer
{
    public static UDPServer? Instance { get; private set; }

    public DateTime CurrentTimeUtc { get; private set; }

    // Estado Central do Servidor
    public readonly ConcurrentDictionary<string, Player> ConnectedPlayers = new();
    public readonly ConcurrentDictionary<string, NpcInstance> ActiveNpcs = new();
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
    private readonly ICharacterDatabase _characterDb;
    private readonly UdpClient _udpListener;
    private const int TIMEOUT_SECONDS = 15;

    private int _nextSessionId = 0;

    public UDPServer(int port, ICharacterDatabase characterDatabase)
    {
        Instance = this;

        _characterDb = characterDatabase;
        _udpListener = new UdpClient(port);
        DataManager.LoadAllData();

        // Instancia todos os managers
        NetworkManager = new NetworkManager(this, _udpListener, _characterDb);
        WorldManager = new WorldManager(this);
        NpcAiManager = new NpcAiManager(this);
        InterestManager = new InterestManager(this);
        CombatManager = new CombatManager(this);
        CommandManager = new CommandManager(this);
        PlayerEquipmentManager = new PlayerEquipmentManager(this);
        PlayerInventoryManager = new PlayerInventoryManager(this);
        PlayerLifecycleManager = new PlayerLifecycleManager(this);
        PlayerProgressionManager = new PlayerProgressionManager(this);
        QuestManager = new QuestManager(this);
        LootManager = new LootManager(this);
        Scheduler = new Scheduler(this);
        GridManager = new SpatialGridManager();
        this.GatherableManager = new GatherableManager(this); // (NOVO) Instancia o manager
    }

    public int GetNextSessionId()
    {
        // Interlocked.Increment é uma operação atômica, o que significa que
        // mesmo que múltiplos jogadores conectem ao mesmo tempo, nunca teremos IDs duplicados.
        return Interlocked.Increment(ref _nextSessionId);
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
        Task aiTask = NpcAiManager.NpcAI_LoopAsync(cancellationToken);

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
            aiTask,
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
            try
            {
                // Atualiza o tempo em uma frequência alta (ex: 20 vezes por segundo)
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

            var timedOutPlayers = ConnectedPlayers
                    .Where(p => (DateTime.UtcNow - p.Value.LastMessageTime).TotalSeconds > TIMEOUT_SECONDS)
                    .ToList();

            foreach (var playerEntry in timedOutPlayers)
            {
                DisconnectPlayer(playerEntry.Key);
            }
        }
    }

    /// <summary>
    /// Desconecta um jogador do servidor, removendo-o da lista de conectados
    /// e notificando os outros jogadores e sistemas (como o OnlineStatusManager).
    /// </summary>
    /// <param name="clientKey">A chave do jogador no dicionário (geralmente EndPoint.ToString()).</param>
    public void DisconnectPlayer(string clientKey)
    {
        // Tenta remover o jogador do dicionário principal.
        if (ConnectedPlayers.TryRemove(clientKey, out Player? disconnectedPlayer))
        {
            if (disconnectedPlayer != null)
            {
                Console.WriteLine($"Jogador {disconnectedPlayer.Username} (ID: {disconnectedPlayer.Id}) foi desconectado.");

                // 1. Notifica o OnlineStatusManager que o personagem está offline.
                OnlineStatusManager.SetOffline(disconnectedPlayer.Id);

                // 2. Notifica todos os outros jogadores que este personagem saiu do mundo.
                // Usamos o NetworkManager para isso, que já tem a lógica de broadcast.
                NetworkManager.BroadcastMessage($"PLAYER_LEFT|{disconnectedPlayer.Id}", disconnectedPlayer.Id);
            }
        }
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
        var players = ConnectedPlayers.Values.ToList();

        // (NOVO) Tenta encontrar por ID de Sessão primeiro (o mais comum para ADMs)
        if (int.TryParse(identifier, out int sessionId))
        {
            var playerBySessionId = players.FirstOrDefault(p => p.SessionId == sessionId);
            if (playerBySessionId != null)
            {
                return playerBySessionId;
            }
        }

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

}