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
    private static readonly byte[] SharedBuffer = new byte[2048];
    private readonly UDPServer _server;
    private const float AI_ACTIVATION_RANGE = 100f;
    private const float VISIBILITY_RANGE = 60f;

    private static readonly float AI_ACTIVATION_RANGE_SQR = AI_ACTIVATION_RANGE * AI_ACTIVATION_RANGE;
    private static readonly float VISIBILITY_RANGE_SQR = VISIBILITY_RANGE * VISIBILITY_RANGE;

    private readonly Dictionary<string, IWorldEntity> _entityCache = new();
    private readonly HashSet<string> _reuseSet = new();

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
                var players = _server.ConnectedPlayers.Values.ToList();
                await Task.Delay(players.Count == 0 ? 1000 : 200, cancellationToken);


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
        if (players.Count == 0)
        {
            foreach (var npc in liveNpcs)
            {
                if (npc.IsActive) npc.IsActive = false;
            }
            return;
        }

        foreach (var npc in liveNpcs)
        {
            bool nearPlayer = false;
            var npcPos = npc.Position;

            foreach (var p in players)
            {
                if (p.IsPendingInitialization) continue;
                var dx = p.Position.X - npcPos.X;
                var dy = p.Position.Y - npcPos.Y;
                var dz = p.Position.Z - npcPos.Z;
                if ((dx * dx + dy * dy + dz * dz) < AI_ACTIVATION_RANGE_SQR)
                {
                    nearPlayer = true;
                    break;
                }
            }

            if (nearPlayer)
            {
                if (!npc.IsActive) npc.IsActive = true;
            }
            else
            {
                if (npc.IsActive) npc.IsActive = false;
            }
        }
    }

    /// <summary>
    /// Para um jogador específico, envia mensagens SPAWN/DESTROY para as entidades que entram/saem de sua visão.
    /// </summary>
    private void UpdatePlayerVisibility(Player player, List<NpcInstance> liveNpcs, List<NpcInstance> deadNpcs, List<Player> allPlayers)
    {
        if (player.IsPendingInitialization) return;

        _entityCache.Clear();
        var pos = player.Position;

        // Live NPCs
        foreach (var npc in liveNpcs)
        {
            var dx = npc.Position.X - pos.X;
            var dy = npc.Position.Y - pos.Y;
            var dz = npc.Position.Z - pos.Z;
            if ((dx * dx + dy * dy + dz * dz) < VISIBILITY_RANGE_SQR)
                _entityCache[npc.Id] = npc;
        }

        // Dead NPCs
        foreach (var corpse in deadNpcs)
        {
            var dx = corpse.Position.X - pos.X;
            var dy = corpse.Position.Y - pos.Y;
            var dz = corpse.Position.Z - pos.Z;
            if ((dx * dx + dy * dy + dz * dz) < VISIBILITY_RANGE_SQR)
                _entityCache[corpse.Id] = corpse;
        }

        // Players
        foreach (var other in allPlayers)
        {
            if (other.Id == player.Id || other.IsPendingInitialization) continue;
            var dx = other.Position.X - pos.X;
            var dy = other.Position.Y - pos.Y;
            var dz = other.Position.Z - pos.Z;
            if ((dx * dx + dy * dy + dz * dz) < VISIBILITY_RANGE_SQR)
                _entityCache[other.Id] = other;
        }

        _reuseSet.Clear();
        foreach (var id in player.KnownPlayerIds) _reuseSet.Add(id);
        foreach (var id in player.KnownNpcIds) _reuseSet.Add(id);

        // --- Spawn novos ---
        foreach (var kvp in _entityCache)
        {
            if (!_reuseSet.Contains(kvp.Key))
            {
                var entity = kvp.Value;
                _server.NetworkManager.SendMessageToClient(entity.GetSpawnMessage(), player.EndPoint);
                if (entity is Player)
                    player.KnownPlayerIds.Add(kvp.Key);
                else if (entity is NpcInstance)
                    player.KnownNpcIds.Add(kvp.Key);
            }
        }

        // --- Remover antigos ---
        foreach (var oldId in _reuseSet)
        {
            if (!_entityCache.ContainsKey(oldId))
            {
                string msg = player.KnownPlayerIds.Remove(oldId)
                    ? $"PLAYER_LEFT|{oldId}"
                    : player.KnownNpcIds.Remove(oldId)
                        ? $"DESTROY_NPC|{oldId}"
                        : null;
                if (msg != null)
                    _server.NetworkManager.SendMessageToClient(msg, player.EndPoint);
            }
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