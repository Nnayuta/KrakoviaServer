using System;
using System.Linq;
using System.Numerics;

// Herda de BaseBehavior para ser independente
public class StationaryGuardBehavior : BaseBehavior
{
    public StationaryGuardBehavior(UDPServer server) : base(server) { }

    public override void Update(NpcInstance npc, float deltaTime)
    {
        // Garante que o guarda fique em seu posto quando não está em combate.
        // Apenas checamos isso se ele não estiver ativamente em combate.
        if (npc.CurrentState != NpcAiState.Attacking)
        {
            if (Vector3.DistanceSquared(npc.Position, npc.SpawnPosition) > 0.01f)
            {
                npc.Position = npc.SpawnPosition;
                npc.Destination = npc.SpawnPosition;
            }
        }

        if (npc.CurrentState == NpcAiState.ReturningToSpawn)
        {
            HandleReturningToSpawnState(npc);
            return;
        }

        ICombatEntity? target = GetCurrentTarget(npc);

        // --- LÓGICA DE DECISÃO REFEITA ---
        if (target == null) // Se não estamos em combate...
        {
            // Se o guarda for amigável, ele procura por monstros inimigos.
            if (npc.BaseData.Faction == NpcFaction.Friendly || npc.BaseData.Faction == NpcFaction.Neutral)
            {
                target = FindClosestEnemyNpcInRange(npc);
            }
            // Se o guarda for da facção inimiga, ele procura por jogadores.
            else if (npc.BaseData.Faction == NpcFaction.Enemy)
            {
                target = FindClosestPlayerInAggroRange(npc);
            }

            // Se encontrou um alvo válido, entra em combate.
            if (target != null)
            {
                EngageTarget(npc, target);
            }
            else // Sem alvo, fica em paz (Idle).
            {
                ChangeNpcState(npc, NpcAiState.Idle);
                return;
            }
        }

        // --- LÓGICA DE MANUTENÇÃO DE COMBATE ---
        target = GetCurrentTarget(npc); // Re-obtém para garantir que não seja nulo.
        if (target == null || target.IsDead || Vector3Helper.Distance2D(npc.Position, target.Position) > npc.BaseData.LeashRange)
        {
            ResetAggro(npc);
            return;
        }

        // Se o alvo é válido, executa a lógica de ataque.
        HandleAttackingState(npc, target);
    }

    private void HandleAttackingState(NpcInstance npc, ICombatEntity target)
    {
        ChangeNpcState(npc, NpcAiState.Attacking);

        // Se o alvo sair do alcance de ataque, o guarda DESISTE em vez de perseguir.
        // Lógica de ataque (agora está correta)
        if (_server.CurrentTimeUtc < npc.GlobalCooldownEndTime) return;

        AbilityData? specialAbility = ChooseBestSpecialAbility(npc, target);
        if (specialAbility != null)
        {
            // A própria validação dentro de ChooseBestSpecialAbility já verifica o alcance da habilidade.
            // Se a habilidade for escolhida, significa que o alvo está no alcance dela.
            _server.CombatManager.ProcessAbilityRequest(npc, specialAbility.ID, target.Id);
            if (specialAbility.CastTime <= 0)
            {
                npc.GlobalCooldownEndTime = _server.CurrentTimeUtc.AddSeconds(1.5);
            }
        }
        else if (npc.BaseData.AutoAttackAbilityID != null && _server.CurrentTimeUtc >= npc.NextAutoAttackTime)
        {
            if (DataManager.Abilities.TryGetValue(npc.BaseData.AutoAttackAbilityID, out var autoAttack) &&
                Vector3Helper.Distance2D(npc.Position, target.Position) <= autoAttack.Range) // A checagem de alcance fica aqui!
            {
                _server.CombatManager.ProcessAbilityRequest(npc, autoAttack.ID, target.Id);
                _server.CombatManager.ProcessAbilityRequest(npc, autoAttack.ID, target.Id);

                npc.GlobalCooldownEndTime = _server.CurrentTimeUtc.AddSeconds(1.5);
                npc.NextAutoAttackTime = _server.CurrentTimeUtc.AddSeconds(npc.BaseData.SwingTimer);
            }
        }
    }

    public override void OnDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        if (npc.Id == attacker.Id || npc.IsDead) return;

        // Revida contra QUALQUER um que o ataque, independente da facção.
        // Se já está atacando, adiciona threat. Senão, engaja o novo atacante.
        if (GetCurrentTarget(npc) == null)
        {
            EngageTarget(npc, attacker);
        }

        // Adiciona uma grande quantidade de threat para focar em quem o atacou.
        npc.ThreatTable[attacker.Id] = npc.ThreatTable.GetValueOrDefault(attacker.Id, 0) + 100.0f;
    }

    // --- MÉTODOS AUXILIARES ---
    protected ICombatEntity? GetCurrentTarget(NpcInstance npc) => _server.ConnectedPlayers.Values.FirstOrDefault(p => p.Id == npc.TargetPlayerId && !p.IsDead);
    protected Player? FindClosestPlayerInAggroRange(NpcInstance npc) => FindClosestPlayer(npc, npc.BaseData.AggroRange);

    protected void EngageTarget(NpcInstance npc, ICombatEntity target)
    {
        npc.TargetPlayerId = target.Id;
        npc.ThreatTable[target.Id] = npc.ThreatTable.GetValueOrDefault(target.Id, 0) + 1.0f;
        npc.AggroPosition = npc.Position;

        if (npc.UpdateTier == AiUpdateTier.Slow)
        {
            npc.UpdateTier = AiUpdateTier.Fast;
            Console.WriteLine($"[AI-TIER] NPC {npc.Id} ({npc.BaseData.TypeId}) promovido para o loop RÁPIDO.");
        }

        ChangeNpcState(npc, NpcAiState.Attacking);
    }

    protected void ResetAggro(NpcInstance npc)
    {
        npc.TargetPlayerId = null;
        npc.ThreatTable.Clear();
        npc.AggroPosition = npc.SpawnPosition;

        if (npc.UpdateTier == AiUpdateTier.Fast)
        {
            npc.UpdateTier = AiUpdateTier.Slow;
            Console.WriteLine($"[AI-TIER] NPC {npc.Id} ({npc.BaseData.TypeId}) rebaixado para o loop LENTO.");
        }

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

    protected NpcInstance? FindClosestEnemyNpcInRange(NpcInstance npc)
    {
        var nearbyEntities = _server.GridManager.GetEntitiesInRadius(npc.Position, npc.BaseData.AggroRange);

        return nearbyEntities
            .OfType<NpcInstance>() // Pega apenas as entidades que são NPCs
            .Where(otherNpc => !otherNpc.IsDead && otherNpc.BaseData.Faction == NpcFaction.Enemy)
            .OrderBy(otherNpc => Vector3.DistanceSquared(npc.Position, otherNpc.Position))
            .FirstOrDefault();
    }

}