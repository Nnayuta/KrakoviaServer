using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static async Task ConsoleCommandListener(UDPServer server, CancellationToken token)
    {
        Console.WriteLine("[Console] Listener de comandos iniciado. Digite 'help' para ajuda.");
        while (!token.IsCancellationRequested)
        {
            try
            {
                string? commandLine = await Console.In.ReadLineAsync(token);
                if (!string.IsNullOrEmpty(commandLine))
                {
                    // ALTERAÇÃO AQUI: Passamos 'null' como o remetente (Player),
                    // pois o comando vem do console e não de um jogador.
                    server.CommandManager.ProcessCommand(null, commandLine);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.WriteLine($"[Console-ERRO] Erro ao ler comando: {ex.Message}"); }
        }
        Console.WriteLine("[Console] Listener de comandos encerrado.");
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine(
$@"
 ____  __.__________    _____   ____  __.____________   ____.___   _____
|    |/ _|\______   \  /  _  \ |    |/ _|\_____  \   \ /   /|   | /  _  \
|      <   |       _/ /  /_\  \|      <   /   |   \   Y   / |   |/  /_\  \
|    |  \  |    |   \/    |    \    |  \ /    |    \     /  |   /    |    \
|____|__ \ |____|_  /\____|__  /____|__ \\_______  /\___/   |___\____|__  /
        \/        \/         \/        \/        \/                     \/

        Server is starting...
        Version: {ServerConfig.GAME_VERSION}
");

        Console.WriteLine("Conectando ao banco de dados MariaDB...");
        // string connectionString = "Server=127.0.0.1;Database=krakovia;User=kakovia;Password=s3@>C6U3K5£:;";
        string connectionString = "Server=127.0.0.1;Database=krakovia;User=root;Password=;";

        IAccountDatabase accountDatabase = new MariaDBAccountDatabase(connectionString);
        ICharacterDatabase characterDatabase = new MariaDBCharacterDatabase(connectionString);

        var tcpServer = new TCPServer(ServerConfig.AUTH_SERVER_PORT, accountDatabase, characterDatabase);
        var udpServer = new UDPServer(ServerConfig.WORLD_SERVER_PORT, characterDatabase);
        var webServer = new WebServer("http://+:8080/");

        var cts = new CancellationTokenSource();

        Console.CancelKeyPress += async (sender, e) =>
        {
            e.Cancel = true;

            if (!cts.IsCancellationRequested)
            {
                Console.WriteLine("\n[SERVER] Shutdown signal received. Saving all players...");

                // 1. SALVA TODOS OS JOGADORES PRIMEIRO
                await udpServer.SaveAllPlayersAsync();

                Console.WriteLine("[SERVER] Closing servers...");

                cts.Cancel();

                tcpServer.Stop();
                udpServer.Stop();
                webServer.Stop();
            }
        };

        try
        {
            Console.WriteLine("[SERVER] Starting TCP, UDP, and Web listeners...");

            Task tcpTask = tcpServer.StartAsync(cts.Token);
            Task udpTask = udpServer.StartAsync(cts.Token);
            Task webTask = webServer.StartAsync(cts.Token);
            Task consoleTask = ConsoleCommandListener(udpServer, cts.Token);

            await Task.WhenAll(tcpTask, udpTask, webTask, consoleTask);
        }
        catch (OperationCanceledException)
        {
            // Normal no shutdown.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SERVER-FATAL] Unhandled exception in Main: {ex}");
        }
        finally
        {
            Console.WriteLine("[SERVER] All tasks completed. Server is shut down.");
        }
    }
}