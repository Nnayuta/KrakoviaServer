using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

public class InterestManager
{
    private readonly UDPServer _server;

    // Raio para "acordar" a IA de um NPC. Geralmente maior que a visão do jogador.
    private const float AI_ACTIVATION_RANGE_SQR = 100f * 100f;

    // --- Constantes para a Histerese de Visibilidade ---
    // Raio MENOR: Se uma entidade entrar neste raio, ela se torna visível.
    private const float VISIBILITY_SPAWN_RADIUS = 80f;
    // Raio MAIOR: Uma entidade só se torna invisível se sair deste raio.
    private const float VISIBILITY_DESPAWN_RADIUS = 90f;

    public InterestManager(UDPServer server)
    {
        _server = server;
    }

    public async Task UpdateInterestAndActivationAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Um intervalo de 500ms é um bom equilíbrio para responsividade.
                await Task.Delay(500, cancellationToken);

                var players = _server.ConnectedPlayers.Values.Where(p => !p.IsPendingInitialization).ToList();

                // Passo 1: Gerencia a hibernação da IA (otimização de CPU do servidor).
                UpdateAiActivation(players);

                // Passo 2: Gerencia a visibilidade para cada cliente (o que eles renderizam).
                foreach (var player in players)
                {
                    UpdatePlayerVisibility(player);
                }
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[InterestManager] Loop cancelado para shutdown.");
                break;
            }
        }
    }

    /// <summary>
    /// Decide quais NPCs devem ter sua IA ativa (processando) ou inativa (hibernando).
    /// Esta função NÃO cria nem destrói NPCs.
    /// </summary>
    private void UpdateAiActivation(List<Player> players)
    {
        var allNpcs = _server.ActiveNpcs.Values;

        if (!players.Any())
        {
            // Se não há jogadores, todos os NPCs que estão ativos e vivos devem hibernar.
            foreach (var npc in allNpcs.Where(n => n.IsActive && !n.IsDead))
            {
                HibernateNpcAI(npc);
            }
            return;
        }

        // Itera sobre todos os NPCs do mundo.
        foreach (var npc in allNpcs)
        {
            // NPCs mortos não têm IA, então os pulamos.
            if (npc.IsDead) continue;

            // Verifica se ALGUM jogador está perto o suficiente para "acordar" o NPC.
            bool shouldBeActive = players.Any(p => Vector3.DistanceSquared(npc.Position, p.Position) < AI_ACTIVATION_RANGE_SQR);

            if (shouldBeActive && !npc.IsActive)
            {
                WakeUpNpcAI(npc);
            }
            else if (!shouldBeActive && npc.IsActive)
            {
                HibernateNpcAI(npc);
            }
        }

    }

    // Arquivo: InterestManager.cs

    private void UpdatePlayerVisibility(Player player)
    {
        if (player.IsPendingInitialization) return;

        // Passo 1: Pega as entidades E FILTRA AS MORTAS
        // <<< CORREÇÃO >>> ADICIONE O FILTRO .Where(...) AQUI!
        var entitiesThatShouldBeVisible = _server.GridManager
            .GetEntitiesInRadius(player.Position, VISIBILITY_SPAWN_RADIUS)
            .Where(e => e.Id != player.Id && !(e is NpcInstance npc && npc.IsDead)) // Adicione esta linha .Where
            .Select(e => e.Id)
            .ToHashSet();

        // Passo 2: Crie um HashSet com todas as entidades que o jogador JÁ CONHECE.
        var entitiesCurrentlyKnown = player.KnownPlayerIds
            .Union(player.KnownNpcIds)
            .ToHashSet();

        // Passo 3: Calcule a diferença. Quais entidades precisam ser SPAWNADAS?
        var entitiesToSpawn = new HashSet<string>(entitiesThatShouldBeVisible);
        entitiesToSpawn.ExceptWith(entitiesCurrentlyKnown);

        foreach (var idToSpawn in entitiesToSpawn)
        {
            // Encontra a entidade real para obter seus dados de spawn
            IWorldEntity? entity = _server.GetWorldEntityById(idToSpawn);
            if (entity != null)
            {
                // O log pode ser removido se quiser, mas é útil para depurar
                // Console.WriteLine($"[VIS-FIX] Spawning NEW entity {entity.Id} for {player.Id}");
                _server.NetworkManager.SendMessageToClient(entity.GetSpawnMessage(), player.EndPoint);

                // Atualiza o estado do jogador
                if (entity is Player) player.KnownPlayerIds.Add(idToSpawn);
                else player.KnownNpcIds.Add(idToSpawn);
            }
        }

        // Passo 4: Calcule a outra diferença. Quais entidades precisam ser DESPAWNADAS?
        var entitiesToDespawn = new HashSet<string>(entitiesCurrentlyKnown);
        entitiesToDespawn.ExceptWith(entitiesThatShouldBeVisible);

        foreach (var idToDespawn in entitiesToDespawn)
        {
            // O log pode ser removido se quiser
            // Console.WriteLine($"[VIS-FIX] Despawning OLD entity {idToDespawn} for {player.Id}");

            string despawnMessage;
            if (player.KnownPlayerIds.Remove(idToDespawn))
            {
                despawnMessage = $"PLAYER_LEFT|{idToDespawn}";
            }
            else if (player.KnownNpcIds.Remove(idToDespawn))
            {
                despawnMessage = $"DESTROY_NPC|{idToDespawn}";
            }
            else continue;

            _server.NetworkManager.SendMessageToClient(despawnMessage, player.EndPoint);
        }
    }

    public void OnPlayerEnteredWorld(Player newPlayer)
    {
        Console.WriteLine($"[InterestManager] {newPlayer.Username} entrou no mundo. O próximo ciclo de visibilidade irá sincronizá-lo.");
    }

    private void WakeUpNpcAI(NpcInstance npc)
    {
        if (npc.IsActive) return;
        npc.IsActive = true;
    }

    private void HibernateNpcAI(NpcInstance npc)
    {
        if (!npc.IsActive) return;
        npc.IsActive = false;
    }
}