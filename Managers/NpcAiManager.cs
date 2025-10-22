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

    private int _tickCounter = 0;

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

    // Agora, substitua o método Update() simples pela versão abaixo:
    public void Update(float deltaTime)
    {
        _tickCounter++; // Incrementa o contador a cada tick do servidor

        // --- NPCs Rápidos ---
        // A cada tick, processamos os NPCs que precisam de alta frequência (ex: em combate)
        var fastNpcs = _server.ActiveNpcs.Values
            .Where(npc => npc.IsActive && !npc.IsDead && npc.UpdateTier == AiUpdateTier.Fast)
            .ToList();

        if (fastNpcs.Any())
        {
            ProcessNpcUpdates(fastNpcs, deltaTime);
        }

        // --- NPCs Lentos ---
        // A cada 3 ticks (por exemplo), processamos os NPCs de baixa frequência.
        // O operador '%' (módulo) é perfeito para isso.
        if (_tickCounter % 3 == 0)
        {
            var slowNpcs = _server.ActiveNpcs.Values
                .Where(npc => npc.IsActive && !npc.IsDead && npc.UpdateTier == AiUpdateTier.Slow)
                .ToList();

            if (slowNpcs.Any())
            {
                // NPCs lentos são atualizados com um deltaTime maior para compensar.
                ProcessNpcUpdates(slowNpcs, deltaTime * 3);
            }
        }
    }

    private void ProcessNpcUpdates(List<NpcInstance> npcs, float deltaTime)
    {
        var partitioner = Partitioner.Create(npcs);
        Parallel.ForEach(partitioner, (npc) =>
        {
            npc.Behavior?.Update(npc, deltaTime);

            if (!npc.IsStationary)
            {
                UpdateNpcPosition(npc, deltaTime);
                CheckIfNpcIsStuck(npc);
            }
        });
    }

    private void UpdateNpcPosition(NpcInstance npc, float deltaTime)
    {
        // <<< MUDANÇA >>> Usa o helper para calcular a distância no plano XZ.
        float distanceToDestination = Vector3Helper.Distance2D(npc.Position, npc.Destination);

        if (distanceToDestination < 0.1f)
        {
            SyncPositionIfMoved(npc);
            return;
        }

        float actualMoveSpeed = 5.0f * (npc.MovementSpeed / 100.0f);
        float moveAmount = actualMoveSpeed * deltaTime;

        if (moveAmount >= distanceToDestination)
        {
            // Chegou ao destino (horizontalmente)
            npc.Position = new Vector3(npc.Destination.X, npc.Position.Y, npc.Destination.Z);
        }
        else
        {
            // <<< MUDANÇA >>> Calcula a direção no plano XZ para garantir que não haja movimento vertical.
            Vector3 direction = npc.Destination - npc.Position;
            direction.Y = 0; // Ignora a componente Y
            direction = Vector3.Normalize(direction);
            npc.Position += direction * moveAmount;
        }

        _server.GridManager.UpdateEntity(npc);
        SyncPositionIfMoved(npc);
    }

    // (NOVO OU RESTAURADO) Adicione este método auxiliar à sua classe NpcAiManager
    private void SyncPositionIfMoved(NpcInstance npc)
    {
        const float SYNC_DISTANCE_THRESHOLD_SQR = 0.1f * 0.1f;

        Vector3 lastSentPos = _lastSentPositions.GetOrAdd(npc.Id, npc.Position);

        // <<< MUDANÇA >>> Usa o helper para comparar a distância quadrada no plano XZ.
        if (Vector3Helper.Distance2DSquared(npc.Position, lastSentPos) > SYNC_DISTANCE_THRESHOLD_SQR)
        {
            string posStr = $"{npc.Position.X.ToString(CultureInfo.InvariantCulture)},{npc.Position.Z.ToString(CultureInfo.InvariantCulture)}";
            _server.NetworkManager.BroadcastMessageToRelevantPlayers(npc.Position, $"NPC_MOVE|{npc.SessionId}|{posStr}");
            _lastSentPositions[npc.Id] = npc.Position; // O Id aqui ainda é o GUID, está certo.
        }
    }

    private void CheckIfNpcIsStuck(NpcInstance npc)
    {
        if (npc.CurrentState != NpcAiState.Idle && npc.CurrentState != NpcAiState.Attacking)
        {
            // <<< MUDANÇA >>> Usa o helper para verificar se houve movimento no plano XZ.
            if (Vector3Helper.Distance2D(npc.Position, npc.LastPosition) < 0.1f)
            {
                if ((_server.CurrentTimeUtc - npc.TimeAtLastPosition).TotalSeconds > 5)
                {
                    npc.CurrentState = NpcAiState.Idle;
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