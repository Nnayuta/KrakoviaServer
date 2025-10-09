// AI/Behaviors/PatrollingGuardBehavior.cs
using System.Linq;
using System.Numerics;

public class PatrollingGuardBehavior : PatrollingAggressiveBehavior
{
    private const float ASSIST_RADIUS = 25.0f; // Raio em que o guarda vai ajudar aliados

    public PatrollingGuardBehavior(UDPServer server) : base(server) { }

    // (A MUDANÇA PRINCIPAL) Sobrescrevemos o método Update para mudar a lógica de decisão.
    public override void Update(NpcInstance npc, float deltaTime)
    {
        // Se já estamos em combate, a lógica da classe base (perseguir, atacar, retornar) é ótima.
        if (npc.CurrentState == NpcAiState.Chasing ||
            npc.CurrentState == NpcAiState.Attacking ||
            npc.CurrentState == NpcAiState.ReturningToSpawn)
        {
            base.Update(npc, deltaTime);
            return;
        }

        // --- NOVA LÓGICA DE DECISÃO DO GUARDA ---
        // Se estamos fora de combate, procuramos um motivo para entrar.

        // Prioridade 1: Ajudar aliados próximos que estão em combate
        ICombatEntity? targetToAssist = FindEnemyAttackingAlly(npc);
        if (targetToAssist != null)
        {
            EngageTarget(npc, targetToAssist);
            return;
        }

        // Prioridade 2: Atacar monstros hostis que se aproximam
        ICombatEntity? nearbyMonster = FindClosestEnemyMonster(npc);
        if (nearbyMonster != null)
        {
            EngageTarget(npc, nearbyMonster);
            return;
        }

        // Se não há inimigos para atacar, apenas executa a lógica de patrulha da classe base.
        // A classe base vai chamar HandleIdleState e HandlePatrollingState, que são perfeitos.
        base.Update(npc, deltaTime);
    }

    // (NOVO) Método para encontrar monstros que estão atacando jogadores ou outros NPCs amigos.
    private NpcInstance? FindEnemyAttackingAlly(NpcInstance guard)
    {
        // Procura por todos os monstros inimigos ativos
        return _server.ActiveNpcs.Values
            .Where(npc =>
                npc.IsActive &&
                !npc.IsDead &&
                npc.BaseData.Faction == NpcFaction.Enemy && // É um monstro inimigo
                Vector3.Distance(guard.Position, npc.Position) <= ASSIST_RADIUS && // Está perto do guarda
                npc.TargetPlayerId != null) // E está atacando um jogador
            .OrderBy(npc => Vector3.Distance(guard.Position, npc.Position))
            .FirstOrDefault();
    }

    // (NOVO) Método para encontrar o monstro inimigo mais próximo, ignorando jogadores.
    private NpcInstance? FindClosestEnemyMonster(NpcInstance guard)
    {
        return _server.ActiveNpcs.Values
            .Where(npc =>
                npc.IsActive &&
                !npc.IsDead &&
                npc.BaseData.Faction == NpcFaction.Enemy && // Filtra apenas por NPCs inimigos
                Vector3.Distance(guard.Position, npc.Position) <= guard.BaseData.AggroRange)
            .OrderBy(npc => Vector3.Distance(guard.Position, npc.Position))
            .FirstOrDefault();
    }

    // (IMPORTANTE) Sobrescrevemos o OnDamaged para garantir que ele revide.
    public override void OnDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        // Se não estivermos já em combate com este alvo, engaja.
        if (npc.TargetPlayerId != attacker.Id)
        {
            // A lógica de EngageTarget da classe base é perfeita para isso.
            EngageTarget(npc, attacker);
        }

        // Adiciona ameaça
        npc.ThreatTable[attacker.Id] = npc.ThreatTable.GetValueOrDefault(attacker.Id, 0) + 100f; // Dano direto gera mais ameaça
    }
}