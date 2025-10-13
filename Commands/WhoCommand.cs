// Servidor/Commands/WhoCommand.cs (um novo comando)

public class WhoCommand : ICommand
{
    public string Name => "who";
    public string Description => "Lista todos os jogadores online e seus IDs de sessão.";
    public string Usage => "who";
    public int RequiredPermissionLevel => 1; // Apenas moderadores e acima

    public void Execute(string[] args, UDPServer server)
    {
        var players = server.ConnectedPlayers.Values.ToList();

        if (!players.Any())
        {
            Console.WriteLine("[Servidor] Não há jogadores online.");
            return;
        }

        Console.WriteLine("\n--- Jogadores Online ---");
        // Formata uma tabela bonita no console
        Console.WriteLine($"{"ID",-5} {"Nome",-18} {"Nível",-7} {"Classe",-12} {"Posição (X, Z)"}");
        Console.WriteLine(new string('-', 60));

        foreach (var player in players)
        {
            string position = $"{player.Position.X:F0}, {player.Position.Z:F0}";
            Console.WriteLine($"{player.SessionId,-5} {player.CharacterName,-18} {player.Level,-7} {player.ClassID,-12} {position}");
        }
        Console.WriteLine("------------------------\n");
    }
}