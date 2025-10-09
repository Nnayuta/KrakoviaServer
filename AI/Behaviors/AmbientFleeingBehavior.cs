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
        // --- LÓGICA DE FUGA (SEMPRE TEM PRIORIDADE) ---
        // Se não estivermos já fugindo, verifica se devemos começar.
        if (npc.CurrentState != NpcAiState.Fleeing)
        {
            Player? nearbyPlayer = FindClosestPlayer(npc, FLEE_TRIGGER_RANGE);
            if (nearbyPlayer != null)
            {
                StartFleeing(npc, nearbyPlayer);
                return; // Já tomou a decisão de fugir, não faz mais nada neste tick.
            }
        }

        // --- LÓGICA DE VAGUEIO (WANDERING) ---
        // Se o código chegou até aqui, significa que não há jogadores por perto.
        // O NPC deve se comportar normalmente, andando pelo mapa.
        switch (npc.CurrentState)
        {
            case NpcAiState.Idle:
                // Se estiver ocioso, decide se deve começar a andar.
                if (_server.CurrentTimeUtc < npc.NextActionTime) return;
                SetNpcDestination(npc, FindWanderPoint(npc));
                ChangeNpcState(npc, NpcAiState.Wandering);
                npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(4, 9));
                break;

            case NpcAiState.Wandering:
                // Se estiver andando, decide se deve parar.
                if (_server.CurrentTimeUtc >= npc.NextActionTime || Vector3.Distance(npc.Position, npc.Destination) < 1.5f)
                {
                    SetNpcDestination(npc, npc.Position); // Para de se mover
                    ChangeNpcState(npc, NpcAiState.Idle);
                    npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(5, 11)); // Pausa antes da próxima ação
                }
                break;

            case NpcAiState.Fleeing:
                // Se estiver fugindo, verifica se o tempo de fuga acabou.
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

        // Esta chamada envia a mensagem "NPC_MOVE" para o cliente.
        SetNpcDestination(npc, fleeDestination);
        ChangeNpcState(npc, NpcAiState.Fleeing);
    }
}