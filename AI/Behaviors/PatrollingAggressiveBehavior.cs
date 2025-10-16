// AI/Behaviors/PatrollingAggressiveBehavior.cs
using System.Linq;
using System.Numerics;

public class PatrollingAggressiveBehavior : WanderingAggressiveBehavior
{
    public PatrollingAggressiveBehavior(UDPServer server) : base(server) { }

    // (CORREÇÃO) Marcado como OVERRIDE.
    public override void Update(NpcInstance npc, float deltaTime)
    {
        // PRIORIDADE 0: Casting (copiado da base)
        if (npc.CurrentState == NpcAiState.Casting)
        {
            HandleCastingState(npc);
            return;
        }

        ICombatEntity? target = GetCurrentTarget(npc);

        // --- LÓGICA DE COMBATE ---
        if (target != null)
        {
            if (target.IsDead || Vector3Helper.Distance2D(npc.Position, npc.AggroPosition) > npc.BaseData.LeashRange)
            {
                ResetAggro(npc);
            }
            else
            {
                float distanceToTarget = Vector3Helper.Distance2D(npc.Position, target.Position);
                if (distanceToTarget > npc.BaseData.MaxAbilityRange)
                {
                    HandleChasingState(npc);
                }
                else
                {
                    HandleAttackingState(npc);
                }
            }
        }
        // --- LÓGICA FORA DE COMBATE ---
        else
        {
            // <<<< A GRANDE MUDANÇA ESTÁ AQUI >>>>
            // Em vez de procurar só por players, procuramos pelo inimigo mais próximo.
            target = FindClosestEnemy(npc, npc.BaseData.AggroRange);

            if (target != null)
            {
                EngageTarget(npc, target); // Encontrou um inimigo, entra em combate!
            }
            else // Não encontrou inimigos, executa a rotina de patrulha.
            {
                if (npc.CurrentState == NpcAiState.ReturningToSpawn)
                {
                    HandleReturningToSpawnState(npc);
                }
                else if (npc.CurrentState == NpcAiState.Patrolling)
                {
                    HandlePatrollingState(npc);
                }
                else // Por padrão, fica Idle esperando o próximo ponto de patrulha.
                {
                    HandleIdleState(npc);
                }
            }
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