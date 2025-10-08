// AI/Behaviors/WanderingAggressiveBehavior.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;

public class WanderingAggressiveBehavior : BaseBehavior
{
    public WanderingAggressiveBehavior(UDPServer server) : base(server) { }

    public override void Update(NpcInstance npc, float deltaTime)
    {
        if (npc.CurrentState == NpcAiState.Chasing || npc.CurrentState == NpcAiState.Attacking)
        {
            ICombatEntity? target = GetCurrentTarget(npc);
            if (target == null || target.IsDead || Vector3.Distance(npc.Position, npc.AggroPosition) > npc.BaseData.LeashRange)
            {
                ResetAggro(npc);
                return;
            }
        }
        else
        {
            ICombatEntity? target = FindClosestPlayerInAggroRange(npc);
            if (target != null)
            {
                EngageTarget(npc, target);
                return;
            }
        }

        switch (npc.CurrentState)
        {
            case NpcAiState.Idle: HandleIdleState(npc); break;
            case NpcAiState.Wandering: HandleWanderingState(npc); break;
            case NpcAiState.Chasing: HandleChasingState(npc); break;
            case NpcAiState.Attacking: HandleAttackingState(npc); break;
            case NpcAiState.ReturningToSpawn: HandleReturningToSpawnState(npc); break;
        }
    }

