// Servidor/Managers/CombatManager.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;

/// <summary>
/// Define os tipos de eventos de combate que podem ser enviados aos clientes para exibição.
/// </summary>
public enum CombatEventType { PhysicalDamage, CriticalDamage, Heal, CriticalHeal }

/// <summary>
/// Gerencia toda a lógica de combate, incluindo validação e execução de habilidades.
/// </summary>
public class CombatManager
{
    private readonly UDPServer _server;
    private static readonly Random _random = new Random();

    public CombatManager(UDPServer server)
    {
        _server = server;
    }


    public void HandleLootRequest(Player player, string targetNpcId)
    {
        // Encontra o NPC no dicionário de NPCs ATIVOS (mesmo que IsActive = false)
        if (!_server.DeadNpcCor_pses.TryGetValue(targetNpcId, out var npc)) return;

        // Validações
        if (!npc.IsDead) return; // Só pode pegar loot de corpos
        if (!npc.HasLoot) return; // Não há loot para pegar
        if (Vector3Helper.Distance2D(player.Position, npc.Position) > 5.0f) return;

        // Tenta adicionar todos os itens ao inventário do jogador
        bool allItemsAdded = true;
        // Proteção contra referência nula em npc.Loot
        if (npc.Loot == null || npc.Loot.Count == 0)
        {
            _server.NetworkManager.SendMessageToPlayer(player, "ERROR|Nenhum loot disponível.");
            return;
        }

        foreach (var itemStack in npc.Loot)
        {
            if (!player.PlayerInventory.AddItem(itemStack.ItemID, itemStack.Quantity))
            {
                allItemsAdded = false;
                _server.NetworkManager.SendMessageToPlayer(player, "ERROR|Inventário cheio.");
                break;
            }
            else
            {
                _server.NetworkManager.SendMessageToPlayer(player, $"SHOW_FEEDBACK|Item+{itemStack.ItemID}");
            }
        }

        if (allItemsAdded)
        {
            // Se todos os itens foram pegos, limpa o loot do NPC
            npc.ClearLoot();

            // Notifica o cliente que a janela de loot deve ser fechada (ou o corpo para de brilhar)
            // e que seu inventário foi atualizado.
            _server.NetworkManager.SendMessageToPlayer(player, "LOOT_SUCCESSFUL");
            player.SendFullStateToClient();
        }
    }


