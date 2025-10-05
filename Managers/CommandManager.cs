// Managers/CommandManager.cs
using System;
using System.Linq;

public class CommandManager
{
    private readonly UDPServer _server;

    public CommandManager(UDPServer server)
    {
        _server = server;
    }

    /// <summary>
    /// Processa uma string de comando vinda do console.
    /// </summary>
    public void ProcessCommand(string commandLine)
    {
        // Divide o comando e os argumentos por espaços.
        string[] parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string command = parts[0].ToLower();
        string[] args = parts.Skip(1).ToArray();

        // Roteia o comando para o método apropriado.
        switch (command)
        {
            case "help":
                PrintHelp();
                break;

            case "giveitem":
                ExecuteGiveItem(args);
                break;

            case "say":
                ExecuteSay(args);
                break;

            // Adicione mais comandos aqui no futuro
            // case "kick": ...
            // case "teleport": ...

            default:
                Console.WriteLine($"[Comando] Comando desconhecido: '{command}'. Digite 'help' para ver a lista de comandos.");
                break;
        }
    }

    private void PrintHelp()
    {
        Console.WriteLine("\n--- Comandos do Servidor Krakovia ---");
        Console.WriteLine("giveitem <CharacterID> <ItemID> <Quantidade> - Dá um item a um jogador.");
        Console.WriteLine("say <Mensagem> - Envia uma mensagem global para todos os jogadores.");
        Console.WriteLine("-------------------------------------\n");
    }

    private void ExecuteGiveItem(string[] args)
    {
        // Validação: giveitem <charId> <itemId> <quantity>
        if (args.Length < 3)
        {
            Console.WriteLine("[Comando] Uso incorreto. Sintaxe: giveitem <CharacterID> <ItemID> <Quantidade>");
            return;
        }

        string characterId = args[0];
        string itemId = args[1];
        if (!int.TryParse(args[2], out int quantity) || quantity <= 0)
        {
            Console.WriteLine("[Comando] Erro: A quantidade deve ser um número inteiro positivo.");
            return;
        }

        // Tenta encontrar o jogador online pelo CharacterID
        Player? targetPlayer = _server.ConnectedPlayers.Values.FirstOrDefault(p => p.CharacterId.Equals(characterId, StringComparison.OrdinalIgnoreCase));

        if (targetPlayer == null)
        {
            Console.WriteLine($"[Comando] Erro: Jogador com CharacterID '{characterId}' não encontrado ou não está online.");
            return;
        }

        // Tenta adicionar o item
        if (targetPlayer.PlayerInventory.AddItem(itemId, quantity))
        {
            Console.WriteLine($"[Comando] Sucesso! {quantity}x '{itemId}' adicionado ao inventário de {targetPlayer.Username}.");

            // ESSENCIAL: Notifica o cliente do jogador sobre a mudança no inventário!
            _server.NetworkManager.SendInventoryUpdate(targetPlayer);
        }
        else
        {
            Console.WriteLine($"[Comando] Falha! O inventário de {targetPlayer.Username} está provavelmente cheio.");
        }
    }

    private void ExecuteSay(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("[Comando] Uso incorreto. Sintaxe: say <Mensagem>");
            return;
        }

        string message = string.Join(" ", args);
        string broadcastMessage = $"CHAT_MSG|[SERVIDOR]|{message}"; // Define um formato de mensagem de chat

        _server.NetworkManager.BroadcastMessageToAll(broadcastMessage);
        Console.WriteLine($"[Broadcast] Mensagem enviada para todos os jogadores: {message}");
    }
}