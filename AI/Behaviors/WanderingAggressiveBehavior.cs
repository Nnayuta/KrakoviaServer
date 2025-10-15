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

        if (npc.CurrentState == NpcAiState.Casting)
        {
            HandleCastingState(npc);
            return; // Sai do Update para não tomar outras decisões.
        }

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

    protected virtual void HandleCastingState(NpcInstance npc)
    {
        // Verifica se o tempo de casting já acabou.
        if (_server.CurrentTimeUtc >= npc.CastingEndTime)
        {
            // Pega os detalhes do cast que foram salvos.
            AbilityData? ability = npc.CastingAbility;
            string? targetId = npc.CastingTargetId;

            // Limpa o estado de casting do NPC.
            npc.FinishCasting();

            // Se os detalhes são válidos, executa os efeitos da habilidade.
            if (ability != null && targetId != null)
            {
                _server.CombatManager.ApplyAbilityEffects(npc, ability, targetId);

                // Aplica o Cooldown da habilidade e o Global Cooldown do NPC
                if (ability.Cooldown > 0)
                {
                    npc.AbilityCooldowns[ability.ID] = _server.CurrentTimeUtc.AddSeconds(ability.Cooldown);
                }
                npc.GlobalCooldownEndTime = _server.CurrentTimeUtc.AddSeconds(1.5);
            }

            // Volta para o estado de ataque para decidir o próximo passo.
            ChangeNpcState(npc, NpcAiState.Attacking);
        }
        // Se ainda não acabou, não faz nada e espera o próximo Update.
    }

    public override void OnDamaged(NpcInstance npc, ICombatEntity attacker)
    {
        if (npc.Id == attacker.Id) return;
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
        // Se o tempo de caminhada acabou OU se já chegamos perto o suficiente do destino...
        if (_server.CurrentTimeUtc >= npc.NextActionTime || Vector3.Distance(npc.Position, npc.Destination) < 1.5f)
        {
            // --- A CORREÇÃO ---
            // Em vez de definir um novo destino, apenas dizemos ao NPC que seu destino agora é onde ele está.
            // Isso o faz parar sem enviar um novo comando de movimento.
            npc.Destination = npc.Position;

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
        // 1. OBTER O ALVO ATUAL
        // A IA precisa saber quem está atacando. Usamos o método auxiliar para isso.
        ICombatEntity? target = GetCurrentTarget(npc);

        // Se por algum motivo perdermos o alvo (ele morreu, desconectou), a lógica
        // principal no método Update irá detectar isso e nos tirar do estado de ataque.
        // Mas por segurança, verificamos aqui também.
        if (target == null) return;


        if (!_server.CombatManager.HasLineOfSight(npc, target))
        {
            ChangeNpcState(npc, NpcAiState.Chasing);
            return;
        }

        // 2. ENCARAR O ALVO (Lógica conceitual)
        FaceTarget(npc, target);


        // 3. CÁLCULO DE DISTÂNCIA
        float distanceToTarget = Vector3.Distance(npc.Position, target.Position);


        // 4. LÓGICA DE REPOSICIONAMENTO (A "ZONA MORTA")
        // Se o NPC tem uma distância mínima de ataque definida e o alvo está perto demais...
        // if (npc.BaseData.MinAttackRange > 0 && distanceToTarget < npc.BaseData.MinAttackRange)
        var MinAttackRange = 4f;
        if (MinAttackRange > 0 && distanceToTarget < MinAttackRange)
        {
            // ... a prioridade máxima é se afastar.
            Vector3 directionAwayFromTarget = Vector3.Normalize(npc.Position - target.Position);

            // Calcula um ponto para recuar. Recuar 5 metros deve ser suficiente para sair da zona de perigo.
            Vector3 repositionPoint = npc.Position + directionAwayFromTarget * 5.0f;

            SetNpcDestination(npc, repositionPoint);

            // Importante: retornamos para que ele não tente atacar enquanto se reposiciona.
            return;
        }


        const float MELEE_ATTACK_BUFFER = 1.5f; // 1.5 metros de "zona de conforto"
        if (distanceToTarget > npc.BaseData.MaxAbilityRange + MELEE_ATTACK_BUFFER)
        {
            ChangeNpcState(npc, NpcAiState.Chasing);
            return;
        }

        // Paramos de nos mover para poder atacar ou conjurar.
        SetNpcDestination(npc, npc.Position);

        // Primeiro, verificamos se o GCD está ativo. Nenhuma ação pode ser tomada se estiver.
        if (_server.CurrentTimeUtc < npc.GlobalCooldownEndTime) return;

        AbilityData? specialAbility = ChooseBestSpecialAbility(npc, target);
        if (specialAbility != null)
        {
            _server.CombatManager.ProcessAbilityRequest(npc, specialAbility.ID, target.Id);
        }
        // Se não puder usar uma habilidade especial, tenta o auto-ataque.
        else if (npc.BaseData.AutoAttackAbilityID != null && _server.CurrentTimeUtc >= npc.NextAutoAttackTime)
        {
            if (DataManager.Abilities.TryGetValue(npc.BaseData.AutoAttackAbilityID, out var autoAttack) && distanceToTarget <= autoAttack.Range)
            {
                _server.CombatManager.ProcessAbilityRequest(npc, autoAttack.ID, target.Id);

                // <<< A CORREÇÃO CRÍTICA ESTÁ AQUI >>>
                // O auto-ataque agora também ativa o Global Cooldown.
                // O valor do GCD pode ser o mesmo para todos ou vir de uma constante. 1.5s é um padrão.
                npc.GlobalCooldownEndTime = _server.CurrentTimeUtc.AddSeconds(1.5);

                // E também reinicia seu próprio timer (swing timer).
                npc.NextAutoAttackTime = _server.CurrentTimeUtc.AddSeconds(npc.BaseData.SwingTimer);
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
        // O alvo precisa ser um jogador para se tornar o 'dono' do feedback.
        if (target is Player playerTarget)
        {
            npc.TargetPlayerId = playerTarget.Id; // 'playerTarget.Id' DEVE ser o SessionId (string do int)

            // <<< LOG DE DEPURAÇÃO CRÍTICO >>>
            Console.WriteLine($"[AI-ENGAGE] NPC {npc.Id} engajou o jogador {playerTarget.Id}. Este jogador é agora o 'dono' do feedback.");
        }
        else
        {
            // Se o alvo for outro NPC, não definimos um 'dono' de feedback.
            npc.TargetPlayerId = null;
        }

        npc.ThreatTable[target.Id] = npc.ThreatTable.GetValueOrDefault(target.Id, 0) + 1.0f;
        npc.AggroPosition = npc.Position;
        npc.LastKnownTargetDistance = float.MaxValue;
        npc.TimeAtLastKnownTargetDistance = _server.CurrentTimeUtc;
        ChangeNpcState(npc, NpcAiState.Chasing);
    }

    public virtual void ResetAggro(NpcInstance npc)
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

            if (!_server.CombatManager.HasLineOfSight(npc, target)) continue;

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