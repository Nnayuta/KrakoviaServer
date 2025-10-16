// Servidor/Managers/ChatManager.cs
using System;
using System.Linq;

public class ChatManager
{
    private readonly UDPServer _server;

    public ChatManager(UDPServer server)
    {
        _server = server;
    }

    /// <summary>
    /// Ponto de entrada principal para qualquer mensagem de chat vinda de um jogador.
    /// </summary>
    public void ProcessChatMessage(Player sender, string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage)) return;

        // Limita o tamanho da mensagem para prevenir spam/ataques
        if (rawMessage.Length > 150)
        {
            rawMessage = rawMessage.Substring(0, 150);
        }

        // Verifica se é um comando (começa com '/')
        if (rawMessage.StartsWith("/"))
        {
            ParseCommand(sender, rawMessage);
        }
        else
        {
            // Se não for um comando, o padrão é o chat "Say"
            HandleSayChat(sender, rawMessage);
        }
    }

    private void ParseCommand(Player sender, string message)
    {
        string[] parts = message.Split(' ', 3); // Divide em no máximo 3 partes: /comando, alvo, mensagem
        string command = parts[0].ToLower();

        switch (command)
        {
            case "/s":
            case "/say":
                HandleSayChat(sender, parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "");
                break;

            case "/w":
            case "/whisper":
                if (parts.Length < 3)
                {
                    SendSystemMessageToPlayer(sender, "Uso: /whisper <PlayerName> <Message>");
                    return;
                }
                HandleWhisperChat(sender, parts[1], parts[2]);
                break;

            // Futuramente, você pode adicionar /party, /guild, etc. aqui.

            default:
                SendSystemMessageToPlayer(sender, $"Comando desconhecido: '{command}'");
                break;
        }
    }

    private void HandleSayChat(Player sender, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        // Formato: CHAT_MSG|Channel|SenderName|MessageText
        string formattedMessage = $"CHAT_MSG|SAY|{sender.CharacterName}|{message}";

        // Envia a mensagem apenas para jogadores próximos! Isso é crucial.
        _server.NetworkManager.BroadcastMessageToRelevantPlayers(sender.Position, formattedMessage);
        Console.WriteLine($"[CHAT-SAY] {sender.CharacterName}: {message}");
    }

    private void HandleWhisperChat(Player sender, string targetName, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        // Procura o jogador alvo (usando o método que já criamos!)
        Player? targetPlayer = _server.FindPlayerByNameOrId(targetName);

        if (targetPlayer == null)
        {
            SendSystemMessageToPlayer(sender, $"Jogador '{targetName}' não encontrado ou não está online.");
            return;
        }

        // Não permite sussurrar para si mesmo
        if (targetPlayer.Id == sender.Id)
        {
            SendSystemMessageToPlayer(sender, "Você não pode sussurrar para si mesmo.");
            return;
        }

        // Envia a mensagem para o alvo
        string messageToTarget = $"CHAT_MSG|WHISPER_RECV|{sender.CharacterName}|{message}";
        _server.NetworkManager.SendMessageToPlayer(targetPlayer, messageToTarget);

        // Envia uma confirmação para o remetente (para ele ver o que enviou)
        string messageToSender = $"CHAT_MSG|WHISPER_SENT|{targetPlayer.CharacterName}|{message}";
        _server.NetworkManager.SendMessageToPlayer(sender, messageToSender);

        Console.WriteLine($"[CHAT-WHISPER] {sender.CharacterName} -> {targetPlayer.CharacterName}: {message}");
    }

    // Método auxiliar para enviar mensagens de erro/sistema
    public void SendSystemMessageToPlayer(Player player, string message)
    {
        string formattedMessage = $"CHAT_MSG|SYSTEM|Servidor|{message}";
        _server.NetworkManager.SendMessageToPlayer(player, formattedMessage);
    }
}