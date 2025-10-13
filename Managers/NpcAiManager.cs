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
    private readonly Dictionary<NpcAiType, INpcBehavior> _behaviors;
    private readonly ConcurrentDictionary<string, Vector3> _lastSentPositions = new ConcurrentDictionary<string, Vector3>();

    public NpcAiManager(UDPServer server)
    {
        _server = server;
        // Instancia um de cada comportamento que criamos.
        var wanderingAggressive = new WanderingAggressiveBehavior(server);
        var passiveAggressive = new PassiveAggressiveBehavior(server);

        _behaviors = new Dictionary<NpcAiType, INpcBehavior>
    {
        // Agressivos
        { NpcAiType.Wandering_Aggressive, wanderingAggressive },
        { NpcAiType.Passive_Aggressive, passiveAggressive },
        { NpcAiType.Stationary_Guard, new StationaryGuardBehavior(server) },
        { NpcAiType.Patrolling_Guard, new PatrollingGuardBehavior(server) },

        { NpcAiType.Patrolling_Aggressive, new PatrollingAggressiveBehavior(server) },

        // Passivos e de Ambiente
        { NpcAiType.Ambient_Fleeing, new AmbientFleeingBehavior(server) },
        { NpcAiType.Ambient_Passive, new AmbientPassiveBehavior() },
        { NpcAiType.Ambient_Wandering, new AmbientWanderingBehavior(server) },

        // Especiais
        { NpcAiType.Training_Dummy, new TrainingDummyBehavior(server) },
    };
    }

    /// <summary>
    /// Método de gatilho para reação imediata a dano. Chamado pelo CombatManager.
    /// </summary>
    public void OnNpcDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        if (npc.IsDead || !npc.IsActive || npc.Behavior == null) return;
        npc.Behavior.OnDamaged(npc, attacker);
    }

    /// <summary>
    /// Retorna uma instância de comportamento com base no tipo de IA.
    /// </summary>
    public INpcBehavior GetBehavior(NpcAiType aiType)
    {
        if (_behaviors.TryGetValue(aiType, out var behavior))
        {
            return behavior;
        }
        // Fallback para um comportamento padrão se não for encontrado
        return _behaviors[NpcAiType.Wandering_Aggressive];
    }

    public async Task NpcAI_LoopAsync(CancellationToken cancellationToken)
    {
        const int AI_TICK_RATE_MS = 150; // Um valor mais equilibrado entre performance e fluidez
        const float DELTA_TIME = AI_TICK_RATE_MS / 1000.0f;
        var stopwatch = new Stopwatch();

        while (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Restart();
            var activeNpcs = _server.ActiveNpcs.Values.Where(npc => npc.IsActive).ToList();

            if (activeNpcs.Any())
            {
                var partitioner = Partitioner.Create(activeNpcs);
                Parallel.ForEach(partitioner, (npc) =>
                {
                    if (npc.IsDead) return;

                    // A IA decide o que fazer (atacar, ficar parado, etc.)
                    npc.Behavior?.Update(npc, DELTA_TIME);

                    // --- A CORREÇÃO CRÍTICA ---
                    // Identifique os tipos de NPC que NUNCA devem se mover da sua célula original.
                    bool isStationary = npc.AiType == NpcAiType.Stationary_Guard ||
                                        npc.AiType == NpcAiType.Training_Dummy;

                    // Só executamos a lógica de movimento e atualização de grade para NPCs móveis.
                    if (!isStationary)
                    {
                        UpdateNpcPosition(npc, DELTA_TIME);
                        CheckIfNpcIsStuck(npc);
                    }
                    // Para os estacionários, NÃO FAZEMOS NADA aqui.
                    // Eles foram colocados na grade no momento do spawn e devem ficar lá para sempre.
                });
            }

            stopwatch.Stop();
            var elapsedMs = (int)stopwatch.ElapsedMilliseconds;
            var delay = Math.Max(0, AI_TICK_RATE_MS - elapsedMs);
            if (delay == 0) Console.WriteLine($"[AI-WARN] Tick processing took longer than tick rate: {elapsedMs}ms");
            await Task.Delay(delay, cancellationToken);
        }
    }

    // Em Managers/NpcAiManager.cs

    private void UpdateNpcPosition(NpcInstance npc, float deltaTime)
    {
        float distanceToDestination = Vector3.Distance(npc.Position, npc.Destination);

        // Se o NPC já está no destino, não há nada a fazer, exceto garantir uma última sincronização.
        if (distanceToDestination < 0.1f)
        {
            SyncPositionIfMoved(npc); // Garante que a posição final seja enviada
            return;
        }

        // --- LÓGICA DE MOVIMENTO DO SERVIDOR ---
        float actualMoveSpeed = 5.0f * (npc.MovementSpeed / 100.0f);
        float moveAmount = actualMoveSpeed * deltaTime;

        if (moveAmount >= distanceToDestination)
        {
            npc.Position = npc.Destination;
        }
        else
        {
            Vector3 direction = Vector3.Normalize(npc.Destination - npc.Position);
            npc.Position += direction * moveAmount;
        }

        // Atualiza a posição do NPC na grade espacial
        _server.GridManager.UpdateEntity(npc);

        // --- LÓGICA DE SINCRONIZAÇÃO CONTÍNUA ---
        SyncPositionIfMoved(npc);
    }

    // (NOVO OU RESTAURADO) Adicione este método auxiliar à sua classe NpcAiManager
    private void SyncPositionIfMoved(NpcInstance npc)
    {
        const float SYNC_DISTANCE_THRESHOLD_SQR = 0.1f * 0.1f; // Limite de 10cm

        // Pega a última posição enviada, ou a posição atual se for a primeira vez
        Vector3 lastSentPos = _lastSentPositions.GetOrAdd(npc.Id, npc.Position);

        if (Vector3.DistanceSquared(npc.Position, lastSentPos) > SYNC_DISTANCE_THRESHOLD_SQR)
        {
            // Envia a NOVA posição atual para os clientes relevantes
            string posStr = $"{npc.Position.X.ToString(CultureInfo.InvariantCulture)},{npc.Position.Y.ToString(CultureInfo.InvariantCulture)},{npc.Position.Z.ToString(CultureInfo.InvariantCulture)}";
            _server.NetworkManager.BroadcastMessageToRelevantPlayers(npc.Position, $"NPC_MOVE|{npc.Id}|{posStr}");

            // Atualiza a "última posição enviada"
            _lastSentPositions[npc.Id] = npc.Position;
        }
    }

    private void CheckIfNpcIsStuck(NpcInstance npc)
    {
        if (npc.CurrentState != NpcAiState.Idle && npc.CurrentState != NpcAiState.Attacking)
        {
            if (Vector3.Distance(npc.Position, npc.LastPosition) < 0.1f)
            {
                if ((_server.CurrentTimeUtc - npc.TimeAtLastPosition).TotalSeconds > 5)
                {
                    npc.CurrentState = NpcAiState.Idle; // Força uma nova decisão
                    npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(2);
                }
            }
            else
            {
                npc.LastPosition = npc.Position;
                npc.TimeAtLastPosition = _server.CurrentTimeUtc;
            }
        }
        else
        {
            npc.LastPosition = npc.Position;
            npc.TimeAtLastPosition = _server.CurrentTimeUtc;
        }
    }
}