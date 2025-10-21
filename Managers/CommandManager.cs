using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text; // Adicionado para o HelpCommand

public class CommandManager
{
    private readonly UDPServer _server;
    // Trocado para public readonly para que o HelpCommand possa acessá-lo facilmente
    public readonly Dictionary<string, ICommand> _commands = new();

    public CommandManager(UDPServer server)
    {
        _server = server;
        RegisterCommands();
    }

    private void RegisterCommands()
    {
        var commandTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetInterfaces().Contains(typeof(ICommand)) && t.GetConstructor(Type.EmptyTypes) != null);

        foreach (var type in commandTypes)
        {
            if (Activator.CreateInstance(type) is ICommand command)
            {
                _commands.Add(command.Name.ToLower(), command);
            }
        }
        Console.WriteLine($"[CommandManager] {_commands.Count} comandos registrados.");
    }

    // MÉTODO ALTERADO: Agora recebe um 'Player' como remetente.
    public void ProcessCommand(Player sender, string commandLine)
    {
        string[] parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string commandName = parts[0].ToLower();
        string[] args = parts.Skip(1).ToArray();

        // Define o nível de permissão com base em quem está executando
        int permissionLevel = (sender != null) ? sender.PermissionLevel : 99; // 99 para console

        if (_commands.TryGetValue(commandName, out var command))
        {
            if (permissionLevel >= command.RequiredPermissionLevel)
            {
                // Passa o 'sender' para o método Execute
                command.Execute(sender, args, _server);
            }
            else
            {
                // Envia a mensagem de erro para o jogador, se aplicável
                SendFeedbackToSender(sender, "[Comando] Permissão negada.");
            }
        }
        else
        {
            SendFeedbackToSender(sender, $"[Comando] Comando desconhecido: '{commandName}'. Digite '/help' para ajuda.");
        }
    }

    // NOVO: Método auxiliar para enviar feedback para o console ou para o jogador.
    public void SendFeedbackToSender(Player sender, string message)
    {
        if (sender == null)
        {
            Console.WriteLine(message);
        }
        else
        {
            // Usa o ChatManager para enviar uma mensagem de sistema privada.
            _server.ChatManager.SendSystemMessageToPlayer(sender, message);
        }
    }
}