    /// <summary>
    /// PONTO DE ENTRADA PRINCIPAL. Valida uma requisição de habilidade e decide se a
    /// executa instantaneamente ou se inicia um processo de casting.
    /// </summary>
    public void ProcessAbilityRequest(ICombatEntity source, string abilityId, string targetId)
    {
        // --- 1. VALIDAÇÕES IMEDIATAS E ESSENCIAIS ---
        if (source.IsDead) return;
        if (!DataManager.Abilities.TryGetValue(abilityId, out var ability)) return;
        if (source is Player p_casting && p_casting.IsCasting) return;
        if (source is NpcInstance npc_casting && npc_casting.IsCasting) return;

        // Validações de Cooldown e Recurso
        if (source.AbilityCooldowns.TryGetValue(abilityId, out DateTime cdEnd) && _server.CurrentTimeUtc < cdEnd)
        {
            if (source is Player player) _server.NetworkManager.SendMessageToPlayer(player, $"ABILITY_FAILED|{abilityId}|AbilityCooldown");
            return;
        }
        if (source.CurrentResource < ability.ResourceCost)
        {
            if (source is Player player) _server.NetworkManager.SendMessageToPlayer(player, $"ABILITY_FAILED|{abilityId}|LowResource");
            return;
        }
        if (!CheckWeaponRequirement(source, ability))
        {
            if (source is Player player) _server.NetworkManager.SendMessageToPlayer(player, $"ABILITY_FAILED|{abilityId}|ActionNotAllowed");
            return;
        }

        // --- 2. VALIDAÇÃO DE ALVO ---
        bool isAreaOfEffectOnGround = targetId.StartsWith("ground:");
        ICombatEntity? target = isAreaOfEffectOnGround ? null : FindEntityById(targetId);
        if (!isAreaOfEffectOnGround) // Só fazemos essas checagens se o alvo não for o chão
        {
            if (target == null)
            {
                if (source is Player player) _server.NetworkManager.SendMessageToPlayer(player, $"ABILITY_FAILED|{abilityId}|InvalidTarget");
                return;
            }

            if (target.IsDead)
            {
                // Se a habilidade for de ajuda (ex: Ressuscitar), ela PODE ter um alvo morto.
                // Adicionamos essa exceção para permitir feitiços de ressurreição.
                if (ability.Intent != AbilityIntent.Helpful)
                {
                    if (source is Player player) _server.NetworkManager.SendMessageToPlayer(player, $"ABILITY_FAILED|{abilityId}|InvalidTarget");
                    return;
                }
            }
        }

        // A habilidade requer um alvo, mas nenhum foi fornecido ou encontrado
        if (ability.RequiresTarget && target == null && !isAreaOfEffectOnGround)
        {
            if (source is Player player) _server.NetworkManager.SendMessageToPlayer(player, $"ABILITY_FAILED|{abilityId}|InvalidTarget");
            return;
        }

        if (target != null && source.Id == target.Id &&
    ability.Intent == AbilityIntent.Harmful &&
    ability.TargetType != TargetType.Self &&
    ability.TargetType != TargetType.AreaOfEffectSelf)
        {
            // Bloqueia silenciosamente no lado do servidor, pois isso indica um bug na IA
            // ou uma tentativa de exploit, e não um erro do jogador.
            Console.WriteLine($"[COMBAT-WARN] Entidade {source.Id} tentou se atacar com a habilidade '{abilityId}'. Ação bloqueada.");
            return;
        }

        // Validação de facção
        if (target != null)
        {
            if (ability.Intent == AbilityIntent.Harmful && AreEntitiesFriendly(source, target))
            {
                if (source is Player player) _server.NetworkManager.SendMessageToPlayer(player, $"ABILITY_FAILED|{abilityId}|InvalidTarget");
                return;
            }
            if (ability.Intent == AbilityIntent.Helpful && !AreEntitiesFriendly(source, target))
            {
                if (source is Player player) _server.NetworkManager.SendMessageToPlayer(player, $"ABILITY_FAILED|{abilityId}|InvalidTarget");
                return;
            }
        }

        // Validação de alcance
        float distanceCheck = isAreaOfEffectOnGround
               ? Vector3Helper.Distance2D(source.Position, ParseVector3FromTargetId(targetId))
               : (target != null ? Vector3Helper.Distance2D(source.Position, target.Position) : 0);

        if (ability.Range > 0 && distanceCheck > ability.Range)
        {
            if (source is Player player) _server.NetworkManager.SendMessageToPlayer(player, $"ABILITY_FAILED|{abilityId}|OutOfRange");
            return;
        }

        // --- HABILIDADE AUTORIZADA PARA INICIAR ---

        if (ability.Intent == AbilityIntent.Harmful && source is Player playerSource)
        {
            playerSource.EnterCombat(_server);
        }

        // --- 3. ROTEAMENTO: CASTING vs. INSTANTÂNEO ---
        if (ability.CastTime > 0)
        {
            string castTimeStr = ability.CastTime.ToString(CultureInfo.InvariantCulture);

            if (source is Player playerCaster)
            {
                playerCaster.StartCasting(ability, targetId, _server.CurrentTimeUtc);
                _server.NetworkManager.SendMessageToPlayer(playerCaster, $"CAST_STARTED|{ability.ID}|{castTimeStr}");
            }
            // NOVO: Lógica para NPCs
            else if (source is NpcInstance npcCaster)
            {
                npcCaster.StartCasting(ability, targetId, _server.CurrentTimeUtc);
            }

            // Mensagem para TODOS os jogadores verem a animação de cast.
            // Isso agora funciona para Players E NPCs.
            string broadcastMessage = $"ENTITY_CAST_START|{source.Id}|{ability.ID}|{castTimeStr}";
            _server.NetworkManager.BroadcastMessageToRelevantPlayers(source.Position, broadcastMessage);
        }
        else // Habilidade Instantânea
        {
            // Aplica custos e cooldowns da habilidade
            source.CurrentResource -= ability.ResourceCost;
            if (ability.Cooldown > 0)
            {
                source.AbilityCooldowns[ability.ID] = _server.CurrentTimeUtc.AddSeconds(ability.Cooldown);
            }

            // <<< ADICIONE ESTA LÓGICA >>>
            // Habilidades instantâneas (que não são auto-ataques) também devem ativar o GCD.
            if (source is NpcInstance npc && abilityId != npc.BaseData.AutoAttackAbilityID)
            {
                npc.GlobalCooldownEndTime = _server.CurrentTimeUtc.AddSeconds(1.5);
            }

            if (source is Player p) _server.NetworkManager.SendVitalsUpdate(p);

            // Aplica os efeitos
            ApplyAbilityEffects(source, ability, targetId);
        }
    }

