// AI/Behaviors/AmbientFleeingBehavior.cs
using System.Numerics;

public class AmbientFleeingBehavior : BaseBehavior
{
    private const float FLEE_TRIGGER_RANGE = 8.0f;
    private const float FLEE_DISTANCE = 15.0f;
    private const float FLEE_DURATION_SECONDS = 6.0f;

    public AmbientFleeingBehavior(UDPServer server) : base(server) { }

    public override void Update(NpcInstance npc, float deltaTime)
    {
        switch (npc.CurrentState)
        {
            case NpcAiState.Idle:
            case NpcAiState.Wandering:
                // Procura por jogadores próximos para fugir
                Player? nearbyPlayer = FindClosestPlayer(npc, FLEE_TRIGGER_RANGE);
                if (nearbyPlayer != null)
                {
                    StartFleeing(npc, nearbyPlayer);
                }
                break;

            case NpcAiState.Fleeing:
                // Verifica se o tempo de fuga acabou ou se chegou ao destino
                if ((_server.CurrentTimeUtc - npc.LastStateChangeTime).TotalSeconds > FLEE_DURATION_SECONDS ||
                    Vector3.Distance(npc.Position, npc.Destination) < 1.5f)
                {
                    ChangeNpcState(npc, NpcAiState.Idle);
                    npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(5, 12)); // Acalma-se por um tempo
                }
                break;
        }
    }

    // Se for atacado, também foge.
    public override void OnDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        StartFleeing(npc, attacker);
    }

    private void StartFleeing(NpcInstance npc, ICombatEntity threat)
    {
        // Calcula a direção oposta à ameaça
        Vector3 fleeDirection = Vector3.Normalize(npc.Position - threat.Position);
        Vector3 fleeDestination = npc.Position + fleeDirection * FLEE_DISTANCE;

        SetNpcDestination(npc, fleeDestination);
        ChangeNpcState(npc, NpcAiState.Fleeing);
    }
}