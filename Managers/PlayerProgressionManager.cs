using System;

public class PlayerProgressionManager
{
    private readonly UDPServer _server;
    public PlayerProgressionManager(UDPServer server) { _server = server; }

    public void GrantExperience(Player player, int amount)
    {
        if (player.Level >= ExperienceManager.MAX_LEVEL) return;

        player.CurrentExperience += amount;
        Console.WriteLine($"[XP] Jogador '{player.Username}' ganhou {amount} XP. Total: {player.CurrentExperience}/{ExperienceManager.GetExperienceForLevel(player.Level)}");

        // Envia a atualização de XP para o cliente
        string xpUpdateMessage = $"XP_UPDATE|{player.CurrentExperience}|{ExperienceManager.GetExperienceForLevel(player.Level)}";
        _server.NetworkManager.SendMessageToClient(xpUpdateMessage, player.EndPoint);

        // Verifica se o jogador subiu de nível (em um loop para suportar múltiplos níveis de uma vez)
        while (player.Level < ExperienceManager.MAX_LEVEL && player.CurrentExperience >= ExperienceManager.GetExperienceForLevel(player.Level))
        {
            LevelUp(player);
        }
    }

    private void LevelUp(Player player)
    {
        long requiredXp = ExperienceManager.GetExperienceForLevel(player.Level);
        long excessXp = player.CurrentExperience - requiredXp;

        player.Level++;
        player.CurrentExperience = excessXp;

        Console.WriteLine($"<color=yellow>[LEVEL UP] {player.Username} alcançou o nível {player.Level}!</color>");

        // --- ATUALIZAÇÕES CRÍTICAS NO LEVEL UP ---
        // 1. Recalcula todos os stats (base + de equipamento) para o novo nível
        player.RebuildStats();

        // 2. Enche a vida e o recurso para os novos valores máximos
        player.CurrentHealth = player.MaxHealth;
        player.CurrentResource = player.MaxResource;

        // 3. Recalcula as habilidades conhecidas (pode ter aprendido novas)
        player.KnownAbilityIDs = player.CalculateKnownAbilities();

        // --- NOTIFICAÇÕES PARA O CLIENTE ---
        // Notifica sobre o level up
        string levelUpMessage = $"LEVEL_UP|{player.Level}|{player.CurrentExperience}|{ExperienceManager.GetExperienceForLevel(player.Level)}";
        _server.NetworkManager.SendMessageToClient(levelUpMessage, player.EndPoint);

        // Envia o estado completo (incluindo novos stats e vida máxima)
        _server.NetworkManager.SendFullStateToPlayer(player);
    }
}