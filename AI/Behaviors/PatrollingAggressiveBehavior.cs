// AI/Behaviors/PatrollingAggressiveBehavior.cs
using System.Linq;
using System.Numerics;

public class PatrollingAggressiveBehavior : WanderingAggressiveBehavior
{
    public PatrollingAggressiveBehavior(UDPServer server) : base(server) { }

    // (CORREÇÃO) Marcado como OVERRIDE.
    public override void Update(NpcInstance npc, float deltaTime)
    {
        base.Update(npc, deltaTime);

        if (npc.CurrentState == NpcAiState.Chasing || npc.CurrentState == NpcAiState.Attacking)
        {
            return;
        }

        switch (npc.CurrentState)
        {
            case NpcAiState.Idle:
                // (CORREÇÃO) Chamando o método HandleIdleState da classe PAI (base) para evitar conflito de nome.
                // Mas na verdade, queremos nossa própria lógica aqui.
                HandleIdleState(npc);
                break;
            case NpcAiState.Patrolling:
                HandlePatrollingState(npc);
                break;
        }
    }

    // Renomeado para não conflitar, ou podemos sobrescrever o da base. Vamos sobrescrever.
    protected override void HandleIdleState(NpcInstance npc)
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

    // Este é um método novo, então não precisa de override.
    private void HandlePatrollingState(NpcInstance npc)
    {
        if (npc.PatrolPath == null || !npc.PatrolPath.Any()) return;

        if (Vector3.Distance(npc.Position, npc.Destination) < 1.0f)
        {
            ChangeNpcState(npc, NpcAiState.Idle);
            npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(3, 8));
            npc.CurrentPatrolIndex = (npc.CurrentPatrolIndex + 1) % npc.PatrolPath.Count;
        }
    }
}