    // Função NOVA em CombatManager.cs
    /// <summary>
    /// Lida com a requisição de um cliente para cancelar um casting em andamento.
    /// </summary>
    public void HandleCancelCastRequest(Player player)
    {
        // Cancela o casting de habilidade
        if (player.IsCasting)
        {
            player.InterruptCasting(true, _server.NetworkManager);
        }

        // (NOVO) Cancela a coleta, se estiver acontecendo
        if (player.CurrentGatheringTokenSource != null)
        {
            player.InterruptGathering();
        }
    }

    public void ApplyAbilityEffects(ICombatEntity source, AbilityData ability, string targetId)
    {
        var finalTargets = new List<ICombatEntity>();
        Vector3 aoeCenter = source.Position; // A posição padrão para o centro do AoE é o próprio conjurador

        // --- 1. Determina os Alvos Finais e o Centro do Efeito ---
        // Esta parte determina quais criaturas serão afetadas e onde o centro do AoE está.
        switch (ability.TargetType)
        {
            case TargetType.Self:
                finalTargets.Add(source);
                break;
            case TargetType.SingleTarget:
                if (FindEntityById(targetId) is { } singleTarget) finalTargets.Add(singleTarget);
                break;
            case TargetType.AreaOfEffectSelf:
                aoeCenter = source.Position;
                finalTargets.AddRange(FindTargetsInRadius(aoeCenter, ability.AoeRadius, source, ability.Intent));
                break;
            case TargetType.AreaOfEffectTarget:
                if (FindEntityById(targetId) is { } aoeTarget)
                {
                    aoeCenter = aoeTarget.Position; // O centro é a posição do alvo
                    finalTargets.AddRange(FindTargetsInRadius(aoeCenter, ability.AoeRadius, source, ability.Intent));
                }
                break;
            case TargetType.AreaOfEffectGround:
                aoeCenter = ParseVector3FromTargetId(targetId); // O centro é a posição do chão
                finalTargets.AddRange(FindTargetsInRadius(aoeCenter, ability.AoeRadius, source, ability.Intent));
                break;
            case TargetType.Cone:
                finalTargets.AddRange(FindTargetsInCone(source, ability));
                break;
            case TargetType.Projectile:
                if (FindEntityById(targetId) is { } projectileTarget) finalTargets.Add(projectileTarget);
                break;
        }

        // --- 2. Notifica os Clientes para a Execução Visual ---
        // A mensagem de rede é enviada aqui, antes da aplicação da lógica, para que os efeitos visuais sejam imediatos.
        _server.NetworkManager.BroadcastMessageToRelevantPlayers(source.Position, $"EXECUTE_ABILITY|{source.Id}|{ability.ID}|{targetId}");

        // --- 3. Separa e Aplica os Efeitos (LÓGICA CORRIGIDA) ---

        // Parte A: Lida com efeitos que acontecem NO CHÃO (Hazards, Summons).
        // Estes são aplicados uma única vez, na localização 'aoeCenter'.
        var groundEffects = ability.Effects
            .Where(e => e is ServerCreateHazardEffectData || e is ServerSummonNpcEffectData);

        if (groundEffects.Any())
        {
            // Cria um alvo temporário na posição central do AoE para passar para o método.
            var groundTarget = new WorldPositionTarget(aoeCenter);
            foreach (var effectData in groundEffects)
            {
                ApplySingleEffect(source, groundTarget, effectData, ability);
            }
        }

        // Parte B: Lida com efeitos que afetam as CRIATURAS na área (Dano, Cura Direta, Buffs).
        // Estes são aplicados a cada alvo encontrado na lista 'finalTargets'.
        var directTargetEffects = ability.Effects
            .Where(e => !(e is ServerCreateHazardEffectData || e is ServerSummonNpcEffectData));

        foreach (var effectData in directTargetEffects)
        {
            foreach (var target in finalTargets)
            {
                if (target == null || target.IsDead) continue;
                ApplySingleEffect(source, target, effectData, ability);
            }
        }

        // --- 4. Aplica os Efeitos com Intenção Oposta ao Próprio Conjurador ---
        // (Ex: uma habilidade de dano que também cura o caster). Lógica mantida.
        var selfEffects = ability.Effects.Where(e => e.Intent != ability.Intent);
        foreach (var effectData in selfEffects)
        {
            if (source.IsDead) continue;
            ApplySingleEffect(source, source, effectData, ability);
        }
    }

