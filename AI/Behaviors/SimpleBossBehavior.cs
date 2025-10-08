// AI/Behaviors/SimpleBossBehavior.cs
using System;
using System.Numerics;

public class SimpleBossBehavior : WanderingAggressiveBehavior
{
    private enum BossPhase { Phase1, Phase2 }
    private BossPhase _currentPhase = BossPhase.Phase1;

    // Habilidades Específicas do Chefe (você define os IDs nos seus dados)
    private const string AOE_SLAM_ABILITY_ID = "boss_aoe_slam";
    private const string SUMMON_MINIONS_ABILITY_ID = "boss_summon_minions";

    private DateTime _nextSpecialAbilityTime = DateTime.MinValue;

    public SimpleBossBehavior(UDPServer server) : base(server) { }

    public override void Update(NpcInstance npc, float deltaTime)
    {
        // A lógica de combate base (perseguir, auto-ataque) ainda é útil.
        base.Update(npc, deltaTime);

        // Se não estiver em combate, não faz nada especial.
        if (npc.CurrentState != NpcAiState.Attacking)
        {
            // Se ele saiu de combate, reseta para a fase 1.
            if (_currentPhase != BossPhase.Phase1) _currentPhase = BossPhase.Phase1;
            return;
        }

        // --- LÓGICA DE FASES ---
        float healthPercentage = npc.CurrentHealth / npc.MaxHealth;

        // Transição para a Fase 2 (só acontece uma vez)
        if (_currentPhase == BossPhase.Phase1 && healthPercentage < 0.5f)
        {
            _currentPhase = BossPhase.Phase2;
            // Grita "ENRAGE!" ou algo assim
            _server.NetworkManager.BroadcastMessageToAll($"NPC_CHAT|{npc.Id}|Chega de brincadeira!");
            // Reseta o timer de habilidade para usar a nova habilidade imediatamente
            _nextSpecialAbilityTime = _server.CurrentTimeUtc;
        }

        // --- LÓGICA DE HABILIDADES POR FASE ---
        if (_server.CurrentTimeUtc >= _nextSpecialAbilityTime)
        {
            if (_currentPhase == BossPhase.Phase1)
            {
                // Usa o Ataque em Área
                UseAbility(npc, AOE_SLAM_ABILITY_ID);
                _nextSpecialAbilityTime = _server.CurrentTimeUtc.AddSeconds(10); // Usa a cada 10s
            }
            else // Fase 2
            {
                // Invoca os ajudantes
                UseAbility(npc, SUMMON_MINIONS_ABILITY_ID);
                _nextSpecialAbilityTime = _server.CurrentTimeUtc.AddSeconds(15); // Invoca a cada 15s
            }
        }
    }

    private void UseAbility(NpcInstance npc, string abilityId)
    {
        ICombatEntity? target = GetCurrentTarget(npc);
        if (target == null) return;

        if (DataManager.Abilities.TryGetValue(abilityId, out var ability))
        {
            // Força o chefe a parar e se comprometer com o ataque
            SetNpcDestination(npc, npc.Position);
            _server.CombatManager.ProcessAbilityRequest(npc, abilityId, target.Id);
        }
    }
}