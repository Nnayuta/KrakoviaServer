using System;
using System.Linq;
using System.Numerics;

// Herda de BaseBehavior para ser independente
public class StationaryGuardBehavior : BaseBehavior
{
    public StationaryGuardBehavior(UDPServer server) : base(server) { }

    public override void Update(NpcInstance npc, float deltaTime)
    {
        // Garante que o guarda esteja em seu posto
        if (Vector3.DistanceSquared(npc.Position, npc.SpawnPosition) > 0.01f)
        {
            npc.Position = npc.SpawnPosition;
            npc.Destination = npc.SpawnPosition;
        }

        // Se estiver retornando ao spawn (após o leash quebrar), reseta o estado
        if (npc.CurrentState == NpcAiState.ReturningToSpawn)
        {
            HandleReturningToSpawnState(npc);
            return;
        }

        // --- LÓGICA DE COMBATE ---
        ICombatEntity? target = GetCurrentTarget(npc);

        // Se não temos um alvo, procuramos por um.
        if (target == null)
        {
            target = FindClosestPlayerInAggroRange(npc);
            if (target != null)
            {
                EngageTarget(npc, target);
            }
            else // Sem alvo encontrado, garante que está em Idle.
            {
                ChangeNpcState(npc, NpcAiState.Idle);
                return;
            }
        }

        // Se chegamos aqui, temos um alvo.

        // Verifica se o alvo ainda é válido
        if (target.IsDead || Vector3.Distance(npc.Position, npc.AggroPosition) > npc.BaseData.LeashRange)
        {
            ResetAggro(npc);
            return;
        }

        // Se o alvo é válido, executa a lógica de ataque.
        HandleAttackingState(npc, target);
    }

    // Método de ataque agora recebe o alvo para evitar procurá-lo novamente
    private void HandleAttackingState(NpcInstance npc, ICombatEntity target)
    {
        FaceTarget(npc, target);

        // Se o alvo sair muito do alcance, reseta.
        const float ATTACK_RANGE_BUFFER = 2.0f;
        if (Vector3.Distance(npc.Position, target.Position) > npc.BaseData.MaxAbilityRange + ATTACK_RANGE_BUFFER)
        {
            ResetAggro(npc);
            return;
        }

        // --- Lógica de Auto-Ataque (Completa) ---
        if (npc.BaseData.AutoAttackAbilityID != null && _server.CurrentTimeUtc >= npc.NextAutoAttackTime)
        {
            if (DataManager.Abilities.TryGetValue(npc.BaseData.AutoAttackAbilityID, out var autoAttack) &&
                Vector3.Distance(npc.Position, target.Position) <= autoAttack.Range)
            {
                _server.CombatManager.ProcessAbilityRequest(npc, autoAttack.ID, target.Id);
                npc.NextAutoAttackTime = _server.CurrentTimeUtc.AddSeconds(npc.BaseData.SwingTimer);
            }
        }

        // --- Lógica de Habilidade Especial (Completa, requer o método ChooseBestSpecialAbility) ---
        if (_server.CurrentTimeUtc >= npc.GlobalCooldownEndTime)
        {
            AbilityData? specialAbility = ChooseBestSpecialAbility(npc, target);
            if (specialAbility != null)
            {
                SetNpcDestination(npc, npc.Position); // Para de se mover para castar
                _server.CombatManager.ProcessAbilityRequest(npc, specialAbility.ID, target.Id);
                npc.GlobalCooldownEndTime = _server.CurrentTimeUtc.AddSeconds(1.5);
            }
        }
    }

    public override void OnDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        if (npc.Id == attacker.Id || npc.IsDead) return;

        // Se já está atacando, apenas adiciona threat. Senão, engaja.
        if (GetCurrentTarget(npc) == null)
        {
            EngageTarget(npc, attacker);
        }
        npc.ThreatTable[attacker.Id] = npc.ThreatTable.GetValueOrDefault(attacker.Id, 0) + 1.0f;
    }

    // --- MÉTODOS AUXILIARES ---
    protected ICombatEntity? GetCurrentTarget(NpcInstance npc) => _server.ConnectedPlayers.Values.FirstOrDefault(p => p.Id == npc.TargetPlayerId && !p.IsDead);
    protected Player? FindClosestPlayerInAggroRange(NpcInstance npc) => FindClosestPlayer(npc, npc.BaseData.AggroRange);

    protected void EngageTarget(NpcInstance npc, ICombatEntity target)
    {
        npc.TargetPlayerId = target.Id;
        npc.ThreatTable[target.Id] = npc.ThreatTable.GetValueOrDefault(target.Id, 0) + 1.0f;
        npc.AggroPosition = npc.Position;
        ChangeNpcState(npc, NpcAiState.Attacking);
    }

    protected void ResetAggro(NpcInstance npc)
    {
        npc.TargetPlayerId = null;
        npc.ThreatTable.Clear();
        npc.AggroPosition = npc.SpawnPosition;
        ChangeNpcState(npc, NpcAiState.ReturningToSpawn);
    }

    protected override void HandleReturningToSpawnState(NpcInstance npc)
    {
        // Retorno instantâneo
        npc.CurrentHealth = npc.MaxHealth;
        npc.CurrentResource = npc.MaxResource;
        ChangeNpcState(npc, NpcAiState.Idle);
    }

    // Copiado do WanderingAggressiveBehavior
    protected AbilityData? ChooseBestSpecialAbility(NpcInstance npc, ICombatEntity target)
    {
        var candidates = new System.Collections.Generic.List<AbilityData>();
        foreach (var abilityId in npc.BaseData.AbilityIDs)
        {
            if (abilityId == npc.BaseData.AutoAttackAbilityID) continue;
            if (!DataManager.Abilities.TryGetValue(abilityId, out var ability)) continue;
            if (IsOnCooldown(npc, ability.ID)) continue;
            if (Vector3.Distance(npc.Position, target.Position) > ability.Range) continue;
            bool isHealAbility = ability.Effects.Any(e => e is ServerHealEffectData);
            if (isHealAbility && npc.CurrentHealth > npc.MaxHealth * 0.6f) continue;
            candidates.Add(ability);
        }
        return candidates.OrderByDescending(a => a.Priority).FirstOrDefault();
    }

    // Copiado do WanderingAggressiveBehavior
    protected bool IsOnCooldown(NpcInstance npc, string cooldownKey)
    {
        if (string.IsNullOrEmpty(cooldownKey)) return false;
        if (npc.AbilityCooldowns.TryGetValue(cooldownKey, out var cooldownEnd))
        {
            return _server.CurrentTimeUtc < cooldownEnd;
        }
        return false;
    }

    protected void FaceTarget(NpcInstance npc, ICombatEntity target) { /* Cliente cuida disso */ }
}