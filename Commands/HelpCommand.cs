using System.Text;

public class HelpCommand : ICommand
{
    public string Name => "help";
    public string Description => "Mostra a lista de comandos disponíveis para seu nível de permissão.";
    public string Usage => "/help";
    public int RequiredPermissionLevel => 0;

    public void Execute(Player sender, string[] args, UDPServer server)
    {
        int permissionLevel = sender?.PermissionLevel ?? 99;
        var availableCommands = server.CommandManager._commands.Values
            .Where(cmd => permissionLevel >= cmd.RequiredPermissionLevel)
            .OrderBy(cmd => cmd.Name);

        var sb = new StringBuilder();
        sb.AppendLine("--- Comandos Disponíveis ---");

        foreach (var cmd in availableCommands)
        {
            sb.AppendLine($"{cmd.Usage} - {cmd.Description}");
        }

        server.CommandManager.SendFeedbackToSender(sender, sb.ToString());
    }
}