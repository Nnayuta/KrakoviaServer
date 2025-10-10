// Managers/PlayerLifecycleManager.cs (NOVO ARQUIVO NO SERVIDOR)
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Globalization;

public class PlayerLifecycleManager
{
    private readonly UDPServer _server;

    // --- Configurações de Regeneração ---
    private const float REGEN_TICK_RATE_SECONDS = 2.0f;
    private const float HEALTH_REGEN_PERCENT = 0.02f;
    private const int ACTION_TICK_RATE_MS = 200;
    private DateTime _nextRegenTickTime;

    // Lista de cemitérios. No futuro, isso virá de um arquivo de dados.
    private readonly List<Vector3> _graveyardPositions = new List<Vector3>
    {
        new Vector3(124.7f, 7.46f, 425.8f), // Posição de exemplo
    };

    public PlayerLifecycleManager(UDPServer server)
    {
        _server = server;
        _nextRegenTickTime = _server.CurrentTimeUtc;
    }

    public async Task Action_LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ACTION_TICK_RATE_MS, cancellationToken);
                ProcessPlayerActions();
                CheckForLimboPlayers();
            }
            catch (TaskCanceledException) { break; }
        }
    }

    private void CheckForLimboPlayers()
    {
        // Pega uma cópia da lista de jogadores para iterar com segurança
        var playersToCheck = _server.ConnectedPlayers.Values.ToList();

        foreach (var player in playersToCheck)
        {
            // Verifica se a posição Y do jogador está abaixo do nosso limite
            if (player.Position.Y < ServerConfig.ANTI_LIMBO_Y_THRESHOLD)
            {
                Console.WriteLine($"[Anti-Limbo] Jogador {player.Username} detectado no limbo (Pos Y: {player.Position.Y}). Resgatando...");

                // Encontra o cemitério mais próximo da última posição "segura" conhecida do jogador
                Vector3 safePosition = FindClosestGraveyard(player.Position);

                // Atualiza a posição do jogador no servidor
                player.Position = safePosition;

                // Força o cliente a se teleportar para a nova posição segura
                // Criamos uma mensagem customizada para isso, para que o cliente saiba que foi um teleporte forçado.
                string posString = $"{safePosition.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                                   $"{safePosition.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                                   $"{safePosition.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

                _server.NetworkManager.SendMessageToClient($"FORCE_TELEPORT|{posString}", player.EndPoint);
                _server.GridManager.UpdateEntity(player);
            }
        }
    }

    private void ProcessPlayerActions()
    {
        // Pega uma cópia segura da lista de jogadores
        var players = _server.ConnectedPlayers.Values.ToList();

        // Verifica se é hora de processar a regeneração
        bool shouldProcessRegen = _server.CurrentTimeUtc >= _nextRegenTickTime;

        foreach (var player in players)
        {
            ProcessCasting(player);
            if (shouldProcessRegen)
            {
                ProcessSinglePlayerRegeneration(player);
            }
        }

        // Se a regeneração foi processada, agenda a próxima
        if (shouldProcessRegen)
        {
            _nextRegenTickTime = _server.CurrentTimeUtc.AddSeconds(REGEN_TICK_RATE_SECONDS);
        }
    }

    private void ProcessCasting(Player player)
    {
        // Se não está castando, sai
        if (!player.IsCasting) return;

        var ability = player.CurrentCastAbility;
        if (ability == null) // Checagem de segurança
        {
            player.InterruptCasting(false, _server.NetworkManager);
            return;
        }

        // VALIDAÇÃO DE INTERRUPÇÃO POR MOVIMENTO (acontece a cada tick)
        if (!ability.CanMoveWhileCasting && Vector3.DistanceSquared(player.Position, player.CastInitialPosition) > 0.25f) // Tolerância de 0.5m
        {
            // Console.WriteLine($"[Casting-Server] Casting de '{player.Username}' interrompido por movimento.");
            player.InterruptCasting(true, _server.NetworkManager);
            _server.NetworkManager.SendMessageToClient("SHOW_FEEDBACK|Conjuracao interrompida", player.EndPoint);
            return;
        }

        // Se o tempo de cast ainda não acabou, sai
        if (_server.CurrentTimeUtc < player.CastEndTime) return;

        // Casting concluído!
        var targetId = player.CurrentCastTargetId;
        if (targetId == null) return;

        // Limpa o estado de casting imediatamente (sem notificar, pois a execução vai acontecer)
        player.InterruptCasting(false, _server.NetworkManager);

        // --- VALIDAÇÕES FINAIS (NO MOMENTO DA CONCLUSÃO) ---
        // Se o recurso é insuficiente (pode ter sido gasto por outro efeito)
        if (player.CurrentResource < ability.ResourceCost)
        {
            _server.NetworkManager.SendMessageToClient($"ABILITY_FAILED|{ability.ID}|Recurso Insuficiente", player.EndPoint);
            return;
        }

        // ----------------------------------------------------

        // Aplica custos e cooldowns
        player.CurrentResource -= ability.ResourceCost;
        if (ability.Cooldown > 0) player.AbilityCooldowns[ability.ID] = _server.CurrentTimeUtc.AddSeconds(ability.Cooldown);

        // Notifica o cliente sobre o gasto de recurso
        _server.NetworkManager.SendVitalsUpdate(player);

        // APLICA OS EFEITOS
        _server.CombatManager.ApplyAbilityEffects(player, ability, targetId);
        _server.NetworkManager.BroadcastMessageToOthers(player, $"ENTITY_CAST_CANCEL|{player.Id}");
    }

    private void ProcessSinglePlayerRegeneration(Player player)
    {
        if (player.IsDead) return;

        bool vitalsChanged = false;

        // --- Regeneração de Vida (Apenas Fora de Combate) ---
        if (!player.IsInCombat && player.CurrentHealth < player.MaxHealth)
        {
            float healthRegenAmount = player.MaxHealth * HEALTH_REGEN_PERCENT;
            player.ReceiveHealing(healthRegenAmount, _server);
            vitalsChanged = true;
        }

        // =================================================================================
        // >> NOVA LÓGICA DE REGENERAÇÃO DE MANA <<
        // =================================================================================
        if (player.CurrentResource < player.MaxResource)
        {
            // Pega o valor base de regeneração (calculado a partir do Intelecto)
            float baseManaRegen = player.Stats.GetStatValue(StatType.ManaRegeneration);
            float finalManaRegen = 0;

            if (player.IsInCombat)
            {
                // Em combate, usa apenas uma porcentagem da regeneração base
                float combatRegenPercent = player.Stats.GetStatValue(StatType.CombatManaRegenPercent);
                finalManaRegen = baseManaRegen * combatRegenPercent;
            }
            else
            {
                // Fora de combate, usa 100% da regeneração base
                finalManaRegen = baseManaRegen;
            }

            if (finalManaRegen > 0)
            {
                player.ReceiveResource(finalManaRegen);
                vitalsChanged = true;
            }
        }

        // =================================================================================
        // Se a vida OU a mana mudaram, envia uma única atualização para o cliente.
        // Isso evita o envio de pacotes de rede desnecessários a cada tick.
        // =================================================================================
        if (vitalsChanged)
        {
            _server.NetworkManager.SendVitalsUpdate(player);
        }
    }

    /// <summary>
    /// Lida com a requisição de um jogador para ressuscitar.
    /// </summary>
    public void HandleRespawnRequest(Player player)
    {
        if (!player.IsDead) return;

        // 1. Encontra o cemitério mais próximo
        Vector3 respawnPosition = FindClosestGraveyard(player.Position);

        // 2. Atualiza o estado do jogador
        player.Respawn(respawnPosition);

        Console.WriteLine($"[RESPAWN] {player.Username} ressuscitou em {respawnPosition}.");

        // 3. Notifica o próprio cliente sobre o sucesso (continua igual)
        string posString = $"{respawnPosition.X.ToString(CultureInfo.InvariantCulture)},{respawnPosition.Y.ToString(CultureInfo.InvariantCulture)},{respawnPosition.Z.ToString(CultureInfo.InvariantCulture)}";
        _server.NetworkManager.SendMessageToClient($"RESPAWN_SUCCESSFUL|{posString}|{player.CurrentHealth}|{player.MaxHealth}", player.EndPoint);

        // 4. (CORREÇÃO) Notifica os jogadores PRÓXIMOS que esta entidade voltou à vida
        string message = $"ENTITY_RESURRECTED|{player.Id}|{player.CurrentHealth}|{player.MaxHealth}";

        // Usamos a NOVA posição de ressurreição como o centro do evento.
        _server.NetworkManager.BroadcastMessageToRelevantPlayers(respawnPosition, message);

        _server.GridManager.UpdateEntity(player);
    }


    private Vector3 FindClosestGraveyard(Vector3 playerPosition)
    {
        // Usa LINQ para encontrar a posição na lista que tem a menor distância do jogador
        return _graveyardPositions.OrderBy(pos => Vector3.Distance(playerPosition, pos)).FirstOrDefault();
    }
}