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
        // Se já estamos em combate, usamos a lógica de combate da classe base, que é segura.
        if (npc.CurrentState == NpcAiState.Chasing || npc.CurrentState == NpcAiState.Attacking)
        {
            // O Update da classe base chamará HandleChasingState e HandleAttackingState,
            // que são perfeitos para o combate.
            base.Update(npc, deltaTime);
            return;
        }

        // Se precisamos retornar ao spawn, usamos o handler da classe base.
        if (npc.CurrentState == NpcAiState.ReturningToSpawn)
        {
            HandleReturningToSpawnState(npc);
            return;
        }

        // --- LÓGICA DE DECISÃO DO GUARDA (FORA DE COMBATE) ---
        // Se chegamos aqui, o guarda está ocioso ou patrulhando.

        // Prioridade 1: Ajudar aliados próximos que estão sendo atacados.
        ICombatEntity? targetToAssist = FindEnemyAttackingAlly(npc);
        if (targetToAssist != null)
        {
            // Console.WriteLine($"[Guard AI] {npc.Id} vai ajudar um aliado contra {targetToAssist.Id}");
            EngageTarget(npc, targetToAssist);
            return;
        }

        // Prioridade 2: Atacar monstros hostis que entram no raio de agressão.
        ICombatEntity? nearbyMonster = FindClosestEnemyMonster(npc);
        if (nearbyMonster != null)
        {
            // Console.WriteLine($"[Guard AI] {npc.Id} engajando monstro próximo {nearbyMonster.Id}");
            EngageTarget(npc, nearbyMonster);
            return;
        }

        // --- LÓGICA DE PATRULHA (SE NÃO HÁ AMEAÇAS) ---
        // Se não há inimigos, apenas executa a lógica de patrulha.
        // Chamamos nossos próprios handlers de estado para evitar a busca por jogadores da classe base.
        switch (npc.CurrentState)
        {
            case NpcAiState.Idle:
                Guard_HandleIdleState(npc);
                break;
            case NpcAiState.Patrolling:
                Guard_HandlePatrollingState(npc);
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