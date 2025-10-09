// Servidor/Managers/InterestManager.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Gerencia a visibilidade das entidades ("o que cada jogador vê") e a ativação da IA dos NPCs.
/// Isso ajuda a economizar largura de banda e CPU do servidor.
/// </summary>
public class InterestManager
{
    private readonly UDPServer _server;
    private const float AI_ACTIVATION_RANGE = 100f;
    private const float VISIBILITY_RANGE = 60f;

    public InterestManager(UDPServer server)
    {
        _server = server;
    }

    /// <summary>
    /// O loop principal do manager, que roda periodicamente para atualizar o estado do mundo.
    /// </summary>
    public async Task UpdateInterestAndActivationAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, cancellationToken);

                var players = _server.ConnectedPlayers.Values.ToList();
                var liveNpcs = _server.ActiveNpcs.Values.ToList();
                var deadNpcs = _server.DeadNpcCor_pses.Values.ToList();

                UpdateAiActivation(liveNpcs, players);

                if (!players.Any()) continue;

                foreach (var player in players)
                {
                    // (CORREÇÃO) Agora passamos a lista 'players' para o método.
                    UpdatePlayerVisibility(player, liveNpcs, deadNpcs, players);
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
    /// Ativa ou hiberna a IA de um NPC baseado na proximidade de QUALQUER jogador.
    /// </summary>
    private void UpdateAiActivation(List<NpcInstance> liveNpcs, List<Player> players)
    {
        if (!players.Any())
        {
            foreach (var npc in liveNpcs.Where(n => n.IsActive))
            {
                HibernateNpcAI(npc);
            }
            return;
        }

        foreach (var npc in liveNpcs)
        {
            var nearbyEntities = _server.GridManager.GetEntitiesInRadius(npc.Position, AI_ACTIVATION_RANGE);
            bool shouldBeActive = nearbyEntities.Any(e => e is Player);

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

    /// <summary>
    /// Para um jogador específico, envia mensagens SPAWN/DESTROY para as entidades que entram/saem de sua visão.
    /// </summary>
    private void UpdatePlayerVisibility(Player player, List<NpcInstance> liveNpcs, List<NpcInstance> deadNpcs, List<Player> allPlayers)
    {
        if (player.IsPendingInitialization) return;

        var entitiesInView = _server.GridManager.GetEntitiesInRadius(player.Position, VISIBILITY_RANGE).ToDictionary(e => e.Id);

        var knownEntities = player.KnownPlayerIds.Union(player.KnownNpcIds).ToHashSet();

        var newEntityIds = entitiesInView.Keys.Except(knownEntities).ToList();
        foreach (var entityId in newEntityIds)
        {
            IWorldEntity entity = entitiesInView[entityId];
            _server.NetworkManager.SendMessageToClient(entity.GetSpawnMessage(), player.EndPoint);
            if (entity is Player) player.KnownPlayerIds.Add(entityId);
            else if (entity is NpcInstance) player.KnownNpcIds.Add(entityId);
        }

        var oldEntityIds = knownEntities.Except(entitiesInView.Keys).ToList();
        foreach (var entityId in oldEntityIds)
        {
            string despawnMessage;
            if (player.KnownPlayerIds.Remove(entityId)) despawnMessage = $"PLAYER_LEFT|{entityId}";
            else if (player.KnownNpcIds.Remove(entityId)) despawnMessage = $"DESTROY_NPC|{entityId}";
            else continue;
            _server.NetworkManager.SendMessageToClient(despawnMessage, player.EndPoint);
        }
    }

    /// <summary>
    /// Chamado quando um jogador envia sua primeira atualização de posição,
    /// significando que ele está oficialmente "no mundo".
    /// </summary>
    public void OnPlayerEnteredWorld(Player newPlayer)
    {
        Console.WriteLine($"[InterestManager] {newPlayer.Username} entrou no mundo em {newPlayer.Position}. Sincronizando visibilidade.");

        var allLiveNpcs = _server.ActiveNpcs.Values.ToList();
        var allDeadNpcs = _server.DeadNpcCor_pses.Values.ToList();
        var allPlayers = _server.ConnectedPlayers.Values.ToList();

        // (CORREÇÃO) Passa a lista 'allPlayers' para a chamada do método.
        UpdatePlayerVisibility(newPlayer, allLiveNpcs, allDeadNpcs, allPlayers);

        foreach (var existingPlayer in allPlayers)
        {
            if (existingPlayer.Id == newPlayer.Id) continue;

            if (Vector3.DistanceSquared(existingPlayer.Position, newPlayer.Position) < VISIBILITY_RANGE * VISIBILITY_RANGE)
            {
                if (!existingPlayer.KnownPlayerIds.Contains(newPlayer.Id))
                {
                    _server.NetworkManager.SendMessageToClient(newPlayer.GetSpawnMessage(), existingPlayer.EndPoint);
                    existingPlayer.KnownPlayerIds.Add(newPlayer.Id);
                }
            }
        }
    }

    #region Métodos Auxiliares

    private void WakeUpNpcAI(NpcInstance npc)
    {
        if (npc.IsActive) return;
        npc.IsActive = true;
    }

    private void HibernateNpcAI(NpcInstance npc)
    {
        if (!npc.IsActive) return;
        npc.IsActive = false;
        // Não reseta a posição, pois isso pode causar saltos visuais se ele hibernar e acordar rapidamente.
        // npc.Position = npc.SpawnPosition;
        // npc.Destination = npc.SpawnPosition;
    }

    #endregion
}