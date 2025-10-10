// AI/Behaviors/BaseBehavior.cs
using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;

public abstract class BaseBehavior : INpcBehavior
{
    protected readonly UDPServer _server;
    protected readonly ThreadLocal<Random> _threadRandom = new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

    protected BaseBehavior(UDPServer server)
    {
        _server = server;
    }

    public abstract void Update(NpcInstance npc, float deltaTime);

    public virtual void OnDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        // A maioria dos comportamentos não agressivos não fará nada.
    }

    // --- MÉTODOS DE ESTADO (VIRTUAL) ---
    // (CORREÇÃO) Adicionamos versões virtuais vazias aqui. As classes que os usam
    // (como WanderingAggressive) irão sobrescrevê-los com a lógica real.
    // Isso permite que outras classes (como Patrolling) também os sobrescrevam.
    protected virtual void HandleIdleState(NpcInstance npc) { }
    protected virtual void HandleWanderingState(NpcInstance npc) { }
    protected virtual void HandleChasingState(NpcInstance npc) { }
    protected virtual void HandleAttackingState(NpcInstance npc) { }
    protected virtual void HandleReturningToSpawnState(NpcInstance npc) { }


    // --- MÉTODOS UTILITÁRIOS COMUNS (PROTECTED) ---

    protected Player? FindClosestPlayer(NpcInstance npc, float range)
    {
        var nearbyEntities = _server.GridManager.GetEntitiesInRadius(npc.Position, range);

        // Agora, filtramos e ordenamos apenas a pequena lista de entidades próximas
        return nearbyEntities
            .OfType<Player>() // Pega apenas as entidades que são jogadores
            .Where(p => !p.IsDead)
            .OrderBy(p => Vector3.DistanceSquared(npc.Position, p.Position)) // Usa DistanceSquared para ser mais rápido
            .FirstOrDefault();
    }

    protected void ChangeNpcState(NpcInstance npc, NpcAiState newState)
    {
        if (npc.CurrentState != newState)
        {
            npc.CurrentState = newState;
            npc.LastStateChangeTime = _server.CurrentTimeUtc;
        }
    }

    protected void SetNpcDestination(NpcInstance npc, Vector3 newDestination)
    {
        // Apenas atualiza o estado interno do NPC. Nenhuma mensagem de rede é enviada aqui.
        if (Vector3.Distance(npc.Destination, newDestination) < 0.1f) return;
        npc.Destination = newDestination;
    }

    protected Vector3 FindWanderPoint(NpcInstance npc)
    {
        float wanderRadius = 15f;
        float angle = (float)(_threadRandom.Value.NextDouble() * 2 * Math.PI);
        var offset = new Vector3((float)Math.Cos(angle) * wanderRadius, 0, (float)Math.Sin(angle) * wanderRadius);
        var potentialPoint = npc.Position + offset;

        if (Vector3.Distance(potentialPoint, npc.SpawnPosition) > npc.BaseData.LeashRange)
        {
            potentialPoint = npc.SpawnPosition;
        }
        potentialPoint.Y = npc.SpawnPosition.Y;
        return potentialPoint;
    }
}