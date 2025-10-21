using System.Numerics;
using System.Globalization;

public class TpToMeCommand : ICommand
{
    public string Name => "tptome";
    public string Description => "Teleporta um jogador alvo para a sua posição atual.";
    public string Usage => "/tptome <CharacterNameOrID>";
    public int RequiredPermissionLevel => 50; // Nível GM

    public void Execute(Player sender, string[] args, UDPServer server)
    {
        // Este comando só faz sentido se executado por um jogador (o GM).
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
            server.CommandManager.SendFeedbackToSender(sender, "Você não pode teleportar a si mesmo.");
            return;
        }

        // 1. Pega a posição do GM que executou o comando.
        Vector3 gmPosition = sender.Position;

        // 2. Define a nova posição para o jogador alvo.
        targetPlayer.Position = gmPosition;

        // 3. Monta a mensagem para forçar o teleporte no cliente do alvo.
        //    (Usando a mesma lógica do seu Anti-Limbo)
        string posString = $"{gmPosition.X.ToString(CultureInfo.InvariantCulture)}," +
                           $"{gmPosition.Y.ToString(CultureInfo.InvariantCulture)}," +
                           $"{gmPosition.Z.ToString(CultureInfo.InvariantCulture)}";

        server.NetworkManager.SendMessageToPlayer(targetPlayer, $"FORCE_TELEPORT|{posString}");

        // 4. Atualiza a posição do jogador alvo no sistema de grid do servidor.
        server.GridManager.UpdateEntity(targetPlayer);

        // 5. Envia feedback para o GM.
        server.CommandManager.SendFeedbackToSender(sender, $"Você teleportou {targetPlayer.CharacterName} até você.");
        // (Opcional) Notifica o jogador que foi teleportado.
        server.ChatManager.SendSystemMessageToPlayer(targetPlayer, "Você foi teleportado por um administrador.");
    }
}