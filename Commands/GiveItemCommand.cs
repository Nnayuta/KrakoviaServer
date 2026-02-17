public class GiveItemCommand : ICommand
{
    public string Name => "item";
    public string Description => "Dá um item a um jogador online.";
    public string Usage => "item <CharacterNameOrID> <ItemID> <Quantidade>";
    public int RequiredPermissionLevel => 50;

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

        // <<< CORREÇÃO 1: Captura o resultado do método AddItem >>>
        var changedSlots = targetPlayer.PlayerInventory.AddItem(itemId, quantity);

        // <<< CORREÇÃO 2: Verifica se o dicionário tem alguma entrada. Se tiver, foi um sucesso. >>>
        if (changedSlots.Any())
        {
            string successMsg = $"[Comando] Sucesso! {quantity}x '{itemId}' adicionado ao inventário de {targetPlayer.CharacterName}.";
            server.CommandManager.SendFeedbackToSender(sender, successMsg);

            // <<< CORREÇÃO 3: Itera sobre os slots alterados e envia uma atualização para cada um. >>>
            foreach (var kvp in changedSlots)
            {
                // kvp.Key = slotIndex, kvp.Value = ItemStack
                server.NetworkManager.SendInventorySlotUpdate(targetPlayer, kvp.Key, kvp.Value);
            }
        }
        else
        {
            server.CommandManager.SendFeedbackToSender(sender, $"[Comando] Falha! O inventário de {targetPlayer.CharacterName} está provavelmente cheio.");
        }
    }
}