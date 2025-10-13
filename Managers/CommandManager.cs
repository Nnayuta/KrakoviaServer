// Servidor/Managers/CommandManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class CommandManager
{
    private readonly UDPServer _server;
    private readonly Dictionary<string, ICommand> _commands = new();

    public CommandManager(UDPServer server)
    {
        _server = server;
        RegisterCommands();
    }

    private void RegisterCommands()
    {
        // Usa Reflection para encontrar e instanciar todas as classes que implementam ICommand
        var commandTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetInterfaces().Contains(typeof(ICommand)) && t.GetConstructor(Type.EmptyTypes) != null);

        foreach (var type in commandTypes)
        {
            if (Activator.CreateInstance(type) is ICommand command)
            {
                _commands.Add(command.Name.ToLower(), command);
            }
        }
        Console.WriteLine($"[CommandManager] { _commands.Count} comandos registrados.");
    }

    // ProcessCommand agora verifica permissões!
    public void ProcessCommand(string commandLine, int permissionLevel = 99) // 99 = Console (poder máximo)
    {
        string[] parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string commandName = parts[0].ToLower();
        string[] args = parts.Skip(1).ToArray();

        if (_commands.TryGetValue(commandName, out var command))
        {
            if (permissionLevel >= command.RequiredPermissionLevel)
            {
                command.Execute(args, _server);
            }
            else
            {
                Console.WriteLine("[Comando] Permissão negada.");
                // Se o comando viesse de um jogador, você enviaria uma mensagem de erro para ele.
            }
        }
        else
        {
            Console.WriteLine($"[Comando] Comando desconhecido: '{commandName}'. Digite 'help' para ajuda.");
        }
    }
}