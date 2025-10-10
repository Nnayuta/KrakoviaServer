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

    public Player? GetCreditPlayer(NpcInstance npc, ICombatEntity? killer)
    {
        if (npc.ThreatTable.Any())
        {
            var topThreatPlayerId = npc.ThreatTable.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key;
            if (_server.ConnectedPlayers.TryGetValue(topThreatPlayerId, out var playerFromThreat))
            {
                return playerFromThreat;
            }
        }
        if (killer is Player killerPlayer)
        {
            return killerPlayer;
        }
        return null;
    }

    public void ProcessNpcDeath(NpcInstance npc, ICombatEntity? killer)
    {
        // 1. Define o estado do NPC como morto e inativo
        npc.ChangeNpcState(NpcAiState.Dead, _server.CurrentTimeUtc); // Método auxiliar para mudar o estado
        npc.IsActive = false;
        npc.SetCorpseDespawnTimer(120.0f, _server.CurrentTimeUtc);

        Player? creditPlayer = this.GetCreditPlayer(npc, killer);
        if (creditPlayer != null)
        {
            if (npc.BaseData.ExperienceReward > 0)
            {
                _server.PlayerProgressionManager.GrantExperience(creditPlayer, npc.BaseData.ExperienceReward);
                _server.NetworkManager.SendMessageToClient($"SHOW_FEEDBACK|+{npc.BaseData.ExperienceReward} EXP", creditPlayer.EndPoint);
            }

            if (npc.BaseData.CurrencyReward > 0)
            {
                creditPlayer.TotalBronze += npc.BaseData.CurrencyReward;

                _server.NetworkManager.SendCurrencyUpdate(creditPlayer);
                _server.NetworkManager.SendMessageToClient($"SHOW_FEEDBACK|+{npc.BaseData.CurrencyReward} Moedas", creditPlayer.EndPoint);
            }
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

        string message = $"ENTITY_DIED|{npc.Id}|{npc.HasLoot}";
        _server.NetworkManager.BroadcastMessageToRelevantPlayers(npc.Position, message);

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
                // Console.WriteLine($"[WorldManager] Corpo do NPC {npc.InstanceId} desapareceu (Tempo Atual: {_server.CurrentTimeUtc}, Hora de Despawn: {npc.CorpseDespawnTime}).");
                _server.NetworkManager.BroadcastMessageToRelevantPlayers(npc.Position, $"DESTROY_NPC|{npc.InstanceId}");
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
    /// Invoca NPCs que desaparecem após um certo tempo. Perfeito para lacaios de chefes.
    /// </summary>
    public void SpawnTemporaryNpcs(string npcTypeId, Vector3 centerPosition, int quantity, float spawnRadius, float duration)
    {
        if (!DataManager.Npcs.TryGetValue(npcTypeId, out NpcData? npcData))
        {
            Console.WriteLine($"[AVISO] Tentativa de invocar NPC temporário com TypeId inválido: '{npcTypeId}'");
            return;
        }

        for (int i = 0; i < quantity; i++)
        {
            // Usa a lógica existente para calcular uma posição aleatória
            Vector3 spawnPosition = centerPosition;
            if (quantity > 1 && spawnRadius > 0f)
            {
                double angle = _random.NextDouble() * 2 * Math.PI;
                double radius = Math.Sqrt(_random.NextDouble()) * spawnRadius;
                spawnPosition += new Vector3((float)(Math.Cos(angle) * radius), 0, (float)(Math.Sin(angle) * radius));
            }

            // Usa a lógica de spawn existente
            var newNpc = SpawnSingleNpc(npcData, spawnPosition, null); // Passa null para spawnPoint pois não é de um ponto fixo

            if (duration > 0)
            {
                // Agenda a "morte" (despawn) do NPC
                _server.Scheduler.ScheduleTask(() =>
                {
                    if (newNpc != null && !newNpc.IsDead)
                    {
                        // Remove o NPC do mundo de forma limpa
                        if (_server.ActiveNpcs.TryRemove(newNpc.InstanceId, out _))
                        {
                            _server.NetworkManager.BroadcastMessageToRelevantPlayers(newNpc.Position, $"DESTROY_NPC|{newNpc.InstanceId}");
                        }
                    }
                }, TimeSpan.FromSeconds(duration));
            }
        }
    }

    public void CreateHazard(ICombatEntity source, AbilityData sourceAbility, Vector3 position, float radius, float duration, float tickRate, List<ServerAbilityEffectData> tickEffects)
    {
        string hazardId = Guid.NewGuid().ToString("N");

        string createMessage = $"CREATE_HAZARD|{hazardId}|{sourceAbility.ID}|{position.X.ToString(CultureInfo.InvariantCulture)},{position.Y.ToString(CultureInfo.InvariantCulture)},{position.Z.ToString(CultureInfo.InvariantCulture)}|{radius.ToString(CultureInfo.InvariantCulture)}|{duration.ToString(CultureInfo.InvariantCulture)}";
        _server.NetworkManager.BroadcastMessageToRelevantPlayers(position, createMessage);

        DateTime endTime = _server.CurrentTimeUtc.AddSeconds(duration);

        Action tickAction = null;
        tickAction = () =>
        {
            if (_server.CurrentTimeUtc < endTime)
            {
                foreach (var effect in tickEffects)
                {
                    var validTargets = _server.ConnectedPlayers.Values.Cast<ICombatEntity>()
                        .Concat(_server.ActiveNpcs.Values.Cast<ICombatEntity>())
                        .Where(p =>
                            !p.IsDead &&
                            Vector3.Distance(p.Position, position) <= radius &&
                            ((effect.Intent == AbilityIntent.Helpful && AreEntitiesFriendly(source, p)) ||
                             (effect.Intent == AbilityIntent.Harmful && !AreEntitiesFriendly(source, p)))
                        ).ToList();

                    foreach (var target in validTargets)
                    {
                        // (CORREÇÃO) Voltamos a chamar ApplySingleEffect.
                        // Isso aplica apenas o efeito do tick (ex: a cura), e não a habilidade inteira de novo.
                        _server.CombatManager.ApplySingleEffect(source, target, effect, sourceAbility);
                    }
                }

                _server.Scheduler.ScheduleTask(tickAction, TimeSpan.FromSeconds(tickRate));
            }
            else
            {
                _server.NetworkManager.BroadcastMessageToRelevantPlayers(position, $"DESTROY_HAZARD|{hazardId}");
            }
        };

        _server.Scheduler.ScheduleTask(tickAction, TimeSpan.FromSeconds(tickRate));
    }

    // (NOVO) Adicione este método auxiliar ao seu WorldManager.cs para evitar duplicação de código.
    private bool AreEntitiesFriendly(ICombatEntity entityA, ICombatEntity entityB)
    {
        if (entityA is Player && entityB is Player) return true;
        if (entityA is Player && entityB is NpcInstance npc) return npc.BaseData.Faction == NpcFaction.Friendly;
        if (entityA is NpcInstance npc2 && entityB is Player) return npc2.BaseData.Faction == NpcFaction.Friendly;
        if (entityA is NpcInstance n1 && entityB is NpcInstance n2) return n1.BaseData.Faction == n2.BaseData.Faction;
        return false;
    }

    /// <summary>
    /// (NOVO) Remove todos os NPCs ativos associados a um SpawnPoint específico.
    /// Chamado pelo InterestManager quando uma área fica vazia por muito tempo.
    /// </summary>
    public void DespawnNpcsForSpawnPoint(SpawnPoint spawnPoint)
    {
        // Usa ToList() para criar uma cópia, pois vamos modificar a coleção original
        var idsToDespawn = spawnPoint.ActiveNpcInstanceIds.ToList();

        foreach (var npcId in idsToDespawn)
        {
            if (_server.ActiveNpcs.TryRemove(npcId, out var npc))
            {
                // Notifica os jogadores próximos (se houver) que este NPC foi destruído
                _server.NetworkManager.BroadcastMessageToRelevantPlayers(npc.Position, $"DESTROY_NPC|{npc.InstanceId}");
            }
        }

        // Limpa a lista de controle do spawn point
        spawnPoint.ActiveNpcInstanceIds.Clear();
        // Reseta o timer de respawn para que ele não tente respawnar sozinho
        spawnPoint.RespawnEndTime = DateTime.MinValue;

        // Console.WriteLine($"[WorldManager] Área de spawn para '{spawnPoint.NpcTypeId}' em {spawnPoint.Position} foi desativada e limpa.");
    }

    /// <summary>
    /// (NOVO) Preenche um SpawnPoint com seus NPCs.
    /// Chamado pelo InterestManager quando um jogador entra em uma área inativa.
    /// </summary>
    public void RespawnNpcsForSpawnPoint(SpawnPoint spawnPoint)
    {
        // Se já existem NPCs, não faz nada.
        if (spawnPoint.ActiveNpcInstanceIds.Any())
        {
            return;
        }

        if (DataManager.Npcs.TryGetValue(spawnPoint.NpcTypeId, out NpcData? npcData))
        {
            Console.WriteLine($"[WorldManager] Ativando área de spawn e ressurgindo NPCs para '{spawnPoint.NpcTypeId}'...");
            for (int i = 0; i < spawnPoint.Quantity; i++)
            {
                Vector3 spawnPosition = CalculateSpawnPosition(spawnPoint);
                SpawnSingleNpc(npcData, spawnPosition, spawnPoint);
            }
        }
    }



    /// <summary>
    /// Spawna todos os NPCs pela primeira vez quando o servidor inicia.
    /// </summary>
    public void InitializeSpawns()
    {
        Console.WriteLine("[WorldManager] Inicializando pontos de spawn...");
        foreach (var spawnPoint in DataManager.SpawnPoints)
        {
            if (DataManager.Npcs.TryGetValue(spawnPoint.NpcTypeId, out NpcData? npcData))
            {
                for (int i = 0; i < spawnPoint.Quantity; i++)
                {
                    Vector3 spawnPosition = CalculateSpawnPosition(spawnPoint);
                    SpawnSingleNpc(npcData, spawnPosition, spawnPoint);
                }

                // (CORREÇÃO) Se populamos este ponto, ele deve começar como ativo.
                // Isso sincroniza o estado inicial com a realidade do mundo.
                if (spawnPoint.ActiveNpcInstanceIds.Any())
                {
                    spawnPoint.IsActive = true;
                    // Também é bom definir o tempo de observação para evitar um despawn imediato
                    // se nenhum jogador spawnar perto dele.
                    spawnPoint.LastObservedTime = _server.CurrentTimeUtc;
                }
            }
            else
            {
                Console.WriteLine($"[AVISO] Tipo de NPC '{spawnPoint.NpcTypeId}' em spawns.json não encontrado em npcs.json.");
            }
        }
        Console.WriteLine($"[WorldManager] {_server.ActiveNpcs.Count} NPCs instanciados.");
    }

    private NpcInstance SpawnSingleNpc(NpcData npcData, Vector3 position, SpawnPoint? spawnPoint)
    {
        var newNpc = new NpcInstance(
            position,
            spawnPoint?.InitialRotation ?? Vector3.Zero,
            spawnPoint?.AiType ?? NpcAiType.Wandering_Aggressive, // Usa um padrão se não houver spawn point
            spawnPoint?.PatrolPath,
            npcData,
            _server
        );

        newNpc.Behavior = _server.NpcAiManager.GetBehavior(newNpc.AiType);

        _server.ActiveNpcs.TryAdd(newNpc.InstanceId, newNpc);

        // Se houver um spawn point (não é um lacaio temporário), adiciona o ID a ele.
        if (spawnPoint != null)
        {
            spawnPoint.ActiveNpcInstanceIds.Add(newNpc.InstanceId);
        }

        newNpc.IsActive = true;

        string spawnMessage = newNpc.GetSpawnMessage();
        _server.NetworkManager.BroadcastMessageToRelevantPlayers(newNpc.Position, spawnMessage);

        // (CORREÇÃO) Retorna o NPC recém-criado.
        return newNpc;
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