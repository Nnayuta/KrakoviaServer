using System.Numerics;

public class UnstuckCommand : ICommand
{
    public string Name => "unstuck";
    public string Description => "Teleporta você para um local seguro se estiver preso. Não funciona em combate.";
    public string Usage => "/unstuck";
    public int RequiredPermissionLevel => 0; // Todos podem usar

    public void Execute(Player sender, string[] args, UDPServer server)
    {
        if (sender == null)
        {
            Console.WriteLine("[Comando] Este comando só pode ser executado por um jogador.");
            return;
        }

        if (sender.IsInCombat)
        {
            server.ChatManager.SendSystemMessageToPlayer(sender, "Você não pode usar /unstuck enquanto estiver em combate!");
            return;
        }

        // Define uma posição segura (ex: ponto de respawn da cidade principal)
        // Você pode tornar isso mais inteligente no futuro.
        Vector3 safePosition = new Vector3(174, 7, 476); // Posição que você usou como padrão em PlayerState

        sender.Position = safePosition;

        // Envia uma mensagem para o cliente confirmando o teleporte
        // O cliente precisa saber interpretar a mensagem "TELEPORT_PLAYER"
        string message = $"FORCE_TELEPORT|{safePosition.X:F2},{safePosition.Y:F2},{safePosition.Z:F2}";
        server.NetworkManager.SendMessageToPlayer(sender, message);

        server.ChatManager.SendSystemMessageToPlayer(sender, "Você foi teleportado para um local seguro.");
    }
}