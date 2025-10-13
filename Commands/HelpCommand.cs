// Servidor/Commands/HelpCommand.cs
public class HelpCommand : ICommand
{
    public string Name => "help";
    public string Description => "Mostra a lista de comandos disponíveis.";
    public string Usage => "help";
    public int RequiredPermissionLevel => 0; // Todos podem usar

    public void Execute(string[] args, UDPServer server)
    {
        // Reutiliza a lógica do CommandManager para buscar os comandos
        // (Isso requer que o Dictionary _commands no CommandManager seja público, ou passar o manager como parâmetro)
        // Para simplificar, vamos hardcodar por enquanto.
        Console.WriteLine("\n--- Comandos do Servidor Krakovia ---");
        Console.WriteLine("giveitem <CharacterNameOrID> <ItemID> <Quantidade> - Dá um item a um jogador.");
        Console.WriteLine("say <Mensagem> - Envia uma mensagem global para todos os jogadores.");
        Console.WriteLine("kick <CharacterNameOrID> [Motivo] - Desconecta um jogador.");
        Console.WriteLine("-------------------------------------\n");
    }
}