    public override void OnDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        if (npc.CurrentState != NpcAiState.Chasing && npc.CurrentState != NpcAiState.Attacking)
        {
            EngageTarget(npc, attacker);
        }
        // (CORREÇÃO THREAT) Adiciona 1.0f para corresponder ao tipo float
        npc.ThreatTable[attacker.Id] = npc.ThreatTable.GetValueOrDefault(attacker.Id, 0) + 1.0f;
    }

    // (CORREÇÃO) Marcado como OVERRIDE para corresponder à nova classe base
    protected override void HandleIdleState(NpcInstance npc)
    {
        if (_server.CurrentTimeUtc < npc.NextActionTime) return;
        if (_threadRandom.Value.Next(0, 10) > 3)
        {
            SetNpcDestination(npc, FindWanderPoint(npc));
            ChangeNpcState(npc, NpcAiState.Wandering);
            npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(3, 7));
        }
        else
        {
            npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(2, 5));
        }
    }

    // (CORREÇÃO) Marcado como OVERRIDE
    protected override void HandleWanderingState(NpcInstance npc)
    {
        if (_server.CurrentTimeUtc >= npc.NextActionTime || Vector3.Distance(npc.Position, npc.Destination) < 1.5f)
        {
            SetNpcDestination(npc, npc.Position);
            ChangeNpcState(npc, NpcAiState.Idle);
            npc.NextActionTime = _server.CurrentTimeUtc.AddSeconds(_threadRandom.Value.Next(4, 10));
        }
    }

    // (CORREÇÃO) Marcado como OVERRIDE
    protected override void HandleChasingState(NpcInstance npc)
    {
        ICombatEntity? target = GetCurrentTarget(npc);
        if (target == null) return;
        if (Vector3.Distance(npc.Position, target.Position) <= npc.BaseData.MaxAbilityRange)
        {
            ChangeNpcState(npc, NpcAiState.Attacking);
            SetNpcDestination(npc, npc.Position);
            return;
        }
        SetNpcDestination(npc, target.Position);
    }

    // (CORREÇÃO) Marcado como OVERRIDE
    protected override void HandleAttackingState(NpcInstance npc)
    {
        ICombatEntity? target = GetCurrentTarget(npc);
        if (target == null) return;

        FaceTarget(npc, target);
        const float ATTACK_RANGE_BUFFER = 2.0f;
        if (Vector3.Distance(npc.Position, target.Position) > npc.BaseData.MaxAbilityRange + ATTACK_RANGE_BUFFER)
        {
            ChangeNpcState(npc, NpcAiState.Chasing);
            return;
        }

        if (npc.BaseData.AutoAttackAbilityID != null && _server.CurrentTimeUtc >= npc.NextAutoAttackTime)
        {
            if (DataManager.Abilities.TryGetValue(npc.BaseData.AutoAttackAbilityID, out var autoAttack) && Vector3.Distance(npc.Position, target.Position) <= autoAttack.Range)
            {
                _server.CombatManager.ProcessAbilityRequest(npc, autoAttack.ID, target.Id);
                npc.NextAutoAttackTime = _server.CurrentTimeUtc.AddSeconds(npc.BaseData.SwingTimer);
            }
        }

        if (_server.CurrentTimeUtc >= npc.GlobalCooldownEndTime)
        {
            AbilityData? specialAbility = ChooseBestSpecialAbility(npc, target);
            if (specialAbility != null)
            {
                SetNpcDestination(npc, npc.Position);
                _server.CombatManager.ProcessAbilityRequest(npc, specialAbility.ID, target.Id);
                npc.GlobalCooldownEndTime = _server.CurrentTimeUtc.AddSeconds(1.5);
            }
        }
    }

    // (CORREÇÃO) Marcado como OVERRIDE
    protected override void HandleReturningToSpawnState(NpcInstance npc)
    {
        if (Vector3.Distance(npc.Position, npc.SpawnPosition) < 1.5f)
        {
            npc.CurrentHealth = npc.MaxHealth;
            npc.CurrentResource = npc.MaxResource;
            ChangeNpcState(npc, NpcAiState.Idle);
        }
        else
        {
            SetNpcDestination(npc, npc.SpawnPosition);
        }
    }

    protected void EngageTarget(NpcInstance npc, ICombatEntity target)
    {
        npc.TargetPlayerId = target.Id;
        // (CORREÇÃO THREAT) Adiciona 1.0f para corresponder ao tipo float
        npc.ThreatTable[target.Id] = npc.ThreatTable.GetValueOrDefault(target.Id, 0) + 1.0f;
        npc.AggroPosition = npc.Position;
        ChangeNpcState(npc, NpcAiState.Chasing);
    }

    protected void ResetAggro(NpcInstance npc)
    {
        npc.TargetPlayerId = null;
        npc.ThreatTable.Clear();
        npc.AggroPosition = npc.SpawnPosition;
        ChangeNpcState(npc, NpcAiState.ReturningToSpawn);
    }

    // (CORREÇÃO) Lógica completa adicionada
    protected AbilityData? ChooseBestSpecialAbility(NpcInstance npc, ICombatEntity target)
    {
        // Lista de candidatos a habilidades para usar
        var candidates = new List<AbilityData>();

        foreach (var abilityId in npc.BaseData.AbilityIDs)
        {
            // Ignora o auto-ataque
            if (abilityId == npc.BaseData.AutoAttackAbilityID) continue;

            // Tenta obter os dados da habilidade
            if (!DataManager.Abilities.TryGetValue(abilityId, out var ability)) continue;

            // --- VALIDAÇÕES BÁSICAS ---
            if (IsOnCooldown(npc, ability.ID)) continue;
            if (Vector3.Distance(npc.Position, target.Position) > ability.Range) continue;

            // --- VALIDAÇÕES CONTEXTUAIS ---
            // Verifica se é uma habilidade de cura
            bool isHealAbility = ability.Effects.Any(e => e is ServerHealEffectData);

            // Se for cura, só usa se a vida estiver abaixo de 60%
            if (isHealAbility && npc.CurrentHealth > npc.MaxHealth * 0.6f) continue;

            // Se passou por tudo, é um candidato válido
            candidates.Add(ability);
        }

        // Retorna a habilidade de maior prioridade entre as válidas, ou null se não houver nenhuma.
        return candidates.OrderByDescending(a => a.Priority).FirstOrDefault();
    }

    // Versão explícita do IsOnCooldown
    protected bool IsOnCooldown(NpcInstance npc, string cooldownKey)
    {
        if (string.IsNullOrEmpty(cooldownKey))
        {
            return false; // Sem chave, sem cooldown.
        }

        if (npc.AbilityCooldowns.TryGetValue(cooldownKey, out var cooldownEnd))
        {
            // Retorna true se o tempo atual ainda é MENOR que o fim do cooldown.
            return _server.CurrentTimeUtc < cooldownEnd;
        }

        // Se não está no dicionário, não está em cooldown.
        return false;
    }

    protected Player? FindClosestPlayerInAggroRange(NpcInstance npc)
    {
        return FindClosestPlayer(npc, npc.BaseData.AggroRange);
    }

    protected ICombatEntity? GetCurrentTarget(NpcInstance npc) => GetPlayerById(npc.TargetPlayerId);
    protected Player? GetPlayerById(string? id) => string.IsNullOrEmpty(id) ? null : _server.ConnectedPlayers.Values.FirstOrDefault(p => p.Id == id);
    protected void FaceTarget(NpcInstance npc, ICombatEntity target) { /* Cliente cuida disso */ }
}