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


    private float _worldUpdateTimer = 0f;
    private const float WORLD_UPDATE_INTERVAL = 5.0f; // 5 segundos

    public void Update()
    {
        // Acumula o tempo do tick do servidor
        // Supondo um tick de 33ms, o deltaTime seria ~0.033
        _worldUpdateTimer += (float)UDPServer.SERVER_TICK_RATE_MS / 1000.0f;

        // Só executa a lógica cara a cada 5 segundos
        if (_worldUpdateTimer >= WORLD_UPDATE_INTERVAL)
        {
            _worldUpdateTimer = 0f; // Reseta o timer

            CleanupExpiredCorpses();
            CheckForRespawns();
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
        npc.IsActive = false; // Desativa a IA imediatamente
        npc.RespawnTime = _server.CurrentTimeUtc.AddSeconds(npc.BaseData.RespawnTimeSeconds);
        npc.SetCorpseDespawnTimer(120.0f, _server.CurrentTimeUtc);

        Player? creditPlayer = this.GetCreditPlayer(npc, killer);
        if (creditPlayer != null)
        {
            // --- LÓGICA DE XP DINÂMICA ---
            // 1. Calcula a quantidade de XP a ser concedida usando a nova lógica.
            int experienceToGrant = ExperienceManager.CalculateExperienceReward(creditPlayer, npc);

            // 2. Concede a experiência calculada.
            if (experienceToGrant > 0)
            {
                _server.PlayerProgressionManager.GrantExperience(creditPlayer, experienceToGrant);
                _server.NetworkManager.SendMessageToPlayer(creditPlayer, $"SHOW_FEEDBACK|+{experienceToGrant} EXP");
            }
            // --- FIM DA LÓGICA DE XP ---

            if (npc.BaseData.CurrencyReward > 0)
            {
                creditPlayer.TotalBronze += npc.BaseData.CurrencyReward;

                _server.NetworkManager.SendCurrencyUpdate(creditPlayer);
                _server.NetworkManager.SendMessageToPlayer(creditPlayer, $"SHOW_FEEDBACK|+{npc.BaseData.CurrencyReward} Moedas");
            }
        }

        if (!string.IsNullOrEmpty(npc.BaseData.LootTableID))
        {
            // O LootManager GERA os stats e REGISTRA no ItemInstanceManager. CORRETO.
            List<ItemStack> generatedLoot = _server.LootManager.GenerateLootForNpc(npc.BaseData.LootTableID, npc.Level);

            // O loot é atribuído ao CORPO do NPC. CORRETO.
            npc.SetLoot(generatedLoot);
        }

        // 4. Move o NPC da lista de vivos para a lista de mortos
        _server.DeadNpcCor_pses.TryAdd(npc.InstanceId, npc);

        if (killer is Player killerPlayer)
        {
            _server.QuestManager.OnEntitySlain(killerPlayer, npc);
        }

        string message = $"ENTITY_DIED|{npc.SessionId}|{npc.HasLoot}";
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

    private void CleanupExpiredCorpses()
    {
        // Esta função agora APENAS envia a mensagem para o cliente destruir o GameObject do corpo.
        // O objeto NpcInstance continua existindo no servidor, marcado como morto.
        var expiredNpcIds = _server.DeadNpcCor_pses
                               .Where(kvp => kvp.Value.CorpseDespawnTime != DateTime.MinValue && _server.CurrentTimeUtc >= kvp.Value.CorpseDespawnTime)
                               .Select(kvp => kvp.Key)
                               .ToList();

        foreach (var npcId in expiredNpcIds)
        {
            if (_server.DeadNpcCor_pses.TryRemove(npcId, out NpcInstance npc))
            {
                // Apenas notifica o cliente. Não muda nada no estado do servidor.
                _server.NetworkManager.BroadcastMessageToRelevantPlayers(npc.Position, $"DESTROY_NPC|{npc.SessionId}");
            }
        }
    }

    private void CheckForRespawns()
    {
        // Pega todos os NPCs que estão marcados como mortos e cujo tempo de respawn já passou.
        var npcsToRespawn = _server.ActiveNpcs.Values
                                .Where(npc => npc.IsDead && _server.CurrentTimeUtc >= npc.RespawnTime)
                                .ToList();

        foreach (var npc in npcsToRespawn)
        {
            // --- PASSO 1: Informa aos clientes para destruírem o corpo na POSIÇÃO ANTIGA ---
            Vector3 corpsePosition = npc.Position;
            _server.NetworkManager.BroadcastMessageToRelevantPlayers(corpsePosition, $"DESTROY_NPC|{npc.SessionId}");

            // --- PASSO 2: Ressuscita o NPC no SERVIDOR ---
            npc.IsDead = false;
            npc.CurrentHealth = npc.MaxHealth;
            npc.CurrentResource = npc.MaxResource;
            npc.ThreatTable.Clear();
            npc.ChangeNpcState(NpcAiState.Idle, _server.CurrentTimeUtc);
            npc.IsActive = false;
            npc.RespawnTime = DateTime.MaxValue; // Previne que ele seja pego de novo neste loop

            // --- PASSO 3: CALCULA A NOVA POSIÇÃO DE RESPAWN ---
            // 3.1: Encontra o SpawnPoint original do NPC.
            var spawnPoint = FindSpawnPointForNpc(npc.InstanceId);

            Vector3 newSpawnPosition;

            // 3.2: Se encontrarmos um SpawnPoint (o caso normal para monstros do mundo)...
            if (spawnPoint != null)
            {
                // ...calculamos uma NOVA posição aleatória dentro da sua área de spawn.
                newSpawnPosition = CalculateSpawnPosition(spawnPoint);
                Console.WriteLine($"[WorldManager] NPC {npc.BaseData.TypeId} irá reaparecer em um novo local: {newSpawnPosition}");
            }
            else
            {
                // ...senão (ex: um lacaio invocado), ele reaparece em sua posição original por segurança.
                newSpawnPosition = npc.SpawnPosition;
                Console.WriteLine($"[WorldManager] NPC {npc.BaseData.TypeId} não tem SpawnPoint, reaparecendo no local original: {newSpawnPosition}");
            }

            // --- PASSO 4: MOVE O NPC para sua NOVA posição de spawn no SERVIDOR ---
            npc.Position = newSpawnPosition;
            npc.Destination = newSpawnPosition; // Importante para que a IA não tente "voltar para casa"
            _server.GridManager.UpdateEntity(npc);

            // --- PASSO 5: Informa aos clientes para criarem o novo NPC na NOVA POSIÇÃO ---
            string spawnMessage = npc.GetSpawnMessage();
            _server.NetworkManager.BroadcastMessageToRelevantPlayers(npc.Position, spawnMessage);

            Console.WriteLine($"[WorldManager] NPC {npc.InstanceId} ({npc.BaseData.TypeId}) desapareceu do local da morte e reapareceu em seu novo spawn.");
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
    /// Spawna todos os NPCs pela primeira vez quando o servidor inicia.
    /// </summary>
    public void InitializeSpawns()
    {
        Console.WriteLine("[WorldManager] Inicializando e populando o mundo...");
        foreach (var spawnPoint in DataManager.SpawnPoints)
        {
            if (DataManager.Npcs.TryGetValue(spawnPoint.NpcTypeId, out NpcData? npcData))
            {
                for (int i = 0; i < spawnPoint.Quantity; i++)
                {
                    Vector3 spawnPosition = CalculateSpawnPosition(spawnPoint);
                    // Importante: Passamos o spawnPoint para que o NPC saiba sua origem.
                    SpawnSingleNpc(npcData, spawnPosition, spawnPoint);
                }
            }
            else
            {
                Console.WriteLine($"[AVISO] Tipo de NPC '{spawnPoint.NpcTypeId}' em spawns.json não encontrado.");
            }
        }
        Console.WriteLine($"[WorldManager] Mundo populado com {_server.ActiveNpcs.Count} NPCs (todos hibernando).");
    }


    private NpcInstance SpawnSingleNpc(NpcData npcData, Vector3 position, SpawnPoint? spawnPoint)
    {
        var newNpc = new NpcInstance(
            position,
            spawnPoint?.InitialRotation ?? Vector3.Zero,
            spawnPoint?.AiType ?? NpcAiType.Wandering_Aggressive,
            spawnPoint?.PatrolPath,
            npcData,
            _server
        );

        newNpc.Behavior = _server.NpcAiManager.GetBehavior(newNpc.AiType);
        _server.ActiveNpcs.TryAdd(newNpc.InstanceId, newNpc);
        _server.GridManager.UpdateEntity(newNpc);
        _server.NpcsBySessionId.TryAdd(newNpc.SessionId, newNpc);

        if (spawnPoint != null)
        {
            spawnPoint.ActiveNpcInstanceIds.Add(newNpc.InstanceId);
        }

        // (MUDANÇA CRÍTICA) NPCs começam inativos por padrão.
        newNpc.IsActive = false;

        // NÃO enviamos mensagem de spawn aqui. O InterestManager/VisibilityManager fará isso.

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