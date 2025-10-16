// AI/Behaviors/PatrollingGuardBehavior.cs
using System;
using System.Linq;
using System.Numerics;

// Herda de PatrollingAggressiveBehavior para reutilizar os MÉTODOS DE COMBATE e os MÉTODOS AUXILIARES,
// mas vamos sobrescrever completamente a lógica de decisão principal no UPDATE.
public class PatrollingGuardBehavior : PatrollingAggressiveBehavior
{
    private const float ASSIST_RADIUS = 25.0f;

    public PatrollingGuardBehavior(UDPServer server) : base(server) { }

    /// <summary>
    /// (LÓGICA DE DECISÃO TOTALMENTE SUBSTITUÍDA)
    /// Este método controla o comportamento do guarda, garantindo que ele nunca
    /// ataque jogadores a menos que seja provocado.
    /// </summary>
    public override void Update(NpcInstance npc, float deltaTime)
    {
        // Obtém o alvo atual no início
        ICombatEntity? target = GetCurrentTarget(npc);

        // --- LÓGICA DE MANUTENÇÃO DE COMBATE ---
        if (target != null)
        {
            // Se o alvo morreu ou quebrou o leash, abandona o combate.
            if (target.IsDead || Vector3Helper.Distance2D(npc.Position, npc.AggroPosition) > npc.BaseData.LeashRange)
            {
                ResetAggro(npc);
                HandleReturningToSpawnState(npc); // Inicia o retorno imediatamente
                return;
            }
            else // O alvo é válido, continua o combate
            {
                float distanceToTarget = Vector3Helper.Distance2D(npc.Position, target.Position);
                if (distanceToTarget > npc.BaseData.MaxAbilityRange)
                {
                    HandleChasingState(npc);
                }
                else
                {
                    // Para o guarda patrulheiro, HandleAttackingState precisa do alvo
                    // Vamos precisar de uma versão sobrecarregada ou passar o alvo.
                    // Por simplicidade, vamos chamar a lógica de ataque diretamente aqui.
                    base.HandleAttackingState(npc);
                }
                return; // Lógica de combate concluída.
            }
        }

        // --- LÓGICA DE DECISÃO FORA DE COMBATE (se target == null) ---
        // Prioridade 1: Ajudar aliados
        ICombatEntity? targetToAssist = FindEnemyAttackingAlly(npc);
        if (targetToAssist != null)
        {
            EngageTarget(npc, targetToAssist);
            return;
        }

        // Prioridade 2: Atacar monstros próximos
        ICombatEntity? nearbyMonster = FindClosestEnemyMonster(npc);
        if (nearbyMonster != null)
        {
            EngageTarget(npc, nearbyMonster);
            return;
        }

        // Prioridade 3: Lógica de patrulha
        switch (npc.CurrentState)
        {
            case NpcAiState.Idle:
                Guard_HandleIdleState(npc);
                break;
            case NpcAiState.Patrolling:
                Guard_HandlePatrollingState(npc);
                break;
            case NpcAiState.ReturningToSpawn:
                HandleReturningToSpawnState(npc);
                break;
        }
    }

    /// <summary>
    /// Lógica de estado Idle para o guarda: espera um pouco e depois continua a patrulha.
    /// </summary>
    private void Guard_HandleIdleState(NpcInstance npc)
    {
        if (_server.CurrentTimeUtc < npc.NextActionTime) return;
        if (npc.PatrolPath == null || !npc.PatrolPath.Any())
        {
            ChangeNpcState(npc, NpcAiState.Idle);
            return;
        }

        SetNpcDestination(npc, npc.PatrolPath[npc.CurrentPatrolIndex]);
        ChangeNpcState(npc, NpcAiState.Patrolling);
    }

    /// <summary>
    /// Lógica de estado Patrolling para o guarda: anda até o próximo ponto da rota.
    /// </summary>
    private void Guard_HandlePatrollingState(NpcInstance npc)
    {
        if (npc.PatrolPath == null || !npc.PatrolPath.Any()) return;

        if (Vector3.Distance(npc.Position, npc.Destination) < 1.0f)
        {
            ChangeNpcState(npc, NpcAiState.Idle);
            npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(3, 8));
            npc.CurrentPatrolIndex = (npc.CurrentPatrolIndex + 1) % npc.PatrolPath.Count;
        }
    }

    /// <summary>
    /// Procura por monstros inimigos que estão atacando um jogador perto do guarda.
    /// </summary>
    private NpcInstance? FindEnemyAttackingAlly(NpcInstance guard)
    {
        var nearbyEntities = _server.GridManager.GetEntitiesInRadius(guard.Position, ASSIST_RADIUS);

        return nearbyEntities
            .OfType<NpcInstance>()
            .Where(npc =>
                !npc.IsDead &&
                npc.BaseData.Faction == NpcFaction.Enemy &&
                npc.TargetPlayerId != null) // Verifica se o monstro tem um alvo (que será um jogador)
            .OrderBy(npc => Vector3.DistanceSquared(guard.Position, npc.Position))
            .FirstOrDefault();
    }

    /// <summary>
    /// Procura pelo monstro inimigo mais próximo, ignorando completamente os jogadores.
    /// </summary>
    private NpcInstance? FindClosestEnemyMonster(NpcInstance guard)
    {
        var nearbyEntities = _server.GridManager.GetEntitiesInRadius(guard.Position, guard.BaseData.AggroRange);

        return nearbyEntities
            .OfType<NpcInstance>()
            .Where(npc =>
                !npc.IsDead &&
                npc.BaseData.Faction == NpcFaction.Enemy)
            .OrderBy(npc => Vector3.DistanceSquared(guard.Position, npc.Position))
            .FirstOrDefault();
    }

    /// <summary>
    /// Garante que o guarda revide se for atacado por QUALQUER entidade.
    /// </summary>
    public override void OnDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        if (npc.Id == attacker.Id) return;

        if (npc.TargetPlayerId != attacker.Id)
        {
            EngageTarget(npc, attacker);
        }
        npc.ThreatTable[attacker.Id] = npc.ThreatTable.GetValueOrDefault(attacker.Id, 0) + 100f;
    }
}