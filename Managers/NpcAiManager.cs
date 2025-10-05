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
                // A sua lógica de processar apenas NPCs ativos já previne a morte instantânea
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

        // A verificação de vida e o relatório de morte continuam sendo uma boa prática
        if (npc.CurrentHealth <= 0)
        {
            ICombatEntity? lastAttacker = GetPlayerById(npc.ThreatTable.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key);
            OnNpcKilled(npc, lastAttacker); // Chamamos a nossa própria função OnNpcKilled
            return;
        }

        CheckIfNpcIsStuck(npc);
        UpdateNpcPosition(npc, deltaTime);

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
    }
    #endregion

    #region Handlers de Estado da IA

    private void HandleIdleState(NpcInstance npc)
    {
        if (_server.CurrentTimeUtc < npc.NextActionTime)
        {
            return;
        }

        // Primeiro, a lógica de combate sempre tem prioridade.
        if (IsAggressive(npc.BaseData.AiType))
        {
            if (TryFindAndSetTarget(npc)) return;
        }

        if (npc.BaseData.AiType == NpcAiType.Ambient_Fleeing)
        {
            Player? nearbyPlayer = FindClosestPlayerInAggroRange(npc);
            if (nearbyPlayer != null)
            {
                npc.TargetPlayerId = nearbyPlayer.Id;
                ChangeNpcState(npc, NpcAiState.Fleeing);
                return;
            }
        }

        switch (npc.BaseData.AiType)
        {
            case NpcAiType.Wandering_Aggressive:
            case NpcAiType.Ambient_Fleeing:
                // DECIDE PASSEAR E JÁ DEFINE O DESTINO AQUI!
                float wanderRadius = npc.BaseData.LeashRange * 0.7f;
                float angle = (float)(_threadRandom.Value.NextDouble() * 2 * Math.PI);
                float radius = (float)_threadRandom.Value.NextDouble() * wanderRadius;
                Vector3 randomPoint = npc.SpawnPosition + new Vector3((float)Math.Cos(angle) * radius, 0, (float)Math.Sin(angle) * radius);

                // Define o novo destino e MUDA o estado.
                SetNpcDestination(npc, randomPoint);
                ChangeNpcState(npc, NpcAiState.Wandering);
                break;

            case NpcAiType.Patrolling_Aggressive:
                if (npc.PatrolPath != null && npc.PatrolPath.Any())
                {
                    // Mesma lógica: decide e define o destino aqui.
                    SetNpcDestination(npc, npc.PatrolPath[npc.CurrentPatrolIndex]);
                    ChangeNpcState(npc, NpcAiState.Patrolling);
                }
                break;

            // Outros tipos de IA ficam em Idle.
            default:
                // Para garantir que a pausa funcione, resetamos o timer para ele "pensar" de novo mais tarde.
                npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(5);
                break;
        }
    }

    private void HandleWanderingState(NpcInstance npc)
    {
        // A lógica de procurar um alvo continua sendo a prioridade.
        if (IsAggressive(npc.BaseData.AiType) && TryFindAndSetTarget(npc)) return;

        // Chegou ao destino? Volta para Idle e agenda uma pausa.
        if (Vector3.Distance(npc.Position, npc.Destination) < 1.5f)
        {
            // Ao chegar, ele para (destino = posição atual), entra em Idle e agenda a próxima ação.
            SetNpcDestination(npc, npc.Position);
            ChangeNpcState(npc, NpcAiState.Idle);
            npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(4, 10));
        }

        // Se o NPC fica "preso" por muito tempo, força uma nova decisão.
        else if ((_server.CurrentTimeUtc - npc.LastStateChangeTime).TotalSeconds > 20)
        {
            ChangeNpcState(npc, NpcAiState.Idle);
            npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(2, 5));
            return;
        }

        // ========================================================================
        // A CORREÇÃO ESTÁ AQUI
        // Se o destino do NPC é o local onde ele já está, ele precisa de um novo lugar para ir.
        // Esta condição é muito mais robusta do que "npc.Position == npc.Destination".
        // ========================================================================
        else if (npc.Destination == npc.Position)
        {
            // Define um novo ponto aleatório para passear perto do spawn.
            float wanderRadius = npc.BaseData.LeashRange * 0.7f;
            float angle = (float)(_threadRandom.Value.NextDouble() * 2 * Math.PI);
            float radius = (float)_threadRandom.Value.NextDouble() * wanderRadius;
            Vector3 randomPoint = npc.SpawnPosition + new Vector3((float)Math.Cos(angle) * radius, 0, (float)Math.Sin(angle) * radius);

            SetNpcDestination(npc, randomPoint);
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

        if (Vector3.Distance(npc.Position, target.Position) <= npc.BaseData.MaxAbilityRange)
        {
            ChangeNpcState(npc, NpcAiState.Attacking);
            return;
        }
        SetNpcDestination(npc, target.Position);
    }

    private void HandleAttackingState(NpcInstance npc)
    {
        ICombatEntity? target = GetCurrentTarget(npc);
        if (target == null || target.IsDead) { ResetAggro(npc); return; }

        if (Vector3.Distance(npc.Position, target.Position) > npc.BaseData.MaxAbilityRange)
        {
            ChangeNpcState(npc, NpcAiState.Chasing);
            return;
        }

        FaceTarget(npc, target);

        bool isGcdReady = _server.CurrentTimeUtc >= npc.GlobalCooldownEndTime;
        if (isGcdReady)
        {
            AbilityData? specialAbility = ChooseBestSpecialAbility(npc, target);
            if (specialAbility != null)
            {
                _server.CombatManager.ProcessAbilityRequest(npc, specialAbility.ID, target.Id);
                npc.GlobalCooldownEndTime = _server.CurrentTimeUtc.AddSeconds(1.5);
                return;
            }
        }

        if (npc.BaseData.AutoAttackAbilityID != null && _server.CurrentTimeUtc >= npc.NextAutoAttackTime)
        {
            if (DataManager.Abilities.TryGetValue(npc.BaseData.AutoAttackAbilityID, out var autoAttackAbility))
            {
                if (Vector3.Distance(npc.Position, target.Position) <= autoAttackAbility.Range)
                {
                    _server.CombatManager.ProcessAbilityRequest(npc, npc.BaseData.AutoAttackAbilityID, target.Id);
                    npc.NextAutoAttackTime = _server.CurrentTimeUtc.AddSeconds(npc.BaseData.SwingTimer);
                }
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

    private bool TryFindAndSetTarget(NpcInstance npc)
    {
        ICombatEntity? target = FindBestTarget(npc);
        if (target != null)
        {
            npc.TargetPlayerId = target.Id;
            ChangeNpcState(npc, NpcAiState.Chasing);
            return true;
        }
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
        if (npc.BaseData.AiType == NpcAiType.Passive_Aggressive) return false;
        if (IsAggressive(npc.BaseData.AiType) && npc.BaseData.Faction == NpcFaction.Enemy) return true;
        // TODO: Adicionar lógica de facção vs facção
        return false;
    }

    private void ResetAggro(NpcInstance npc)
    {
        npc.TargetPlayerId = null;
        npc.ThreatTable.Clear();
        ChangeNpcState(npc, NpcAiState.ReturningToSpawn);
    }

    public void ChangeNpcState(NpcInstance npc, NpcAiState newState)
    {
        if (npc.CurrentState == newState) return;
        npc.CurrentState = newState;
        npc.LastStateChangeTime = _server.CurrentTimeUtc;
    }

    private void UpdateNpcPosition(NpcInstance npc, float deltaTime)
    {
        if (Vector3.Distance(npc.Position, npc.Destination) < 0.1f) return;

        float currentMoveSpeedStat = npc.MovementSpeed;
        if (currentMoveSpeedStat <= 0)
        {
            Console.WriteLine($"[AI-WARN] NPC {npc.InstanceId} tem velocidade 0 e não pode se mover.");
            return;
        }

        float actualMoveSpeed = BASE_NPC_MOVE_SPEED * (currentMoveSpeedStat / 100.0f);
        float dist = Vector3.Distance(npc.Position, npc.Destination);
        float moveAmount = actualMoveSpeed * deltaTime;

        if (moveAmount >= dist)
        {
            npc.Position = npc.Destination;
        }
        else
        {
            npc.Position += Vector3.Normalize(npc.Destination - npc.Position) * moveAmount;
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
               aiType == NpcAiType.Wandering_Aggressive;
    }

    private AbilityData? ChooseBestSpecialAbility(NpcInstance npc, ICombatEntity target)
    {
        return npc.BaseData.AbilityIDs
            .Where(id => id != npc.BaseData.AutoAttackAbilityID)
            .Select(id => DataManager.Abilities.TryGetValue(id, out var ability) ? ability : null)
            .Where(ability => ability != null && !IsOnCooldown(npc, ability.ID) && Vector3.Distance(npc.Position, target.Position) <= ability.Range)
            .OrderByDescending(ability => ability?.Priority ?? 0)
            .FirstOrDefault();
    }

    private bool IsOnCooldown(NpcInstance npc, string cooldownKey)
    {
        if (string.IsNullOrEmpty(cooldownKey)) return false;
        return npc.AbilityCooldowns.TryGetValue(cooldownKey, out var cooldownEnd) && _server.CurrentTimeUtc < cooldownEnd;
    }

    #endregion

    #endregion
}