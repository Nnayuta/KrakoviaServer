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
        if (Vector3.Distance(player.Position, npc.Position) > 5.0f) return;

        // TODO: Adicionar lógica para verificar quem tem direito ao loot (quem matou, grupo, etc.)

        // Tenta adicionar todos os itens ao inventário do jogador
        bool allItemsAdded = true;
        foreach (var itemStack in npc.Loot)
        {
            if (!player.PlayerInventory.AddItem(itemStack.ItemID, itemStack.Quantity))
            {
                allItemsAdded = false;
                _server.NetworkManager.SendMessageToClient("ERROR|Inventário cheio.", player.EndPoint);
                break;
            }
        }

        if (allItemsAdded)
        {
            // Se todos os itens foram pegos, limpa o loot do NPC
            npc.ClearLoot();

            // Notifica o cliente que a janela de loot deve ser fechada (ou o corpo para de brilhar)
            // e que seu inventário foi atualizado.
            _server.NetworkManager.SendMessageToClient("LOOT_SUCCESSFUL", player.EndPoint);
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
        if (source is Player p_casting && p_casting.IsCasting) return; // Jogador já está conjurando algo

        // Validações de Cooldown e Recurso
        if (source.AbilityCooldowns.TryGetValue(abilityId, out DateTime cdEnd) && _server.CurrentTimeUtc < cdEnd)
        {
            if (source is Player p) _server.NetworkManager.SendMessageToClient($"ABILITY_FAILED|{abilityId}|Em Cooldown", p.EndPoint);
            return;
        }
        if (source.CurrentResource < ability.ResourceCost)
        {
            if (source is Player p) _server.NetworkManager.SendMessageToClient($"ABILITY_FAILED|{abilityId}|Recurso Insuficiente", p.EndPoint);
            return;
        }
        if (!CheckWeaponRequirement(source, ability))
        {
            if (source is Player p) _server.NetworkManager.SendMessageToClient($"ABILITY_FAILED|{abilityId}|Requer Arma Específica", p.EndPoint);
            return;
        }

        // --- 2. VALIDAÇÃO DE ALVO ---
        bool isAreaOfEffectOnGround = targetId.StartsWith("ground:");
        ICombatEntity? target = isAreaOfEffectOnGround ? null : FindEntityById(targetId);

        // A habilidade requer um alvo, mas nenhum foi fornecido ou encontrado
        if (ability.RequiresTarget && target == null && !isAreaOfEffectOnGround)
        {
            if (source is Player p) _server.NetworkManager.SendMessageToClient($"ABILITY_FAILED|{abilityId}|Alvo Inválido", p.EndPoint);
            return;
        }

        // Validação de facção
        if (target != null)
        {
            if (ability.Intent == AbilityIntent.Harmful && AreEntitiesFriendly(source, target))
            {
                if (source is Player p) _server.NetworkManager.SendMessageToClient($"ABILITY_FAILED|{abilityId}|Alvo Inválido", p.EndPoint);
                return;
            }
            if (ability.Intent == AbilityIntent.Helpful && !AreEntitiesFriendly(source, target))
            {
                if (source is Player p) _server.NetworkManager.SendMessageToClient($"ABILITY_FAILED|{abilityId}|Alvo Inválido", p.EndPoint);
                return;
            }
        }

        // Validação de alcance
        float distanceCheck = isAreaOfEffectOnGround
            ? Vector3.Distance(source.Position, ParseVector3FromTargetId(targetId))
            : (target != null ? Vector3.Distance(source.Position, target.Position) : 0);

        if (ability.Range > 0 && distanceCheck > ability.Range)
        {
            if (source is Player p) _server.NetworkManager.SendMessageToClient($"ABILITY_FAILED|{abilityId}|Fora de Alcance", p.EndPoint);
            return;
        }

        // --- HABILIDADE AUTORIZADA PARA INICIAR ---

        if (ability.Intent == AbilityIntent.Harmful && source is Player playerSource)
        {
            playerSource.EnterCombat();
        }

        // --- 3. ROTEAMENTO: CASTING vs. INSTANTÂNEO ---
        if (ability.CastTime > 0 && source is Player playerCaster) // Apenas jogadores têm casting por enquanto
        {
            playerCaster.StartCasting(ability, targetId, _server.CurrentTimeUtc);

            // NOVO: Notifica o cliente que o casting foi autorizado e pode começar
            string castTimeStr = ability.CastTime.ToString(CultureInfo.InvariantCulture);
            _server.NetworkManager.SendMessageToClient($"CAST_STARTED|{ability.ID}|{castTimeStr}", playerCaster.EndPoint);

            // --- NOVO BROADCAST ---
            // Mensagem para os OUTROS jogadores, para eles verem a animação.
            string broadcastMessage = $"ENTITY_CAST_START|{playerCaster.Id}|{ability.ID}|{castTimeStr}";
            _server.NetworkManager.BroadcastMessageToOthers(playerCaster, broadcastMessage);
        }
        else // Habilidade Instantânea (ou usada por NPC)
        {
            // Aplica custos e cooldowns IMEDIATAMENTE
            source.CurrentResource -= ability.ResourceCost;
            if (ability.Cooldown > 0)
            {
                source.AbilityCooldowns[ability.ID] = _server.CurrentTimeUtc.AddSeconds(ability.Cooldown);
            }

            // Notifica o cliente sobre a mudança de recurso (se for um jogador)
            if (source is Player p) _server.NetworkManager.SendVitalsUpdate(p);

            // Aplica os efeitos e notifica os clientes para a execução visual
            ApplyAbilityEffects(source, ability, targetId);
        }
    }

    // Função NOVA em CombatManager.cs
    /// <summary>
    /// Lida com a requisição de um cliente para cancelar um casting em andamento.
    /// </summary>
    public void HandleCancelCastRequest(Player player)
    {
        if (player.IsCasting)
        {
            player.InterruptCasting(true, _server.NetworkManager); // Passa o NetworkManager para enviar a confirmação
        }
    }


    /// <summary>
    /// Método PÚBLICO que determina os alvos e aplica os efeitos de uma habilidade.
    /// É o "finalizador" de qualquer ação de combate.
    /// </summary>
    // Função ALTERADA em CombatManager.cs (substitua a função inteira)
    /// <summary>
    /// Método PÚBLICO que determina os alvos e aplica os efeitos de uma habilidade.
    /// É o "finalizador" de qualquer ação de combate.
    /// </summary>
    public void ApplyAbilityEffects(ICombatEntity source, AbilityData ability, string targetId)
    {
        var finalTargets = new List<ICombatEntity>();

        // 1. Determina os alvos finais da habilidade
        switch (ability.TargetType)
        {
            case TargetType.Self:
                finalTargets.Add(source);
                break;

            case TargetType.SingleTarget:
                if (FindEntityById(targetId) is { } singleTarget)
                {
                    finalTargets.Add(singleTarget);
                }
                break;

            case TargetType.AreaOfEffect:
                Vector3 aoePosition = ParseVector3FromTargetId(targetId);
                float radius = ability.AoeRadius > 0 ? ability.AoeRadius : 5.0f;
                finalTargets.AddRange(FindTargetsInRadius(aoePosition, radius, source, ability.Intent));
                break;

            // --- LÓGICA DO CONE ADICIONADA ---
            case TargetType.Cone:
                // Para habilidades em cone, o 'targetId' é irrelevante, pois a origem e direção são do próprio conjurador.
                finalTargets.AddRange(FindTargetsInCone(source, ability));
                break;

            case TargetType.Projectile:
                if (FindEntityById(targetId) is { } projectileTarget)
                {
                    Console.WriteLine($"[COMBAT] Criando projétil de '{ability.ID}' para o alvo '{targetId}'.");
                }
                break;
        }

        // 2. Itera sobre cada alvo e aplica o efeito principal (esta parte não muda)
        foreach (var target in finalTargets)
        {
            if (target == null || (target.IsDead && ability.EffectType != AbilityEffectType.Resurrect)) continue;

            switch (ability.EffectType)
            {
                case AbilityEffectType.Damage:
                    float rawDamage = ability.BaseValue;
                    rawDamage += source.Stats.GetStatValue(StatType.AttackPower) * ability.AttackPowerScaling;
                    rawDamage += source.Stats.GetStatValue(StatType.SpellPower) * ability.SpellPowerScaling;

                    bool isCritical = _random.NextDouble() * 100 < source.Stats.GetStatValue(StatType.CriticalStrikeChance);
                    if (isCritical) rawDamage *= 2.0f;

                    float armor = target.Stats.GetStatValue(StatType.Armor);
                    float reduction = armor / (armor + 400 + 85 * source.Level);
                    float finalDamage = Math.Max(1, rawDamage * (1 - reduction));

                    target.TakeDamage((int)finalDamage, source, _server);

                    var eventType = isCritical ? CombatEventType.CriticalDamage : CombatEventType.PhysicalDamage;
                    BroadcastCombatEvent(target.Id, eventType, (int)finalDamage, isCritical);
                    BroadcastHealthUpdate(target);

                    if (target.CurrentHealth <= 0)
                    {
                        ProcessDeath(source, target);
                    }
                    break;

                case AbilityEffectType.Heal:
                    float rawHeal = ability.BaseValue + (source.Stats.GetStatValue(StatType.SpellPower) * ability.SpellPowerScaling);
                    bool isHealCrit = _random.NextDouble() * 100 < source.Stats.GetStatValue(StatType.CriticalStrikeChance);
                    if (isHealCrit) rawHeal *= 1.5f;

                    target.ReceiveHealing((int)rawHeal);
                    var healEventType = isHealCrit ? CombatEventType.CriticalHeal : CombatEventType.Heal;
                    BroadcastCombatEvent(target.Id, healEventType, (int)rawHeal, isHealCrit);
                    BroadcastHealthUpdate(target);
                    break;
            }
        }

        // 3. Notifica os clientes para tocarem as animações e VFX
        _server.NetworkManager.BroadcastMessageToAll($"EXECUTE_ABILITY|{source.Id}|{ability.ID}|{targetId}");
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
            Vector3.DistanceSquared(target.Position, center) <= radiusSqr &&
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
            // Ignora a si mesmo, alvos mortos, etc.
            if (target.Id == source.Id || target.IsDead) continue;

            // --- 1. Verificação de Distância ---
            if (Vector3.DistanceSquared(sourcePosition, target.Position) > coneRangeSqr)
            {
                continue; // Alvo está longe demais, fora do comprimento do cone.
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


    /// <summary>
    /// Verifica se uma entidade morreu após uma ação de combate e gerencia as consequências.
    /// </summary>
    private void ProcessDeath(ICombatEntity killer, ICombatEntity victim)
    {
        Console.WriteLine($"[MORTE] Entidade {victim.Id} foi derrotada por {killer.Id}.");

        // Notifica o cliente do jogador que ele morreu
        if (victim is Player deadPlayer)
        {
            _server.NetworkManager.SendMessageToClient("YOU_DIED", deadPlayer.EndPoint);
        }

        // Se um NPC morreu, inicia a lógica de recompensas e respawn
        if (victim is NpcInstance deadNpc)
        {
            _server.NpcAiManager.OnNpcKilled(deadNpc, killer);
        }

        // Notifica TODOS os clientes que a entidade morreu (para animações, etc.)
        // Adicionamos o 'hasLoot' aqui
        bool hasLoot = (victim is NpcInstance npc) ? npc.HasLoot : false;
        _server.NetworkManager.BroadcastMessageToAll($"ENTITY_DIED|{victim.Id}|{hasLoot}");
    }

    #region Métodos Auxiliares

    private void BroadcastCombatEvent(string targetId, CombatEventType eventType, int amount, bool isCritical)
    {
        _server.NetworkManager.BroadcastMessageToAll($"COMBAT_EVENT|{targetId}|{eventType}|{amount}|{isCritical}");
    }

    private void BroadcastHealthUpdate(ICombatEntity entity)
    {
        _server.NetworkManager.BroadcastMessageToAll($"ENTITY_HEALTH_UPDATE|{entity.Id}|{entity.CurrentHealth}|{entity.MaxHealth}");
    }

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

    private ICombatEntity? FindEntityById(string id)
    {
        if (string.IsNullOrEmpty(id) || id.ToLower() == "null") return null;
        if (_server.ActiveNpcs.TryGetValue(id, out var npc)) return npc;
        return _server.ConnectedPlayers.Values.FirstOrDefault(p => p.Id == id);
    }

    // Este método parece ser chamado de fora, então o mantemos público.
    public void ReportNpcDeath(NpcInstance npc, ICombatEntity? lastAttacker)
    {
        _server.NpcAiManager.OnNpcKilled(npc, lastAttacker);
    }
    #endregion
}