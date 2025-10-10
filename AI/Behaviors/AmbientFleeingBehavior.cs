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
        // --- Hierarquia de Decisão ---

        // PRIORIDADE MÁXIMA: Já estou fugindo?
        if (npc.CurrentState == NpcAiState.Fleeing)
        {
            // Se estou fugindo, verifico se devo parar.
            if ((_server.CurrentTimeUtc - npc.LastStateChangeTime).TotalSeconds > FLEE_DURATION_SECONDS ||
                Vector3.Distance(npc.Position, npc.Destination) < 1.5f)
            {
                ChangeNpcState(npc, NpcAiState.Idle);
                SetNpcDestination(npc, npc.Position); // Garante que ele pare no lugar
                npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(5, 12)); // Acalma-se por um tempo
            }
            return; // Lógica de fuga concluída, não faz mais nada.
        }

        // PRIORIDADE 2: Preciso começar a fugir?
        // Isso só é verificado se não estivermos já fugindo.
        Player? nearbyPlayer = FindClosestPlayer(npc, FLEE_TRIGGER_RANGE);
        if (nearbyPlayer != null)
        {
            StartFleeing(npc, nearbyPlayer);
            return; // Decisão de fugir tomada, não faz mais nada.
        }

        // PRIORIDADE 3 (Padrão): Comportamento normal de vagueio (Wandering)
        // Este código só é alcançado se o NPC não estiver fugindo e não houver jogadores por perto.
        switch (npc.CurrentState)
        {
            case NpcAiState.Idle:
                // Se estiver ocioso e o tempo de pausa acabou, começa a andar.
                if (_server.CurrentTimeUtc < npc.NextActionTime) return;

                SetNpcDestination(npc, FindWanderPoint(npc));
                ChangeNpcState(npc, NpcAiState.Wandering);
                // Define por quanto tempo ele vai andar antes de reavaliar
                npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(4, 9));
                break;

            case NpcAiState.Wandering:
                // Se o tempo de caminhada acabou ou chegou ao destino, para e fica ocioso.
                if (_server.CurrentTimeUtc >= npc.NextActionTime || Vector3.Distance(npc.Position, npc.Destination) < 1.5f)
                {
                    SetNpcDestination(npc, npc.Position); // Para de se mover
                    ChangeNpcState(npc, NpcAiState.Idle);
                    // Define o tempo de pausa antes da próxima caminhada
                    npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(5, 11));
                }
                break;
        }
    }

    public override void OnDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        StartFleeing(npc, attacker);
    }

    private void StartFleeing(NpcInstance npc, ICombatEntity threat)
    {
        Vector3 fleeDirection = Vector3.Normalize(npc.Position - threat.Position);

        // Garante que a direção não seja um vetor zero se o jogador estiver exatamente na mesma posição
        if (fleeDirection == Vector3.Zero)
        {
            fleeDirection = new Vector3((float)_threadRandom.Value.NextDouble() * 2 - 1, 0, (float)_threadRandom.Value.NextDouble() * 2 - 1);
            fleeDirection = Vector3.Normalize(fleeDirection);
        }

        Vector3 fleeDestination = npc.Position + fleeDirection * FLEE_DISTANCE;

        SetNpcDestination(npc, fleeDestination);
        ChangeNpcState(npc, NpcAiState.Fleeing);
    }
}