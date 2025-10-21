using System.Globalization;

public class ReviveCommand : ICommand
{
    public string Name => "revive";
    public string Description => "Ressuscita um jogador morto em sua posição atual.";
    public string Usage => "/revive <CharacterNameOrID>";
    public int RequiredPermissionLevel => 50; // Nível GM

    public void Execute(Player sender, string[] args, UDPServer server)
    {
        if (args.Length < 1)
        {
            server.CommandManager.SendFeedbackToSender(sender, $"Uso: {Usage}");
            return;
        }

        Player? targetPlayer = server.FindPlayerByNameOrId(args[0]);

        if (targetPlayer == null)
        {
            server.CommandManager.SendFeedbackToSender(sender, $"Jogador '{args[0]}' não encontrado ou não está online.");
            return;
        }

        if (!targetPlayer.IsDead)
        {
            server.CommandManager.SendFeedbackToSender(sender, $"{targetPlayer.CharacterName} já está vivo.");
            return;
        }

        // 1. Pega a posição atual do corpo do jogador.
        var respawnPosition = targetPlayer.Position;

        // 2. Chama o método Respawn do jogador para restaurar sua vida e estado.
        targetPlayer.Respawn(respawnPosition);

        Console.WriteLine($"[GM Command] {targetPlayer.Username} foi ressuscitado por {(sender?.Username ?? "Console")}.");

        // 3. Notifica o próprio jogador que ele ressuscitou com sucesso.
        //    (Lógica copiada e adaptada de HandleRespawnRequest)
        string posString = $"{respawnPosition.X.ToString(CultureInfo.InvariantCulture)},{respawnPosition.Y.ToString(CultureInfo.InvariantCulture)},{respawnPosition.Z.ToString(CultureInfo.InvariantCulture)}";
        server.NetworkManager.SendMessageToPlayer(targetPlayer, $"RESPAWN_SUCCESSFUL|{posString}|{targetPlayer.CurrentHealth}|{targetPlayer.MaxHealth}");

        // 4. Notifica os jogadores próximos que a entidade voltou à vida.
        string message = $"ENTITY_RESURRECTED|{targetPlayer.Id}|{targetPlayer.CurrentHealth}|{targetPlayer.MaxHealth}";
        server.NetworkManager.BroadcastMessageToRelevantPlayers(respawnPosition, message);

        // 5. Atualiza o jogador no grid, caso necessário.
        server.GridManager.UpdateEntity(targetPlayer);

        // 6. Envia feedback para o GM.
        server.CommandManager.SendFeedbackToSender(sender, $"{targetPlayer.CharacterName} foi ressuscitado.");
    }
}