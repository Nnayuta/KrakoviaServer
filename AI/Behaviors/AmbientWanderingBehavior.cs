// AI/Behaviors/AmbientWanderingBehavior.cs
using System.Numerics;

public class AmbientWanderingBehavior : BaseBehavior
{
    public AmbientWanderingBehavior(UDPServer server) : base(server) { }

    public override void Update(NpcInstance npc, float deltaTime)
    {
        // Lógica de vagueio idêntica à do monstro agressivo, mas sem nunca checar por alvos.
        switch (npc.CurrentState)
        {
            case NpcAiState.Idle:
                if (_server.CurrentTimeUtc < npc.NextActionTime) return;
                SetNpcDestination(npc, FindWanderPoint(npc));
                ChangeNpcState(npc, NpcAiState.Wandering);
                npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(4, 9));
                break;

            case NpcAiState.Wandering:
                if (_server.CurrentTimeUtc >= npc.NextActionTime || Vector3.Distance(npc.Position, npc.Destination) < 1.5f)
                {
                    SetNpcDestination(npc, npc.Position);
                    ChangeNpcState(npc, NpcAiState.Idle);
                    npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(5, 11));
                }
                break;
        }
    }

    // Intencionalmente vazio. Não reage a dano.
    public override void OnDamaged(NpcInstance npc, ICombatEntity attacker) { }
}