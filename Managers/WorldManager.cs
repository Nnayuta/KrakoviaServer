// Managers/WorldManager.cs
using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

public class WorldManager
{
    private readonly UDPServer _server;
    private readonly Random _random = new Random();

    private readonly object _spawnLock = new object();

    public WorldManager(UDPServer server)
    {
        _server = server;
    }

    public async Task WorldManagement_LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(5000, cancellationToken);

                CleanupExpiredCorpses();
                CheckForRespawns();
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[WorldManager] Loop de gerenciamento do mundo cancelado para shutdown.");
                break;
            }
        }
    }

    public void ProcessNpcDeath(NpcInstance npc, ICombatEntity? killer)
    {
        // 1. Define o estado do NPC como morto e inativo
        npc.ChangeNpcState(NpcAiState.Dead, _server.CurrentTimeUtc); // Método auxiliar para mudar o estado
        npc.IsActive = false;
        npc.SetCorpseDespawnTimer(120.0f, _server.CurrentTimeUtc);

        // 2. Processa recompensas de XP
        Player? creditPlayer = _server.NpcAiManager.GetCreditPlayer(npc, killer); // Reutiliza o método de busca
        if (creditPlayer != null && npc.BaseData.ExperienceReward > 0)
        {
            _server.PlayerProgressionManager.GrantExperience(creditPlayer, npc.BaseData.ExperienceReward);
        }

        // 3. Processa recompensas de Loot
        if (!string.IsNullOrEmpty(npc.BaseData.LootTableID))
        {
            List<ItemStack> generatedLoot = _server.LootManager.GenerateLootForNpc(npc.BaseData.LootTableID);
            npc.SetLoot(generatedLoot);
        }

        // 4. Move o NPC da lista de vivos para a lista de mortos
        if (_server.ActiveNpcs.TryRemove(npc.InstanceId, out _))
        {
            _server.DeadNpcCor_pses.TryAdd(npc.InstanceId, npc);
        }

        if (killer is Player killerPlayer)
        {
            _server.QuestManager.OnEntitySlain(killerPlayer, npc);
        }

        // 5. Notifica os clientes que o NPC morreu
        _server.NetworkManager.BroadcastMessageToAll($"ENTITY_DIED|{npc.Id}|{npc.HasLoot}");

        // 6. Agenda o RESPAWN (lógica antiga de OnNpcDied)
        lock (_spawnLock)
        {
            var spawnPoint = FindSpawnPointForNpc(npc.InstanceId);
            if (spawnPoint != null)
            {
                spawnPoint.ActiveNpcInstanceIds.Remove(npc.InstanceId);
                if (spawnPoint.RespawnEndTime <= _server.CurrentTimeUtc)
                {
                    spawnPoint.RespawnEndTime = _server.CurrentTimeUtc.AddSeconds(npc.BaseData.RespawnTimeSeconds);
                    Console.WriteLine($"[WorldManager] Respawn para {npc.BaseData.TypeId} agendado para {spawnPoint.RespawnEndTime}.");
                }
            }
        }
    }

    public void OnNpcGroupMemberDied(NpcInstance npc)
    {
        lock (_spawnLock)
        {
            var spawnPoint = FindSpawnPointForNpc(npc.InstanceId);
            if (spawnPoint != null)
            {
                // Apenas remove o ID da lista do spawn point.
                spawnPoint.ActiveNpcInstanceIds.Remove(npc.InstanceId);

                if (spawnPoint.RespawnEndTime <= _server.CurrentTimeUtc)
                {
                    spawnPoint.RespawnEndTime = _server.CurrentTimeUtc.AddSeconds(npc.BaseData.RespawnTimeSeconds);
                    Console.WriteLine($"[WorldManager] Respawn para o grupo {npc.BaseData.TypeId} agendado para {spawnPoint.RespawnEndTime}.");
                }
            }
        }
    }

    private void CleanupExpiredCorpses()
    {
        // Pega os IDs para evitar problemas ao modificar a coleção
        var expiredNpcIds = _server.DeadNpcCor_pses
                               .Where(kvp => kvp.Value.CorpseDespawnTime != DateTime.MinValue && _server.CurrentTimeUtc >= kvp.Value.CorpseDespawnTime)
                               .Select(kvp => kvp.Key)
                               .ToList();

        foreach (var npcId in expiredNpcIds)
        {
            if (_server.DeadNpcCor_pses.TryRemove(npcId, out NpcInstance npc))
            {
                Console.WriteLine($"[WorldManager] Corpo do NPC {npc.InstanceId} desapareceu (Tempo Atual: {_server.CurrentTimeUtc}, Hora de Despawn: {npc.CorpseDespawnTime}).");
                _server.NetworkManager.BroadcastMessageToAll($"DESTROY_NPC|{npc.InstanceId}");
            }
        }
    }

    private void CheckForRespawns()
    {
        // O loop de respawn já roda em uma única thread, então o acesso aqui é seguro.
        // No entanto, para consistência, vamos usar o lock aqui também.
        lock (_spawnLock)
        {
            var spawnsToRespawn = DataManager.SpawnPoints
                .Where(sp => sp.ActiveNpcInstanceIds.Count < sp.Quantity &&
                             sp.RespawnEndTime <= _server.CurrentTimeUtc && // Usa o tempo do servidor
                             sp.RespawnEndTime != DateTime.MinValue)
                .ToList();

            foreach (var spawnPoint in spawnsToRespawn)
            {
                if (DataManager.Npcs.TryGetValue(spawnPoint.NpcTypeId, out NpcData? npcData))
                {
                    Console.WriteLine($"[WorldManager] Ressurgindo um NPC para o grupo '{spawnPoint.NpcTypeId}'...");
                    Vector3 spawnPosition = CalculateSpawnPosition(spawnPoint);
                    SpawnSingleNpc(npcData, spawnPosition, spawnPoint);

                    if (spawnPoint.ActiveNpcInstanceIds.Count < spawnPoint.Quantity)
                    {
                        spawnPoint.RespawnEndTime = _server.CurrentTimeUtc.AddSeconds(npcData.RespawnTimeSeconds);
                    }
                    else
                    {
                        spawnPoint.RespawnEndTime = DateTime.MinValue;
                        Console.WriteLine($"[WorldManager] Grupo '{spawnPoint.NpcTypeId}' em {spawnPoint.Position} está completo.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Spawna todos os NPCs pela primeira vez quando o servidor inicia.
    /// </summary>
    public void InitializeSpawns()
    {
        Console.WriteLine("[WorldManager] Inicializando pontos de spawn...");
        // Esta inicialização acontece antes do loop de IA começar, então não precisa de lock.
        foreach (var spawnPoint in DataManager.SpawnPoints)
        {
            if (DataManager.Npcs.TryGetValue(spawnPoint.NpcTypeId, out NpcData? npcData))
            {
                for (int i = 0; i < spawnPoint.Quantity; i++)
                {
                    Vector3 spawnPosition = CalculateSpawnPosition(spawnPoint);
                    SpawnSingleNpc(npcData, spawnPosition, spawnPoint);
                }
            }
            else
            {
                Console.WriteLine($"[AVISO] Tipo de NPC '{spawnPoint.NpcTypeId}' em spawns.json não encontrado em npcs.json.");
            }
        }
        Console.WriteLine($"[WorldManager] {_server.ActiveNpcs.Count} NPCs instanciados.");
    }


    /// <summary>
    /// Cria uma nova instância de NPC, a ativa e notifica os clientes.
    /// </summary>
    private void SpawnSingleNpc(NpcData npcData, Vector3 position, SpawnPoint spawnPoint)
    {
        // Agora passamos os dados de comportamento do SpawnPoint para a nova instância do NPC
        var newNpc = new NpcInstance(
            position,
            spawnPoint.InitialRotation,
            spawnPoint.AiType,
            spawnPoint.PatrolPath, // Passando o caminho da patrulha também
            npcData,
            _server
        );

        _server.ActiveNpcs.TryAdd(newNpc.InstanceId, newNpc);

        spawnPoint.ActiveNpcInstanceIds.Add(newNpc.InstanceId);
        newNpc.IsActive = true;

        string spawnMessage = newNpc.GetSpawnMessage();
        _server.NetworkManager.BroadcastMessageToAll(spawnMessage);
    }
    /// <summary>
    /// Calcula uma posição de spawn aleatória dentro do raio do spawn point.
    /// </summary>
    private Vector3 CalculateSpawnPosition(SpawnPoint spawnPoint)
    {
        Vector3 spawnPosition = spawnPoint.Position;
        if (spawnPoint.Quantity > 1 && spawnPoint.SpawnRadius > 0f)
        {
            double angle = _random.NextDouble() * 2 * Math.PI;
            double radius = Math.Sqrt(_random.NextDouble()) * spawnPoint.SpawnRadius;
            spawnPosition += new Vector3((float)(Math.Cos(angle) * radius), 0, (float)(Math.Sin(angle) * radius));
        }
        return spawnPosition;
    }

    public SpawnPoint? FindSpawnPointForNpc(string npcInstanceId)
    {
        return DataManager.SpawnPoints.FirstOrDefault(sp => sp.ActiveNpcInstanceIds.Contains(npcInstanceId));
    }
}