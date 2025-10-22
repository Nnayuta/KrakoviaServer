using System.Globalization;
using System.Numerics;

public class GatherableManager
{
    private readonly UDPServer _server;
    private readonly Random _random = new Random();

    // Armazena o estado de TODOS os itens coletáveis do mundo
    public readonly Dictionary<string, GatherableInstance> ActiveGatherables = new Dictionary<string, GatherableInstance>();

    public GatherableManager(UDPServer server)
    {
        _server = server;
    }

    private float _gatherableUpdateTimer = 0f;
    private const float GATHERABLE_UPDATE_INTERVAL = 5.0f; // 5 segundos

    public void Update()
    {
        _gatherableUpdateTimer += (float)UDPServer.SERVER_TICK_RATE_MS / 1000.0f;

        if (_gatherableUpdateTimer >= GATHERABLE_UPDATE_INTERVAL)
        {
            _gatherableUpdateTimer = 0f;
            CheckForRespawns();
        }
    }

    // Chamado na inicialização do servidor
    public void InitializeSpawns()
    {
        Console.WriteLine("[GatherableManager] Inicializando e populando o mundo com itens coletáveis...");
        foreach (var spawnPoint in DataManager.GatherableSpawnPoints)
        {
            if (DataManager.Gatherables.TryGetValue(spawnPoint.GatherableTypeID, out var data))
            {
                var position = CalculateSpawnPosition(spawnPoint);
                // Converte a rotação de Euler (Vector3) para Quaternion
                var rotation = Quaternion.CreateFromYawPitchRoll(spawnPoint.Rotation.Y * (float)(Math.PI / 180), spawnPoint.Rotation.X * (float)(Math.PI / 180), spawnPoint.Rotation.Z * (float)(Math.PI / 180));

                var instance = new GatherableInstance(data, position, rotation);
                ActiveGatherables.Add(instance.InstanceId, instance);
                // (Opcional, mas recomendado) Adiciona à grade espacial
                _server.GridManager.UpdateEntity(instance);
            }
        }
        Console.WriteLine($"[GatherableManager] Mundo populado com {ActiveGatherables.Count} itens coletáveis.");
    }

    private void CheckForRespawns()
    {
        var itemsToRespawn = ActiveGatherables.Values.Where(g => g.IsDepleted && _server.CurrentTimeUtc >= g.RespawnTime).ToList();
        if (!itemsToRespawn.Any()) return;

        foreach (var item in itemsToRespawn)
        {
            item.IsDepleted = false;
            item.RespawnTime = DateTime.MinValue;

            _server.GridManager.UpdateEntity(item);

            // Envia a mensagem de spawn para os clientes próximos
            var eulerAngles = _server.NetworkManager.ToEulerAngles(item.Rotation);
            string message = $"SPAWN_GATHERABLE|{item.InstanceId}|{item.BaseData.ID}|{item.Position.X.ToString(CultureInfo.InvariantCulture)},{item.Position.Y.ToString(CultureInfo.InvariantCulture)},{item.Position.Z.ToString(CultureInfo.InvariantCulture)}|{eulerAngles.X.ToString(CultureInfo.InvariantCulture)},{eulerAngles.Y.ToString(CultureInfo.InvariantCulture)},{eulerAngles.Z.ToString(CultureInfo.InvariantCulture)}";
            _server.NetworkManager.BroadcastMessageToRelevantPlayers(item.Position, message);
        }
    }

    // Chamado quando o jogador envia uma mensagem "quero coletar"
    public void OnPlayerAttemptGather(Player player, string instanceId)
    {
        if (!ActiveGatherables.TryGetValue(instanceId, out var item) || item.IsDepleted)
        {
            _server.NetworkManager.SendMessageToPlayer(player, "GATHER_FAILED|Já foi coletado.");
            return;
        }

        if (Vector3.Distance(player.Position, item.Position) > 5.0f)
        {
            _server.NetworkManager.SendMessageToPlayer(player, "GATHER_FAILED|Muito longe.");
            return;
        }

        // Se o jogador já estiver coletando algo, não permite iniciar outra.
        if (player.CurrentGatheringTokenSource != null)
        {
            _server.NetworkManager.SendMessageToPlayer(player, "GATHER_FAILED|Você já está ocupado.");
            return;
        }

        // --- INÍCIO DA LÓGICA REFEITA ---

        // 1. Cria um CancellationToken para esta ação específica de coleta
        player.CurrentGatheringTokenSource = new CancellationTokenSource();
        var cancellationToken = player.CurrentGatheringTokenSource.Token;

        // 2. Inicia o processo de coleta em uma nova Task
        Task.Run(async () =>
        {
            try
            {
                // Armazena a posição inicial do jogador
                Vector3 startPosition = player.Position;
                float gatherTime = item.BaseData.GatherTimeSeconds;
                float timer = 0f;
                const float checkInterval = 0.25f; // Verifica a posição 4x por segundo

                while (timer < gatherTime)
                {
                    // Lança uma exceção se o token for cancelado
                    cancellationToken.ThrowIfCancellationRequested();

                    // Verifica se o jogador se moveu muito longe
                    if (Vector3.Distance(player.Position, startPosition) > 1.0f)
                    {
                        throw new TaskCanceledException("O jogador se moveu.");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(checkInterval), cancellationToken);
                    timer += checkInterval;
                }

                // --- COLETA BEM-SUCEDIDA ---

                // Marca o item como esgotado no servidor
                item.IsDepleted = true;
                item.RespawnTime = _server.CurrentTimeUtc.AddSeconds(item.BaseData.RespawnTimeSeconds);

                // Concede o loot
                /// TODO: VOLTA AQUI
                var lootItems = _server.LootManager.GenerateLootForNpc(item.BaseData.LootTableID, 0);
                _server.PlayerInventoryManager.GrantLootToPlayer(player, lootItems);

                _server.NetworkManager.SendMessageToPlayer(player, "GATHER_COMPLETE");
                _server.GridManager.RemoveEntity(item);
                string message = $"DESTROY_GATHERABLE|{item.InstanceId}";
                _server.NetworkManager.BroadcastMessageToRelevantPlayers(item.Position, message);
            }
            catch (OperationCanceledException)
            {
                // --- COLETA CANCELADA (PELO JOGADOR OU MOVIMENTO) ---
                _server.NetworkManager.SendMessageToPlayer(player, "GATHER_FAILED|Acao cancelada.");
            }
            finally
            {
                // --- LIMPEZA ---
                // Garante que o token de coleta seja limpo, permitindo que o jogador tente novamente.
                player.InterruptGathering();
            }
        }, cancellationToken);

        // Informa ao cliente para iniciar a barra de progresso (isso acontece imediatamente)
        _server.NetworkManager.SendMessageToPlayer(player, $"GATHER_STARTED|{item.BaseData.GatherTimeSeconds}");
    }

    private Vector3 CalculateSpawnPosition(GatherableSpawnPoint spawnPoint)
    {
        Vector3 spawnPosition = spawnPoint.Position;
        if (spawnPoint.SpawnRadius > 0f)
        {
            double angle = _random.NextDouble() * 2 * Math.PI;
            double radius = Math.Sqrt(_random.NextDouble()) * spawnPoint.SpawnRadius;
            spawnPosition += new Vector3((float)(Math.Cos(angle) * radius), 0, (float)(Math.Sin(angle) * radius));
        }
        return spawnPosition;
    }
}