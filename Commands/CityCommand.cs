using System.Numerics;

public class CityCommand : ICommand
{
    public string Name => "city";
    public string Description => "Teleporta você para a cidade principal. Custo: 10 Bronze. Não funciona em combate.";
    public string Usage => "/city";
    public int RequiredPermissionLevel => 0;
    private const int TELEPORT_COST = 10;

    public void Execute(Player sender, string[] args, UDPServer server)
    {
        if (sender == null)
        {
            Console.WriteLine("[Comando] Este comando só pode ser executado por um jogador.");
            return;
        }

        if (sender.IsInCombat)
        {
            server.ChatManager.SendSystemMessageToPlayer(sender, "Você não pode se teleportar durante o combate!");
            return;
        }

        if (sender.TotalBronze < TELEPORT_COST)
        {
            server.ChatManager.SendSystemMessageToPlayer(sender, $"Você precisa de {TELEPORT_COST} de bronze para usar este comando.");
            return;
        }

        // Debita o custo
        sender.TotalBronze -= TELEPORT_COST;

        // Posição da cidade principal
        Vector3 cityPosition = new Vector3(174, 7, 476); // Exemplo, use as coordenadas reais da sua cidade

        sender.Position = cityPosition;

        string message = $"FORCE_TELEPORT|{cityPosition.X:F2},{cityPosition.Y:F2},{cityPosition.Z:F2}";
        server.NetworkManager.SendMessageToPlayer(sender, message);

        server.ChatManager.SendSystemMessageToPlayer(sender, $"Você foi teleportado para a cidade por {TELEPORT_COST} de bronze.");
        // É uma boa prática enviar uma atualização de inventário/moeda também
        server.NetworkManager.SendCurrencyUpdate(sender);
    }
}