    // =================================================================================
    // >> MÉTODO AUXILIAR ATUALIZADO (COM O FIX DO NAMEPLATE) <<
    // =================================================================================
    public void ApplySingleEffect(ICombatEntity caster, ICombatEntity target, ServerAbilityEffectData effectData, AbilityData sourceAbility)
    {
        if (caster.Stats == null) return;

        if (effectData is ServerDamageEffectData damageEffect)
        {
            float rawDamage = damageEffect.BaseValue;
            rawDamage += caster.Stats.GetStatValue(StatType.AttackPower) * damageEffect.AttackPowerScaling;
            rawDamage += caster.Stats.GetStatValue(StatType.SpellPower) * damageEffect.SpellPowerScaling;

            bool isCritical = _random.NextDouble() * 100 < caster.Stats.GetStatValue(StatType.CriticalStrikeChance);
            if (isCritical) rawDamage *= 2.0f;

            // --- LOG DE DEPURAÇÃO ---
            // Console.ForegroundColor = ConsoleColor.Yellow;
            // Console.WriteLine($"[DAMAGE DEBUG] Caster: {caster.Id} ({caster.GetType().Name}) | Target: {target.Id} ({target.GetType().Name})");
            // Console.ResetColor();
            // --- FIM DO LOG ---

            target.TakeDamage(rawDamage, caster, _server);

            var eventType = isCritical ? CombatEventType.CriticalDamage : CombatEventType.PhysicalDamage;
            string message = $"COMBAT_EVENT|{target.SessionId}|{eventType}|{(int)rawDamage}|{isCritical}";
            _server.NetworkManager.BroadcastMessageToRelevantPlayers(target.Position, message);
        }
        else if (effectData is ServerHealEffectData healEffect)
        {
            float rawHeal = healEffect.BaseValue;
            rawHeal += caster.Stats.GetStatValue(StatType.SpellPower) * healEffect.SpellPowerScaling;

            bool isCritical = _random.NextDouble() * 100 < caster.Stats.GetStatValue(StatType.CriticalStrikeChance);
            if (isCritical) rawHeal *= 1.5f;

            target.ReceiveHealing(rawHeal, _server);

            var eventType = isCritical ? CombatEventType.CriticalHeal : CombatEventType.Heal;

            string message = $"COMBAT_EVENT|{target.SessionId}|{eventType}|{(int)rawHeal}|{isCritical}";
            _server.NetworkManager.BroadcastMessageToRelevantPlayers(target.Position, message);
        }
        else if (effectData is ServerApplyStatusEffectData applyStatusEffect)
        {
            Console.WriteLine($"[COMBAT] Aplicando Status Effect '{applyStatusEffect.StatusEffectID}' de {caster.Id} para {target.Id}");
            target.StatusEffectController.ApplyEffect(applyStatusEffect.StatusEffectID, caster);
        }
        else if (effectData is ServerSummonNpcEffectData summonEffect)
        {
            // O alvo determina ONDE invocar. Se o alvo for o chão, usa a posição do alvo.
            // Se for uma entidade, invoca perto dela.
            _server.WorldManager.SpawnTemporaryNpcs(
                summonEffect.NpcTypeId,
                target.Position, // A posição do alvo é o centro do spawn
                summonEffect.Quantity,
                summonEffect.SpawnRadius,
                summonEffect.DurationSeconds);
        }
        else if (effectData is ServerCreateHazardEffectData hazardEffect)
        {
            // (MUDANÇA) Passa a 'ability' inteira para que o WorldManager saiba o ID.
            _server.WorldManager.CreateHazard(
                caster,
                sourceAbility, // Passa a habilidade original
                target.Position,
                hazardEffect.Radius,
                hazardEffect.DurationSeconds,
                hazardEffect.TickRate,
                hazardEffect.TickEffects);
        }
    }

