// AI/Behaviors/PassiveAggressiveBehavior.cs
public class PassiveAggressiveBehavior : WanderingAggressiveBehavior
{
    public PassiveAggressiveBehavior(UDPServer server) : base(server) { }

    public override void Update(NpcInstance npc, float deltaTime)
    {
        // PRIORIDADE 0: Casting
        if (npc.CurrentState == NpcAiState.Casting)
        {
            HandleCastingState(npc);
            return;
        }

        ICombatEntity? target = GetCurrentTarget(npc);

        // Se NÃO temos um alvo (não estamos em combate)...
        if (target == null)
        {
            // ... simplesmente ficamos parados e garantimos que o estado é Idle.
            if (npc.CurrentState != NpcAiState.Idle)
            {
                ChangeNpcState(npc, NpcAiState.Idle);
                SetNpcDestination(npc, npc.Position); // Garante que ele pare de se mover
            }

            // Se estamos voltando para o spawn, continuamos voltando.
            if (npc.CurrentState == NpcAiState.ReturningToSpawn)
            {
                HandleReturningToSpawnState(npc);
            }

            return; // Fim da lógica. Não procura por alvos.
        }

        // Se TEMOS um alvo (estamos em combate)...
        // Usamos a mesma lógica de manutenção de combate dos outros.
        if (target.IsDead || Vector3Helper.Distance2D(npc.Position, npc.AggroPosition) > npc.BaseData.LeashRange)
        {
            ResetAggro(npc); // Abandona o combate.
        }
        else
        {
            float distanceToTarget = Vector3Helper.Distance2D(npc.Position, target.Position);
            if (distanceToTarget > npc.BaseData.MaxAbilityRange)
            {
                HandleChasingState(npc); // Persegue
            }
            else
            {
                HandleAttackingState(npc); // Ataca
            }
        }
    }

    // O método OnDamaged é o CORAÇÃO deste comportamento.
    // É a única forma de ele entrar em combate.
    public override void OnDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        if (npc.Id == attacker.Id || npc.IsDead) return;

        // Se já não temos um alvo, ou se fomos atacados por outra pessoa,
        // engajamos o novo atacante.
        if (GetCurrentTarget(npc) == null || npc.TargetPlayerId != attacker.Id)
        {
            // Usa o EngageTarget da classe base, que já lida com a promoção de tier!
            EngageTarget(npc, attacker);
        }

        // Adiciona threat normalmente.
        npc.ThreatTable[attacker.Id] = npc.ThreatTable.GetValueOrDefault(attacker.Id, 0) + 1.0f;
    }
}