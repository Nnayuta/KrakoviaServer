// AI/Behaviors/StationaryGuardBehavior.cs
using System.Numerics;

public class StationaryGuardBehavior : WanderingAggressiveBehavior
{
    public StationaryGuardBehavior(UDPServer server) : base(server) { }

    // (CORREÇÃO) Marcado como OVERRIDE.
    public override void Update(NpcInstance npc, float deltaTime)
    {
        if (Vector3.Distance(npc.Position, npc.SpawnPosition) > 0.1f)
        {
            npc.Position = npc.SpawnPosition;
        }
        SetNpcDestination(npc, npc.SpawnPosition);


        if (npc.CurrentState != NpcAiState.Attacking)
        {
            ICombatEntity? target = FindClosestPlayerInAggroRange(npc);
            if (target != null)
            {
                EngageTarget(npc, target);
                ChangeNpcState(npc, NpcAiState.Attacking);
            }
            else
            {
                 if(npc.TargetPlayerId != null) ResetAggro(npc);
                 ChangeNpcState(npc, NpcAiState.Idle);
            }
        }

        base.Update(npc, deltaTime);
    }

    // (CORREÇÃO) Marcado como OVERRIDE.
    protected override void HandleChasingState(NpcInstance npc)
    {
        ChangeNpcState(npc, NpcAiState.Attacking);
    }
}