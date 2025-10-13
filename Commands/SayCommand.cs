// Servidor/Commands/SayCommand.cs
public class SayCommand : ICommand
{
    public string Name => "say";
    public string Description => "Envia uma mensagem global para todos os jogadores.";
    public string Usage => "say <Mensagem>";
    public int RequiredPermissionLevel => 1; // Moderadores e acima

    public void Execute(string[] args, UDPServer server)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("[Comando] Uso incorreto. Sintaxe: say <Mensagem>");
            return;
        }

        string message = string.Join(" ", args);
        string broadcastMessage = $"CHAT_MSG|[SERVIDOR]|{message}"; // Define um formato de mensagem de chat

        server.NetworkManager.BroadcastMessageToAll(broadcastMessage);
        Console.WriteLine($"[Broadcast] Mensagem enviada para todos os jogadores: {message}");
    }
}