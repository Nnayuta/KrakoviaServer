using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

public class InterestManager
{
    private readonly UDPServer _server;

    // Raio maior para ativar a ÁREA. Se um jogador entrar neste raio, a área "acende".
    private const float SPAWN_AREA_ACTIVATION_RANGE_SQR = 120f * 120f;

    // Tempo em segundos que uma área deve ficar sem jogadores antes de ser desativada.
    private const int AREA_DEACTIVATION_TIME_SECONDS = 300; // 5 minutos

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
                // A frequência de checagem pode ser menor agora, pois é menos crítico que a visibilidade.
                await Task.Delay(2000, cancellationToken);

                var players = _server.ConnectedPlayers.Values.Where(p => !p.IsPendingInitialization).ToList();

                // A nova lógica principal que gerencia a ativação de áreas.
                UpdateSpawnPointActivation(players);

                // A lógica de visibilidade para cada jogador continua a mesma.
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
    /// (LÓGICA COMPLETAMENTE REFEITA)
    /// Itera por todos os SpawnPoints e decide se devem estar ativos ou inativos
    /// com base na proximidade dos jogadores.
    /// </summary>
    private void UpdateSpawnPointActivation(List<Player> players)
    {
        // Se não há jogadores online, desativa todas as áreas.
        if (!players.Any())
        {
            foreach (var sp in DataManager.SpawnPoints.Where(s => s.IsActive))
            {
                DeactivateSpawnPoint(sp);
            }
            return;
        }

        // Itera por cada ponto de spawn definido no jogo.
        foreach (var spawnPoint in DataManager.SpawnPoints)
        {
            // Verifica se QUALQUER jogador está dentro do raio de ativação da área.
            bool isPlayerNearby = players.Any(p => Vector3.DistanceSquared(spawnPoint.Position, p.Position) < SPAWN_AREA_ACTIVATION_RANGE_SQR);

            if (isPlayerNearby)
            {
                // Se um jogador está perto e a área está inativa, ativá-la.
                if (!spawnPoint.IsActive)
                {
                    ActivateSpawnPoint(spawnPoint);
                }
                // Atualiza o tempo de observação.
                spawnPoint.LastObservedTime = _server.CurrentTimeUtc;
            }
            else
            {
                // Se nenhum jogador está perto E a área está ativa...
                if (spawnPoint.IsActive)
                {
                    // ...verifica há quanto tempo ela não é observada.
                    if ((_server.CurrentTimeUtc - spawnPoint.LastObservedTime).TotalSeconds > AREA_DEACTIVATION_TIME_SECONDS)
                    {
                        // Se o tempo limite foi excedido, desativa a área.
                        DeactivateSpawnPoint(spawnPoint);
                    }
                }
            }
        }
    }

    /// <summary>
    /// (NOVO) Ativa uma área de spawn, chamando o WorldManager para criar os NPCs.
    /// </summary>
    private void ActivateSpawnPoint(SpawnPoint spawnPoint)
    {
        // (CORREÇÃO DE SEGURANÇA) Adiciona uma dupla verificação aqui.
        // Se, por alguma razão, este método for chamado para uma área que já tem NPCs,
        // nós apenas atualizamos o estado e saímos para evitar a duplicação.
        if (spawnPoint.ActiveNpcInstanceIds.Any())
        {
            spawnPoint.IsActive = true; // Garante que o estado esteja correto
            // Console.WriteLine($"[InterestManager-WARN] ActivateSpawnPoint chamado para a área '{spawnPoint.NpcTypeId}' que já continha NPCs. Apenas corrigindo o estado.");
            return;
        }

        spawnPoint.IsActive = true;
        _server.WorldManager.RespawnNpcsForSpawnPoint(spawnPoint);
    }

    /// <summary>
    /// (NOVO) Desativa uma área de spawn, chamando o WorldManager para remover os NPCs.
    /// </summary>
    private void DeactivateSpawnPoint(SpawnPoint spawnPoint)
    {
        spawnPoint.IsActive = false;
        _server.WorldManager.DespawnNpcsForSpawnPoint(spawnPoint);
    }


    /// <summary>
    /// A lógica de visibilidade para o jogador individual não precisa mudar.
    /// Ela funciona perfeitamente com a nova abordagem, pois ela apenas lê a lista
    /// de ActiveNpcs, que agora será populada e despovoada dinamicamente.
    /// </summary>
    private void UpdatePlayerVisibility(Player player)
    {
        if (player.IsPendingInitialization) return;

        var entitiesThatShouldBeVisible = _server.GridManager.GetEntitiesInRadius(player.Position, 80f)
            .Where(e => e.Id != player.Id)
            .ToDictionary(e => e.Id);

        var knownEntityIds = player.KnownPlayerIds.Union(player.KnownNpcIds).ToList();
        foreach (var knownId in knownEntityIds)
        {
            if (!entitiesThatShouldBeVisible.ContainsKey(knownId))
            {
                string despawnMessage;
                if (player.KnownPlayerIds.Remove(knownId))
                {
                    despawnMessage = $"PLAYER_LEFT|{knownId}";
                }
                else if (player.KnownNpcIds.Remove(knownId))
                {
                    despawnMessage = $"DESTROY_NPC|{knownId}";
                }
                else continue;

                _server.NetworkManager.SendMessageToClient(despawnMessage, player.EndPoint);
            }
        }

        foreach (var pair in entitiesThatShouldBeVisible)
        {
            var entityId = pair.Key;
            var entity = pair.Value;
            bool isPlayer = entity is Player;
            bool isKnown = (isPlayer && player.KnownPlayerIds.Contains(entityId)) || (!isPlayer && player.KnownNpcIds.Contains(entityId));

            if (!isKnown)
            {
                _server.NetworkManager.SendMessageToClient(entity.GetSpawnMessage(), player.EndPoint);
                if (isPlayer) player.KnownPlayerIds.Add(entityId);
                else player.KnownNpcIds.Add(entityId);
            }
        }
    }

    public void OnPlayerEnteredWorld(Player newPlayer)
    {
        Console.WriteLine($"[InterestManager] {newPlayer.Username} entrou no mundo. O próximo ciclo de visibilidade irá sincronizá-lo.");
    }

    // A lógica antiga de hibernação de IA individual (WakeUp/Hibernate) pode ser removida,
    // pois a ativação/desativação agora acontece no nível da área de spawn.
    // O loop de IA em NpcAiManager já filtra por `npc.IsActive`, que é definido como `true`
    // quando o NPC é spawnado em `WorldManager.SpawnSingleNpc`.
}