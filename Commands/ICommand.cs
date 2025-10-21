public interface ICommand
{
    string Name { get; }
    string Description { get; }
    string Usage { get; }
    int RequiredPermissionLevel { get; }

    // ALTERADO: Adicionamos o parâmetro 'Player sender'.
    // Ele será 'null' se o comando for executado pelo console do servidor.
    void Execute(Player sender, string[] args, UDPServer server);
}