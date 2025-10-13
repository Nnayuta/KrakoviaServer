// Servidor/Commands/ICommand.cs
public interface ICommand
{
    string Name { get; } // O nome do comando (ex: "giveitem")
    string Description { get; } // A descrição para o comando "help"
    string Usage { get; } // A sintaxe (ex: "giveitem <charId> <itemId> <qty>")
    int RequiredPermissionLevel { get; } // Nível de permissão necessário (0=jogador, 1=moderador, 2=GM, etc.)

    void Execute(string[] args, UDPServer server);
}