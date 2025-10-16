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
        // Apenas atualiza o estado de "intenção" do NPC no servidor.
        // Nenhuma mensagem de rede é enviada aqui.
        if (Vector3.DistanceSquared(npc.Destination, newDestination) < 0.01f)
        {
            return;
        }
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

    protected ICombatEntity? FindClosestEnemy(NpcInstance npc, float range)
    {
        var nearbyEntities = _server.GridManager.GetEntitiesInRadius(npc.Position, range);

        // A "mágica" está aqui. Procuramos por qualquer entidade de combate...
        return nearbyEntities
            .OfType<ICombatEntity>()
            // ...que não seja ela mesma, não esteja morta, e NÃO SEJA AMIGÁVEL.
            .Where(target => target.Id != npc.Id && !target.IsDead && !AreEntitiesFriendly(npc, target))
            .OrderBy(target => Vector3.DistanceSquared(npc.Position, target.Position))
            .FirstOrDefault();
    }

    // Adicione este método aqui também, se ainda não estiver.
    // Ele será usado pelo FindClosestEnemy.
    protected bool AreEntitiesFriendly(ICombatEntity entityA, ICombatEntity entityB)
    {
        if (entityA is Player && entityB is Player) return true;
        // Um guarda Friendly/Neutral é amigável a um Player
        if (entityA is Player && entityB is NpcInstance npc) return npc.BaseData.Faction != NpcFaction.Enemy;
        if (entityA is NpcInstance npc2 && entityB is Player) return npc2.BaseData.Faction != NpcFaction.Enemy;
        // Dois NPCs são amigáveis se tiverem a mesma facção
        if (entityA is NpcInstance n1 && entityB is NpcInstance n2) return n1.BaseData.Faction == n2.BaseData.Faction;
        return false;
    }
}