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

    // Distância para um jogador ATIVAR A IA de um NPC (geralmente maior que a visibilidade).
    private const float AI_ACTIVATION_RANGE = 100f;
    // Distância para um jogador VER uma entidade (NPC, outro jogador, corpo).
    private const float VISIBILITY_RANGE = 80f;

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
                // Roda a verificação 2x por segundo.
                await Task.Delay(500, cancellationToken);

                var players = _server.ConnectedPlayers.Values.ToList();
                var liveNpcs = _server.ActiveNpcs.Values.ToList();

                // =================================================================================
                // ATUALIZAÇÃO: Agora também consideramos os corpos dos NPCs mortos.
                // =================================================================================
                var deadNpcs = _server.DeadNpcCor_pses.Values.ToList();

                // Passo 1: Gerenciar Ativação da IA (Apenas para NPCs vivos)
                UpdateAiActivation(liveNpcs, players);

                // Se não há jogadores online, não há necessidade de gerenciar a visibilidade.
                if (!players.Any()) continue;

                // Passo 2: Gerenciar Visibilidade para cada jogador, incluindo os corpos.
                foreach (var player in players)
                {
                    UpdatePlayerVisibility(player, liveNpcs, deadNpcs);
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
            bool shouldBeActive = players.Any(p => Vector3.DistanceSquared(npc.Position, p.Position) < AI_ACTIVATION_RANGE * AI_ACTIVATION_RANGE);
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
    private void UpdatePlayerVisibility(Player player, List<NpcInstance> liveNpcs, List<NpcInstance> deadNpcs)
    {
        var entitiesInView = new Dictionary<string, IWorldEntity>();
        var visRangeSqr = VISIBILITY_RANGE * VISIBILITY_RANGE;

        // 1. Adiciona NPCs VIVOS na visão
        foreach (var npc in liveNpcs)
        {
            if (Vector3.DistanceSquared(player.Position, npc.Position) < visRangeSqr)
            {
                entitiesInView[npc.Id] = npc;
            }
        }

        // 2. Adiciona CORPOS na visão
        foreach (var corpse in deadNpcs)
        {
            if (Vector3.DistanceSquared(player.Position, corpse.Position) < visRangeSqr)
            {
                entitiesInView[corpse.Id] = corpse;
            }
        }

        // 3. Adiciona outros JOGADORES na visão
        foreach (var otherPlayer in _server.ConnectedPlayers.Values)
        {
            if (player.Id != otherPlayer.Id && Vector3.DistanceSquared(player.Position, otherPlayer.Position) < visRangeSqr)
            {
                entitiesInView[otherPlayer.Id] = otherPlayer;
            }
        }

        // --- Lógica de "Diff" (Diferença) para decidir o que spawnar/destruir ---

        var knownEntities = player.KnownPlayerIds.Union(player.KnownNpcIds).ToHashSet();

        // Entidades que ENTRARAM na visão (estão em 'entitiesInView' mas não em 'knownEntities')
        var newEntityIds = entitiesInView.Keys.Except(knownEntities).ToList();
        foreach (var entityId in newEntityIds)
        {
            IWorldEntity entity = entitiesInView[entityId];
            _server.NetworkManager.SendMessageToClient(entity.GetSpawnMessage(), player.EndPoint);

            if (entity is Player) player.KnownPlayerIds.Add(entityId);
            else if (entity is NpcInstance) player.KnownNpcIds.Add(entityId);
        }

        // Entidades que SAÍRAM da visão (estão em 'knownEntities' mas não em 'entitiesInView')
        var oldEntityIds = knownEntities.Except(entitiesInView.Keys).ToList();
        foreach (var entityId in oldEntityIds)
        {
            string despawnMessage;
            if (player.KnownPlayerIds.Remove(entityId))
            {
                despawnMessage = $"PLAYER_LEFT|{entityId}";
            }
            else if (player.KnownNpcIds.Remove(entityId))
            {
                // A mensagem DESTROY_NPC agora é enviada corretamente quando um NPC
                // (vivo OU morto) sai da área de visibilidade.
                despawnMessage = $"DESTROY_NPC|{entityId}";
            }
            else continue;

            _server.NetworkManager.SendMessageToClient(despawnMessage, player.EndPoint);
        }
    }

    /// <summary>
    /// Chamado quando um novo jogador se conecta. Envia o estado inicial das entidades visíveis.
    /// </summary>
    public void OnPlayerConnected(Player newPlayer)
    {
        var allVisibleEntities = new List<IWorldEntity>();
        allVisibleEntities.AddRange(_server.ConnectedPlayers.Values);
        allVisibleEntities.AddRange(_server.ActiveNpcs.Values);
        allVisibleEntities.AddRange(_server.DeadNpcCor_pses.Values); // Inclui os corpos

        var entitiesInView = allVisibleEntities
            .Where(e => e.Id != newPlayer.Id && Vector3.DistanceSquared(newPlayer.Position, e.Position) < VISIBILITY_RANGE * VISIBILITY_RANGE)
            .ToList();

        if (!entitiesInView.Any()) return;

        var spawnMessages = new List<string>();
        foreach (var entity in entitiesInView)
        {
            spawnMessages.Add(entity.GetSpawnMessage());
            if (entity is Player) newPlayer.KnownPlayerIds.Add(entity.Id);
            else if (entity is NpcInstance) newPlayer.KnownNpcIds.Add(entity.Id);
        }

        if (spawnMessages.Any())
            _server.NetworkManager.SendMessageToClient(string.Join("\n", spawnMessages), newPlayer.EndPoint);
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
        npc.Position = npc.SpawnPosition;
        npc.Destination = npc.SpawnPosition;
    }

    #endregion
}