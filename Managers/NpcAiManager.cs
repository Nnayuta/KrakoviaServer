// Managers/NpcAiManager.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

public class NpcAiManager
{
    private readonly UDPServer _server;
    private readonly ThreadLocal<Random> _threadRandom =
        new ThreadLocal<Random>(() => new Random(Environment.CurrentManagedThreadId));

    private const float BASE_NPC_MOVE_SPEED = 5.0f;
    private const float CASTER_IDEAL_DISTANCE = 15.0f;

    public NpcAiManager(UDPServer server)
    {
        _server = server;
    }

    #region Loop Principal Paralelo

    public async Task NpcAI_LoopAsync(CancellationToken cancellationToken)
    {
        const int AI_TICK_RATE_MS = 200;
        const float DELTA_TIME = AI_TICK_RATE_MS / 1000.0f;
        var stopwatch = new Stopwatch();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                stopwatch.Restart();
                var activeNpcs = _server.ActiveNpcs.Values.Where(npc => npc.IsActive).ToList();
                if (activeNpcs.Any())
                {
                    var partitioner = Partitioner.Create(activeNpcs, EnumerablePartitionerOptions.NoBuffering);
                    Parallel.ForEach(partitioner, (npc) => ProcessNpcTick(npc, DELTA_TIME));
                }
                stopwatch.Stop();
                var elapsedMs = (int)stopwatch.ElapsedMilliseconds;
                var delay = Math.Max(0, AI_TICK_RATE_MS - elapsedMs);
                if (delay == 0) Console.WriteLine($"[AI-WARN] Tick processing took longer than tick rate: {elapsedMs}ms");
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[NpcAiManager] AI loop cancelled for shutdown.");
                break;
            }
        }
    }

    #region Restante do código (sem alterações)

    private void ProcessNpcTick(NpcInstance npc, float deltaTime)
    {
        if (npc.IsDead) return;

        // Lógica de Training Dummy permanece a mesma
        if (npc.AiType == NpcAiType.Training_Dummy)
        {
            if (npc.CurrentHealth < npc.MaxHealth && (_server.CurrentTimeUtc - npc.LastDamageTime).TotalSeconds > 10)
            {
                npc.CurrentHealth = npc.MaxHealth;
                _server.NetworkManager.BroadcastMessageToAll($"ENTITY_HEALTH_UPDATE|{npc.Id}|{npc.CurrentHealth}|{npc.MaxHealth}");
            }
            return;
        }

        if (npc.CurrentHealth <= 0)
        {
            ICombatEntity? lastAttacker = GetPlayerById(npc.ThreatTable.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key);
            OnNpcKilled(npc, lastAttacker);
            return;
        }

        switch (npc.CurrentState)
        {
            case NpcAiState.Idle: HandleIdleState(npc); break;
            case NpcAiState.Wandering: HandleWanderingState(npc); break;
            case NpcAiState.Patrolling: HandlePatrollingState(npc); break;
            case NpcAiState.Chasing: HandleChasingState(npc); break;
            case NpcAiState.Attacking: HandleAttackingState(npc); break;
            case NpcAiState.ReturningToSpawn: HandleReturningToSpawnState(npc); break;
            case NpcAiState.Fleeing: HandleFleeingState(npc); break;
        }

        UpdateNpcPosition(npc, deltaTime);
        CheckIfNpcIsStuck(npc);
    }
    #endregion

    #region Handlers de Estado da IA

    private void HandleIdleState(NpcInstance npc)
    {
        if (_server.CurrentTimeUtc < npc.NextActionTime) return;

        // Primeiro, a lógica de combate sempre tem prioridade.
        if (IsAggressive(npc.AiType))
        {
            if (TryFindAndSetTarget(npc, true)) return; // 'true' para checar aggro social
        }


        if (npc.AiType == NpcAiType.Ambient_Fleeing)
        {
            Player? nearbyPlayer = FindClosestPlayerInAggroRange(npc);
            if (nearbyPlayer != null)
            {
                npc.TargetPlayerId = nearbyPlayer.Id;
                ChangeNpcState(npc, NpcAiState.Fleeing);
                return;
            }
        }

        switch (npc.AiType)
        {
            case NpcAiType.Wandering_Aggressive:
            case NpcAiType.Ambient_Fleeing:
            case NpcAiType.Ambient_Wandering:
                Vector3 randomPoint = FindWanderPoint(npc);
                SetNpcDestination(npc, randomPoint);
                ChangeNpcState(npc, NpcAiState.Wandering);
                break;

            case NpcAiType.Patrolling_Aggressive:
                if (npc.PatrolPath != null && npc.PatrolPath.Any())
                {
                    SetNpcDestination(npc, npc.PatrolPath[npc.CurrentPatrolIndex]);
                    ChangeNpcState(npc, NpcAiState.Patrolling);
                }
                break;

            default:
                npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(5);
                break;
        }
    }

    private void HandleWanderingState(NpcInstance npc)
    {
        if (IsAggressive(npc.AiType) && TryFindAndSetTarget(npc)) return;

        // Chegou ao destino? Volta a ficar ocioso.
        if (Vector3.Distance(npc.Position, npc.Destination) < 1.5f)
        {
            ChangeNpcState(npc, NpcAiState.Idle);
            npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value!.Next(4, 10));
        }
    }

    private void CheckIfNpcIsStuck(NpcInstance npc)
    {
        // Só checa se o NPC deveria estar se movendo (não está Idle ou Attacking)
        if (npc.CurrentState != NpcAiState.Idle && npc.CurrentState != NpcAiState.Attacking)
        {
            // Se a posição quase não mudou
            if (Vector3.Distance(npc.Position, npc.LastPosition) < 0.1f)
            {
                // E ele está parado há mais de 5 segundos
                if ((_server.CurrentTimeUtc - npc.TimeAtLastPosition).TotalSeconds > 5)
                {
                    // Ele está preso! Força o retorno ao Idle para uma nova decisão.
                    // Console.WriteLine($"[AI-WARN] NPC {npc.InstanceId} parece estar preso. Resetando para Idle.");
                    ChangeNpcState(npc, NpcAiState.Idle);
                    npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(2); // Pequena pausa antes de tentar de novo
                }
            }
            else // Se ele se moveu, atualiza os dados
            {
                npc.LastPosition = npc.Position;
                npc.TimeAtLastPosition = _server.CurrentTimeUtc;
            }
        }
        else // Se ele está parado por design, reseta o tracker.
        {
            npc.LastPosition = npc.Position;
            npc.TimeAtLastPosition = _server.CurrentTimeUtc;
        }
    }

    private void HandlePatrollingState(NpcInstance npc)
    {
        if (TryFindAndSetTarget(npc)) return;
        if (npc.PatrolPath == null || !npc.PatrolPath.Any()) { ChangeNpcState(npc, NpcAiState.Idle); return; }

        // Chegou ao ponto de patrulha?
        if (Vector3.Distance(npc.Position, npc.Destination) < 1.0f)
        {
            // Para no local.
            SetNpcDestination(npc, npc.Position);

            // Avança o índice para a próxima vez.
            npc.CurrentPatrolIndex = (npc.CurrentPatrolIndex + 1) % npc.PatrolPath.Count;

            // Entra em Idle para a pausa. O HandleIdleState cuidará do resto.
            ChangeNpcState(npc, NpcAiState.Idle);
            npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(2, 6));
        }
    }

    private void HandleChasingState(NpcInstance npc)
    {
        ICombatEntity? target = GetCurrentTarget(npc);
        if (target == null || target.IsDead || Vector3.Distance(npc.Position, npc.SpawnPosition) > npc.BaseData.LeashRange)
        {
            ResetAggro(npc);
            return;
        }

        // Se já está no alcance para atacar, muda de estado e PARA de se mover.
        if (Vector3.Distance(npc.Position, target.Position) <= npc.BaseData.MaxAbilityRange)
        {
            ChangeNpcState(npc, NpcAiState.Attacking);
            SetNpcDestination(npc, npc.Position);
            return;
        }

        // Se não, continua perseguindo.
        SetNpcDestination(npc, target.Position);
    }

    private void HandleAttackingState(NpcInstance npc)
    {
        ICombatEntity? target = GetCurrentTarget(npc);
        if (target == null || target.IsDead)
        {
            ResetAggro(npc);
            return;
        }

        FaceTarget(npc, target);

        // Se o alvo fugiu, volta a perseguir.
        if (Vector3.Distance(npc.Position, target.Position) > npc.BaseData.MaxAbilityRange)
        {
            ChangeNpcState(npc, NpcAiState.Chasing);
            return;
        }

        if (npc.BaseData.AutoAttackAbilityID != null && _server.CurrentTimeUtc >= npc.NextAutoAttackTime)
        {
            // A sua verificação de distância aqui já é uma boa prática.
            if (Vector3.Distance(npc.Position, target.Position) <= DataManager.Abilities[npc.BaseData.AutoAttackAbilityID].Range)
            {
                _server.CombatManager.ProcessAbilityRequest(npc, npc.BaseData.AutoAttackAbilityID, target.Id);
                // Reseta o timer do auto-ataque com base no SwingTimer do NPC.
                npc.NextAutoAttackTime = _server.CurrentTimeUtc.AddSeconds(npc.BaseData.SwingTimer);
            }
        }

        // --- Passo 2: Processar Habilidades Especiais (dependente do GCD) ---
        // O NPC só tenta usar uma habilidade especial se não estiver em Global Cooldown.
        if (_server.CurrentTimeUtc >= npc.GlobalCooldownEndTime)
        {
            AbilityData? specialAbility = ChooseBestSpecialAbility(npc, target);
            if (specialAbility != null)
            {
                _server.CombatManager.ProcessAbilityRequest(npc, specialAbility.ID, target.Id);
                npc.GlobalCooldownEndTime = _server.CurrentTimeUtc.AddSeconds(1.5);
            }
        }
    }

    private void HandleReturningToSpawnState(NpcInstance npc)
    {
        if (Vector3.Distance(npc.Position, npc.SpawnPosition) < 1.5f)
        {
            npc.CurrentHealth = npc.MaxHealth;
            npc.CurrentResource = npc.MaxResource;
            ChangeNpcState(npc, NpcAiState.Idle);
        }
        else
        {
            SetNpcDestination(npc, npc.SpawnPosition);
        }
    }


    private void HandleFleeingState(NpcInstance npc)
    {
        if ((_server.CurrentTimeUtc - npc.LastStateChangeTime).TotalSeconds > 8)
        {
            npc.TargetPlayerId = null;
            ChangeNpcState(npc, NpcAiState.Idle);
            return;
        }

        ICombatEntity? target = GetCurrentTarget(npc);
        if (target != null)
        {
            Vector3 fleeDirection = Vector3.Normalize(npc.Position - target.Position);
            SetNpcDestination(npc, npc.Position + fleeDirection * 10f);
        }
    }

    #endregion

    #region Lógica de Morte e Métodos de Suporte (Públicos e Privados)

    public void OnNpcKilled(NpcInstance npc, ICombatEntity? killer)
    {
        if (npc.IsDead) return;

        // A única responsabilidade do NpcAiManager agora é iniciar o processo de morte.
        Console.WriteLine($"[AI] Iniciando processo de morte para {npc.Id}.");

        // Ele notifica o WorldManager, que cuidará de TODO o resto.
        _server.WorldManager.ProcessNpcDeath(npc, killer);
    }

    public Player? GetCreditPlayer(NpcInstance npc, ICombatEntity? killer)
    {
        // 1. Tenta encontrar o jogador com a maior ameaça na ThreatTable.
        if (npc.ThreatTable.Any())
        {
            var topThreatPlayerId = npc.ThreatTable.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key;
            if (GetPlayerById(topThreatPlayerId) is { } playerFromThreat)
            {
                return playerFromThreat;
            }
        }

        // 2. Se não houver ninguém na ThreatTable, ou o jogador não for encontrado,
        //    usa o 'killer' como fallback, se ele for um jogador.
        if (killer is Player killerPlayer)
        {
            return killerPlayer;
        }

        // 3. Se não houver nenhum jogador para dar o crédito, retorna nulo.
        return null;
    }

    private Vector3 FindWanderPoint(NpcInstance npc)
    {
        float wanderRadius = npc.BaseData.LeashRange * 0.7f;
        float angle = (float)(_threadRandom.Value!.NextDouble() * 2 * Math.PI);
        float radius = (float)_threadRandom.Value.NextDouble() * wanderRadius;

        // Cria o deslocamento no plano XZ
        var offset = new Vector3((float)Math.Cos(angle) * radius, 0, (float)Math.Sin(angle) * radius);

        // Adiciona o deslocamento à posição de spawn, mas preserva o Y original do spawn point.
        var newPoint = npc.SpawnPosition + offset;
        newPoint.Y = npc.SpawnPosition.Y;

        return newPoint;
    }

    private bool TryFindAndSetTarget(NpcInstance npc, bool allowSocialAggro = false)
    {
        ICombatEntity? target = FindBestTarget(npc);
        if (target != null)
        {
            if (npc.TargetPlayerId != target.Id) // Se for um novo alvo
            {
                npc.TargetPlayerId = target.Id;
                ChangeNpcState(npc, NpcAiState.Chasing);
                if (allowSocialAggro)
                {
                    NotifyNearbyAllies(npc, target); // Alerta os amigos!
                }
            }
            return true;
        }
        return false;
    }

    private void NotifyNearbyAllies(NpcInstance originalNpc, ICombatEntity target)
    {
        const float socialAggroRadius = 20f;
        var nearbyNpcs = _server.ActiveNpcs.Values
            .Where(otherNpc =>
                otherNpc.IsActive &&
                otherNpc.Id != originalNpc.Id &&
                otherNpc.BaseData.Faction == originalNpc.BaseData.Faction && // Mesma facção
                otherNpc.CurrentState == NpcAiState.Idle && // Só alerta quem está ocioso
                Vector3.Distance(originalNpc.Position, otherNpc.Position) <= socialAggroRadius)
            .ToList();

        foreach (var ally in nearbyNpcs)
        {
            // Adiciona uma pequena quantidade de ameaça para "puxar" o aliado para o combate
            ally.ThreatTable[target.Id] = 1;
            Console.WriteLine($"[Social Aggro] NPC {originalNpc.Id} alertou o aliado {ally.Id} sobre o alvo {target.Id}");
        }
    }

    private bool FindValidWanderPoint(NpcInstance npc, out Vector3 foundPoint)
    {
        float wanderRadius = npc.BaseData.LeashRange * 0.7f;
        const int maxAttempts = 10; // Tenta 10 vezes encontrar um ponto

        for (int i = 0; i < maxAttempts; i++)
        {
            float angle = (float)(_threadRandom.Value!.NextDouble() * 2 * Math.PI);
            float radius = (float)_threadRandom.Value.NextDouble() * wanderRadius;
            Vector3 potentialPoint = npc.SpawnPosition + new Vector3((float)Math.Cos(angle) * radius, 0, (float)Math.Sin(angle) * radius);

            // VALIDAÇÃO: O ponto está a uma distância razoável do ponto atual?
            // Isso evita que ele escolha um ponto logo ao lado e fique "tremendo".
            if (Vector3.Distance(npc.Position, potentialPoint) > 3.0f)
            {
                foundPoint = potentialPoint;
                return true;
            }
        }

        foundPoint = npc.Position; // Não encontrou, fica parado
        return false;
    }

    private ICombatEntity? FindBestTarget(NpcInstance npc)
    {
        if (npc.ThreatTable.Any())
        {
            var topThreat = npc.ThreatTable.OrderByDescending(kvp => kvp.Value).First();
            var player = GetPlayerById(topThreat.Key);
            if (player != null && !player.IsDead && Vector3.Distance(npc.Position, player.Position) <= npc.BaseData.LeashRange)
            {
                return player;
            }
        }
        return FindClosestPlayerInAggroRange(npc);
    }

    // Dentro da sua classe NpcAiManager.cs

    private Player? FindClosestPlayerInAggroRange(NpcInstance npc)
    {
        return _server.ConnectedPlayers.Values
            .Where(p => !p.IsDead && IsHostileTo(npc, p) && Vector3.Distance(npc.Position, p.Position) <= npc.BaseData.AggroRange)
            .OrderBy(p => Vector3.Distance(npc.Position, p.Position))
            .FirstOrDefault();
    }

    private bool IsHostileTo(NpcInstance npc, Player player)
    {
        if (npc.ThreatTable.ContainsKey(player.Id)) return true;
        if (npc.AiType == NpcAiType.Passive_Aggressive || npc.AiType == NpcAiType.Ambient_Passive) return false;
        if (IsAggressive(npc.AiType) && npc.BaseData.Faction == NpcFaction.Enemy) return true;
        return false;
    }


    private void ResetAggro(NpcInstance npc)
    {
        npc.TargetPlayerId = null;
        npc.ThreatTable.Clear();
        ChangeNpcState(npc, NpcAiState.ReturningToSpawn);
    }


    private void ChangeNpcState(NpcInstance npc, NpcAiState newState)
    {
        if (npc.CurrentState == newState) return;
        npc.CurrentState = newState;
        npc.LastStateChangeTime = _server.CurrentTimeUtc;
    }

    private void UpdateNpcPosition(NpcInstance npc, float deltaTime)
    {
        float distanceToDestination = Vector3.Distance(npc.Position, npc.Destination);

        if (distanceToDestination < 0.1f)
        {
            if (!npc.HasStopped)
            {
                SetNpcDestination(npc, npc.Position);
                npc.HasStopped = true;
            }
            return;
        }
        npc.HasStopped = false;

        float actualMoveSpeed = BASE_NPC_MOVE_SPEED * (npc.MovementSpeed / 100.0f);
        float moveAmount = actualMoveSpeed * deltaTime;

        if (moveAmount >= distanceToDestination)
        {
            npc.Position = npc.Destination;
        }
        else
        {
            // Calcula a direção normalizada do movimento
            Vector3 direction = Vector3.Normalize(npc.Destination - npc.Position);
            // Move o NPC na direção correta pela quantidade calculada
            npc.Position += direction * moveAmount;
        }
    }

    private void SetNpcDestination(NpcInstance npc, Vector3 newDestination)
    {
        if (Vector3.Distance(npc.Destination, newDestination) < 0.1f) return;

        npc.Destination = newDestination;
        string posStr = $"{newDestination.X.ToString(CultureInfo.InvariantCulture)},{newDestination.Y.ToString(CultureInfo.InvariantCulture)},{newDestination.Z.ToString(CultureInfo.InvariantCulture)}";
        _server.NetworkManager.BroadcastMessageToAll($"NPC_MOVE|{npc.InstanceId}|{posStr}");
    }

    private void FaceTarget(NpcInstance npc, ICombatEntity target) { /* Cosmético, tratado no cliente */ }
    private ICombatEntity? GetCurrentTarget(NpcInstance npc) => GetPlayerById(npc.TargetPlayerId);

    private Player? GetPlayerById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return _server.ConnectedPlayers.Values.FirstOrDefault(p => p.Id == id);
    }

    private bool IsAggressive(NpcAiType aiType)
    {
        return aiType == NpcAiType.Passive_Aggressive ||
               aiType == NpcAiType.Patrolling_Aggressive ||
               aiType == NpcAiType.Wandering_Aggressive ||
               aiType == NpcAiType.Stationary_Guard;
    }

    private AbilityData? ChooseBestSpecialAbility(NpcInstance npc, ICombatEntity target)
    {
        return npc.BaseData.AbilityIDs
            .Where(id => id != npc.BaseData.AutoAttackAbilityID)
            .Select(id => DataManager.Abilities.TryGetValue(id, out var ability) ? ability : null)
            .Where(ability =>
            {
                if (ability == null || IsOnCooldown(npc, ability.ID) || Vector3.Distance(npc.Position, target.Position) > ability.Range)
                    return false;

                // CONTEXTO: Só usa cura se a vida estiver baixa
                if (ability.EffectType == AbilityEffectType.Heal && npc.CurrentHealth > npc.MaxHealth * 0.6)
                    return false; // Não cura se tiver mais de 60% de vida

                return true;
            })
            .OrderByDescending(ability => ability?.Priority ?? 0)
            .FirstOrDefault();
    }

    private bool IsCaster(NpcInstance npc) => npc.BaseData.MaxAbilityRange > 5.0f;
    private float GetAttackRange(NpcInstance npc) => npc.BaseData.MaxAbilityRange;

    private bool IsOnCooldown(NpcInstance npc, string cooldownKey)
    {
        if (string.IsNullOrEmpty(cooldownKey)) return false;
        return npc.AbilityCooldowns.TryGetValue(cooldownKey, out var cooldownEnd) && _server.CurrentTimeUtc < cooldownEnd;
    }

    #endregion
    #endregion
}