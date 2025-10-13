// Servidor/ServerStatusHtmlGenerator.cs
using System;
using System.Linq;
using System.Text;

public static class ServerStatusHtmlGenerator
{
    public static string Generate()
    {
        var sb = new StringBuilder();

        // Obtém a instância do servidor UDP para acessar os dados
        var udpServer = UDPServer.Instance;

        // --- Início do HTML ---
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang='pt-br'>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset='UTF-s8'>");
        sb.AppendLine("<title>Status do Servidor Krakovia</title>");
        // Adiciona um refresh automático a cada 10 segundos
        sb.AppendLine("<meta http-equiv='refresh' content='10'>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #1e1e1e; color: #dcdcdc; }");
        sb.AppendLine("h1, h2 { color: #569cd6; border-bottom: 2px solid #569cd6; padding-bottom: 5px; }");
        sb.AppendLine("table { width: 80%; border-collapse: collapse; margin-top: 20px; }");
        sb.AppendLine("th, td { border: 1px solid #444; padding: 10px; text-align: left; }");
        sb.AppendLine("th { background-color: #2a2a2a; color: #9cdcfe; }");
        sb.AppendLine("tr:nth-child(even) { background-color: #2a2a2a; }");
        sb.AppendLine(".container { width: 90%; margin: auto; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class='container'>");

        sb.AppendLine("<h1>Status do Servidor Krakovia</h1>");

        // --- Seção de Status Geral ---
        sb.AppendLine("<h2>Informações Gerais</h2>");
        if (udpServer != null)
        {
            sb.AppendLine($"<p><strong>Jogadores Online:</strong> {udpServer.ConnectedPlayers.Count}</p>");
            sb.AppendLine($"<p><strong>NPCs Ativos (IA processando):</strong> {udpServer.ActiveNpcs.Values.Count(n => n.IsActive)}</p>");
            sb.AppendLine($"<p><strong>Total de NPCs no Mundo:</strong> {udpServer.ActiveNpcs.Count}</p>");
            sb.AppendLine($"<p><strong>Hora do Servidor (UTC):</strong> {udpServer.CurrentTimeUtc:yyyy-MM-dd HH:mm:ss}</p>");
        }
        else
        {
            sb.AppendLine("<p>Servidor de mundo não iniciado.</p>");
        }

        // --- Tabela de Jogadores Online ---
        sb.AppendLine("<h2>Jogadores Online</h2>");
        if (udpServer != null && udpServer.ConnectedPlayers.Any())
        {
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Nome</th><th>Nível</th><th>Classe</th><th>Posição (X, Y, Z)</th></tr>");

            // Cria uma cópia para iterar com segurança
            var players = udpServer.ConnectedPlayers.Values.ToList();
            foreach (var player in players)
            {
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{player.CharacterName}</td>");
                sb.AppendLine($"<td>{player.Level}</td>");
                sb.AppendLine($"<td>{player.ClassID}</td>");
                sb.AppendLine($"<td>{player.Position.X:F2}, {player.Position.Y:F2}, {player.Position.Z:F2}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</table>");
        }
        else
        {
            sb.AppendLine("<p>Nenhum jogador online.</p>");
        }

        // --- Fim do HTML ---
        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }
}