public class GiveItemCommand : ICommand
{
    public string Name => "item";
    public string Description => "Dá um item a um jogador online.";
    public string Usage => "item <CharacterNameOrID> <ItemID> <Quantidade>";
    public int RequiredPermissionLevel => 50; // Apenas GMs

    public void Execute(Player sender, string[] args, UDPServer server)
    {
        if (args.Length < 3)
        {
            server.CommandManager.SendFeedbackToSender(sender, $"[Comando] Uso incorreto. Sintaxe: {Usage}");
            return;
        }

        string playerNameOrId = args[0];
        string itemId = args[1];
        if (!int.TryParse(args[2], out int quantity) || quantity <= 0)
        {
            quantity = 1;
        }

        Player? targetPlayer = server.FindPlayerByNameOrId(playerNameOrId);

        if (targetPlayer == null)
        {
            server.CommandManager.SendFeedbackToSender(sender, $"[Comando] Erro: Jogador '{playerNameOrId}' não encontrado ou não está online.");
            return;
        }

        if (targetPlayer.PlayerInventory.AddItem(itemId, quantity))
        {
            string successMsg = $"[Comando] Sucesso! {quantity}x '{itemId}' adicionado ao inventário de {targetPlayer.CharacterName}.";
            server.CommandManager.SendFeedbackToSender(sender, successMsg);
            server.NetworkManager.SendInventoryUpdate(targetPlayer);
        }
        else
        {
            server.CommandManager.SendFeedbackToSender(sender, $"[Comando] Falha! O inventário de {targetPlayer.CharacterName} está provavelmente cheio.");
        }
    }
}