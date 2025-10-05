// Managers/PlayerLifecycleManager.cs (NOVO ARQUIVO NO SERVIDOR)
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;

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
        new Vector3(10, 1, 10), // Posição de exemplo
        new Vector3(-50, 1, -50) // Outra posição de exemplo
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
            }
            catch (TaskCanceledException) { break; }
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
            player.InterruptCasting();
            return;
        }

        // VALIDAÇÃO DE INTERRUPÇÃO POR MOVIMENTO (acontece a cada tick)
        if (!ability.CanMoveWhileCasting && Vector3.DistanceSquared(player.Position, player.CastInitialPosition) > 0.25f) // Tolerância de 0.5m
        {
            // Console.WriteLine($"[Casting-Server] Casting de '{player.Username}' interrompido por movimento.");
            player.InterruptCasting(true, _server.NetworkManager);
            _server.NetworkManager.SendMessageToClient("SHOW_FEEDBACK|Conjuração interrompida: Você se moveu.", player.EndPoint);
            return;
        }

        // Se o tempo de cast ainda não acabou, sai
        if (_server.CurrentTimeUtc < player.CastEndTime) return;

        // Casting concluído!
        var targetId = player.CurrentCastTargetId;

        // Limpa o estado de casting imediatamente (sem notificar, pois a execução vai acontecer)
        player.InterruptCasting(false);

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
    }

    private void ProcessSinglePlayerRegeneration(Player player)
    {
        if (!player.IsDead && !player.IsInCombat && player.CurrentHealth < player.MaxHealth)
        {
            float regenAmount = player.MaxHealth * HEALTH_REGEN_PERCENT;
            player.ReceiveHealing(regenAmount);
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

        // 2. Chama o método de respawn no próprio jogador para atualizar seu estado
        player.Respawn(respawnPosition);

        Console.WriteLine($"[RESPAWN] {player.Username} ressuscitou em {respawnPosition}.");

        // 3. Notifica o cliente sobre o sucesso, sua nova posição e estado de vida
        string posString = $"{respawnPosition.X},{respawnPosition.Y},{respawnPosition.Z}";
        _server.NetworkManager.SendMessageToClient($"RESPAWN_SUCCESSFUL|{posString}|{player.CurrentHealth}|{player.MaxHealth}", player.EndPoint);

        // 4. Notifica todos os outros jogadores que esta entidade voltou à vida
        _server.NetworkManager.BroadcastMessageToAll($"ENTITY_RESURRECTED|{player.Id}|{player.CurrentHealth}|{player.MaxHealth}");
    }

    private Vector3 FindClosestGraveyard(Vector3 playerPosition)
    {
        // Usa LINQ para encontrar a posição na lista que tem a menor distância do jogador
        return _graveyardPositions.OrderBy(pos => Vector3.Distance(playerPosition, pos)).FirstOrDefault();
    }
}