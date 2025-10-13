// Servidor/Commands/GiveItemCommand.cs
public class GiveItemCommand : ICommand
{
    public string Name => "item";
    public string Description => "Dá um item a um jogador online.";
    public string Usage => "item <CharacterNameOrID> <ItemID> <Quantidade>";
    public int RequiredPermissionLevel => 2; // Apenas GMs

    public void Execute(string[] args, UDPServer server)
    {
        if (args.Length < 3)
        {
            Console.WriteLine($"[Comando] Uso incorreto. Sintaxe: {Usage}");
            return;
        }

        string playerNameOrId = args[0];
        string itemId = args[1];
        if (!int.TryParse(args[2], out int quantity) || quantity <= 0)
        {
            quantity = 1;
        }

        // Usa o novo método auxiliar para encontrar jogadores
        Player? targetPlayer = server.FindPlayerByNameOrId(playerNameOrId);

        if (targetPlayer == null)
        {
            Console.WriteLine($"[Comando] Erro: Jogador '{playerNameOrId}' não encontrado ou não está online.");
            return;
        }

        if (targetPlayer.PlayerInventory.AddItem(itemId, quantity))
        {
            Console.WriteLine($"[Comando] Sucesso! {quantity}x '{itemId}' adicionado ao inventário de {targetPlayer.CharacterName}.");
            server.NetworkManager.SendInventoryUpdate(targetPlayer);
        }
        else
        {
            Console.WriteLine($"[Comando] Falha! O inventário de {targetPlayer.CharacterName} está provavelmente cheio.");
        }
    }
}