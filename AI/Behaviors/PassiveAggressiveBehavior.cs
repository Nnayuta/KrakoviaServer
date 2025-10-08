// AI/Behaviors/PassiveAggressiveBehavior.cs
public class PassiveAggressiveBehavior : WanderingAggressiveBehavior
{
    public PassiveAggressiveBehavior(UDPServer server) : base(server) { }

    // (CORREÇÃO) Marcado como OVERRIDE.
    public override void Update(NpcInstance npc, float deltaTime)
    {
        if (npc.CurrentState != NpcAiState.Chasing &&
            npc.CurrentState != NpcAiState.Attacking &&
            npc.CurrentState != NpcAiState.ReturningToSpawn)
        {
            if (npc.CurrentState != NpcAiState.Idle)
            {
                 ChangeNpcState(npc, NpcAiState.Idle);
                 SetNpcDestination(npc, npc.Position);
            }
            return;
        }

        // Chama a lógica de combate da classe mãe.
        base.Update(npc, deltaTime);
    }
}