    #region Métodos Auxiliares para Efeitos

    private List<ICombatEntity> FindTargetsInRadius(Vector3 center, float radius, ICombatEntity source, AbilityIntent intent)
    {
        var radiusSqr = radius * radius;
        var allPossibleTargets = _server.ActiveNpcs.Values.Cast<ICombatEntity>()
                                    .Concat(_server.ConnectedPlayers.Values.Cast<ICombatEntity>())
                                    .ToList();

        return allPossibleTargets.Where(target =>
            target.Id != source.Id &&
            !target.IsDead &&
            // <<< MUDANÇA >>> Usa o helper para comparar a distância quadrada no plano XZ.
            Vector3Helper.Distance2DSquared(target.Position, center) <= radiusSqr &&
            ((intent == AbilityIntent.Harmful && !AreEntitiesFriendly(source, target)) ||
             (intent == AbilityIntent.Helpful && AreEntitiesFriendly(source, target)))
        ).ToList();
    }

    private Vector3 ParseVector3FromTargetId(string targetId)
    {
        try
        {
            string posStr = targetId.Substring("ground:".Length);
            string[] parts = posStr.Split(',');
            return new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture)
            );
        }
        catch { return Vector3.Zero; }
    }

    #endregion

    /// <summary>
    /// Obtém o vetor de direção "para a frente" de uma entidade.
    /// </summary>
    private Vector3 GetEntityForwardVector(ICombatEntity entity)
    {
        if (entity is Player player)
        {
            // Converte a rotação Y (em graus) do jogador para um vetor de direção.
            // float angleInRadians = player.State.RotationY * (float)(Math.PI / 180.0);
            // return new Vector3((float)Math.Sin(angleInRadians), 0, (float)Math.Cos(angleInRadians));
        }

        // TODO: Para NPCs, você precisará implementar uma forma de obter a direção deles.
        // Por enquanto, eles olharão para a frente no eixo Z.
        if (entity is NpcInstance npc)
        {
            // Se seu NpcInstance tiver uma propriedade de Rotação ou Forward, use-a aqui.
            // Ex: return npc.Forward;
        }

        return Vector3.UnitZ;
    }

    // Adicione este SEGUNDO NOVO método auxiliar em CombatManager.cs (perto de FindTargetsInRadius)
    /// <summary>
    /// Encontra todas as entidades de combate válidas dentro de um cone à frente do conjurador.
    /// </summary>
    private List<ICombatEntity> FindTargetsInCone(ICombatEntity source, AbilityData ability)
    {
        var targetsInCone = new List<ICombatEntity>();
        var allPossibleTargets = _server.ActiveNpcs.Values.Cast<ICombatEntity>()
                                    .Concat(_server.ConnectedPlayers.Values.Cast<ICombatEntity>())
                                    .ToList();

        Vector3 sourcePosition = source.Position;
        Vector3 sourceForward = GetEntityForwardVector(source);
        float coneRangeSqr = ability.Range * ability.Range;

        // Otimização: Em vez de calcular ângulos (que é lento), vamos comparar o cosseno do ângulo.
        // Calculamos o cosseno do meio-ângulo do cone uma única vez.
        float coneHalfAngleRadians = (ability.ConeAngle / 2.0f) * (float)(Math.PI / 180.0);
        float minDotProduct = (float)Math.Cos(coneHalfAngleRadians);

        foreach (var target in allPossibleTargets)
        {
            if (target.Id == source.Id || target.IsDead) continue;

            // <<< MUDANÇA >>> Usa o helper para a verificação de distância no plano XZ.
            if (Vector3Helper.Distance2DSquared(sourcePosition, target.Position) > coneRangeSqr)
            {
                continue;
            }

            // --- 2. Verificação de Ângulo ---
            Vector3 directionToTarget = Vector3.Normalize(target.Position - sourcePosition);
            // O produto escalar (Dot Product) de dois vetores normalizados é o cosseno do ângulo entre eles.
            float dotProduct = Vector3.Dot(sourceForward, directionToTarget);

            if (dotProduct < minDotProduct)
            {
                continue; // Alvo está fora do ângulo do cone.
            }

            // --- 3. Verificação de Facção (igual à do AoE) ---
            bool isFriendly = AreEntitiesFriendly(source, target);
            if ((ability.Intent == AbilityIntent.Harmful && !isFriendly) ||
                (ability.Intent == AbilityIntent.Helpful && isFriendly))
            {
                targetsInCone.Add(target);
            }
        }

        return targetsInCone;
    }

    #region Métodos Auxiliares

    private bool AreEntitiesFriendly(ICombatEntity entityA, ICombatEntity entityB)
    {
        if (entityA is Player && entityB is Player) return true;
        if (entityA is Player && entityB is NpcInstance npc) return npc.BaseData.Faction == NpcFaction.Friendly;
        if (entityA is NpcInstance npc2 && entityB is Player) return npc2.BaseData.Faction == NpcFaction.Friendly;
        if (entityA is NpcInstance n1 && entityB is NpcInstance n2) return n1.BaseData.Faction == n2.BaseData.Faction;
        return false;
    }

    private bool CheckWeaponRequirement(ICombatEntity source, AbilityData ability)
    {
        if (source is NpcInstance) return true;

        if (source is Player player)
        {
            WeaponRequirement requirement = ability.WeaponRequirement;
            // Assumindo que Player tem um método para obter o tipo de arma equipada.
            WeaponType? equippedType = player.PlayerEquipment.GetMainHandWeaponType();

            switch (requirement)
            {
                case WeaponRequirement.None: return true;
                case WeaponRequirement.Unarmed: return !equippedType.HasValue;
                case WeaponRequirement.WeaponRequired: return equippedType.HasValue;
                case WeaponRequirement.MeleeWeapon: return equippedType.HasValue && WeaponHelper.IsMelee(equippedType.Value);
                case WeaponRequirement.RangedWeapon: return equippedType.HasValue && WeaponHelper.IsRanged(equippedType.Value);
                default: return false;
            }
        }
        return false;
    }

    // Em CombatManager.cs

    // SUBSTITUA SEU MÉTODO FindEntityById ATUAL POR ESTE NOVO:
    private ICombatEntity? FindEntityById(string id)
    {
        if (string.IsNullOrEmpty(id) || id.ToLower() == "null") return null;

        // --- PASSO 1: Tenta interpretar como um SessionId (inteiro) ---
        if (int.TryParse(id, out int sessionId))
        {
            // É um jogador?
            if (_server.PlayersBySessionId.TryGetValue(sessionId, out Player? player))
            {
                return player;
            }

            // É um NPC? (Vamos precisar de um novo dicionário no servidor)
            // Por enquanto, vamos iterar. Ver otimização abaixo.
            if (_server.NpcsBySessionId.TryGetValue(sessionId, out NpcInstance? npcBySessionId))
            {
                return npcBySessionId;
            }
        }

        // --- PASSO 2: Se não for um inteiro, trata como uma string (GUID/InstanceId) ---

        // É um NPC (pelo InstanceId/GUID)?
        if (_server.ActiveNpcs.TryGetValue(id, out var npcByInstanceId))
        {
            return npcByInstanceId;
        }

        // É um jogador (pelo CharacterId/GUID)?
        var playerByCharacterId = _server.ConnectedPlayers.Values.FirstOrDefault(p => p.CharacterId == id);
        if (playerByCharacterId != null)
        {
            return playerByCharacterId;
        }

        return null;
    }

    // Este método parece ser chamado de fora, então o mantemos público.
    public void ReportNpcDeath(NpcInstance npc, ICombatEntity? lastAttacker)
    {
        _server.WorldManager.ProcessNpcDeath(npc, lastAttacker);
    }
    #endregion

    /// <summary>
    /// Verifica se há uma linha de visão desobstruída entre duas entidades usando lógica comportamental.
    /// </summary>
    public bool HasLineOfSight(ICombatEntity source, ICombatEntity target)
    {
        // Por padrão, consideramos que a visão está limpa.
        if (source is not NpcInstance npc || target is not Player player)
        {
            return true;
        }

        // A lógica de "LoS Falso":
        // Se o estado do NPC é de perseguição, mas ele não conseguiu se mover...
        if (npc.CurrentState == NpcAiState.Chasing && Vector3Helper.Distance2D(npc.Position, npc.LastPosition) < 0.1f)
        {
            if ((_server.CurrentTimeUtc - npc.TimeAtLastPosition).TotalMilliseconds > 500)
            {
                return false;
            }
        }

        // Se a condição acima não for atendida, assumimos que a visão está limpa.
        return true;
    }
}


