using System.Numerics;
using System.Globalization;

public class TpToCommand : ICommand
{
    public string Name => "tpto";
    public string Description => "Teleporta você para a posição de um jogador alvo.";
    public string Usage => "/tpto <CharacterNameOrID>";
    public int RequiredPermissionLevel => 50; // Nível GM

    public void Execute(Player sender, string[] args, UDPServer server)
    {
        if (sender == null)
        {
            Console.WriteLine("[Comando] Este comando só pode ser executado por um GM dentro do jogo.");
            return;
        }

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

        if (sender.Id == targetPlayer.Id)
        {
            server.CommandManager.SendFeedbackToSender(sender, "Você já está aqui!");
            return;
        }

        // 1. Pega a posição do jogador alvo.
        Vector3 targetPosition = targetPlayer.Position;

        // 2. Define a nova posição para o GM.
        sender.Position = targetPosition;

        // 3. Monta e envia a mensagem de teleporte para o GM.
        string posString = $"{targetPosition.X.ToString(CultureInfo.InvariantCulture)}," +
                           $"{targetPosition.Y.ToString(CultureInfo.InvariantCulture)}," +
                           $"{targetPosition.Z.ToString(CultureInfo.InvariantCulture)}";

        server.NetworkManager.SendMessageToPlayer(sender, $"FORCE_TELEPORT|{posString}");

        // 4. Atualiza a posição do GM no grid.
        server.GridManager.UpdateEntity(sender);

        // 5. Envia feedback para o GM.
        server.CommandManager.SendFeedbackToSender(sender, $"Teleportado para a posição de {targetPlayer.CharacterName}.");
    }
}