/// <summary>
/// Representa um alvo que é apenas uma posição no mundo, não uma criatura.
/// Usado para habilidades de AoE no chão, para que possam ser passadas para métodos
/// que esperam um ICombatEntity.
/// </summary>
public class WorldPositionTarget : ICombatEntity
{
    public string Id => "ground_target";
    public Vector3 Position { get; }
    public bool IsDead => false;

    // --- (CORREÇÃO) IMPLEMENTAÇÕES VAZIAS PARA SATISFAZER A INTERFACE ---
    public CharacterStats? Stats => null;
    public int Level => 0;
    public float CurrentHealth { get; set; } = 1;
    public float MaxHealth => 1;
    public float CurrentResource { get; set; } = 0;
    public float MaxResource => 0;
    public float MovementSpeed => 0;
    public Dictionary<string, DateTime> AbilityCooldowns { get; } = new Dictionary<string, DateTime>();

    public int SessionId => 0;
    public string InstanceId => Id;

    public StatusEffectController StatusEffectController => throw new NotImplementedException();

    // Métodos que não fazem nada para um alvo no chão
    public void TakeDamage(float amount, ICombatEntity source, UDPServer server) { }
    public void ReceiveHealing(float amount, UDPServer server) { }
    public void ProcessDeath(ICombatEntity killer, UDPServer server) { }

    public WorldPositionTarget(Vector3 position)
    {
        Position = position;
    }
}
