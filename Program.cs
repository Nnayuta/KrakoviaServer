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
                    server.CommandManager.ProcessCommand(commandLine);
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

        // Cria os servidores primeiro para que possamos referenciá-los no handler de shutdown.
        Console.WriteLine("Conectando ao banco de dados MariaDB...");
        string connectionString = "Server=127.0.0.1;Database=krakovia;User=root;Password=;";
        IAccountDatabase accountDatabase = new MariaDBAccountDatabase(connectionString);
        ICharacterDatabase characterDatabase = new MariaDBCharacterDatabase(connectionString);

        var tcpServer = new TCPServer(ServerConfig.AUTH_SERVER_PORT, accountDatabase, characterDatabase);
        var udpServer = new UDPServer(ServerConfig.WORLD_SERVER_PORT, characterDatabase);

        // --- (LÓGICA DE SHUTDOWN ATUALIZADA) ---
        var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Impede que o console feche o programa abruptamente.

            // Verifica se o shutdown já foi iniciado para evitar chamadas duplas.
            if (!cts.IsCancellationRequested)
            {
                Console.WriteLine("\n[SERVER] Shutdown signal received. Closing servers...");

                // 1. Dispara o cancelamento para todos os loops de Task.Delay.
                cts.Cancel();

                // 2. Chama os métodos de parada explícita para desbloquear os sockets.
                // (Presumindo que você também adicionará um método Stop() ao TCPServer)
                tcpServer.Stop();
                udpServer.Stop();
            }
        };
        // --- FIM DA LÓGICA DE SHUTDOWN ---

        try
        {
            Console.WriteLine("[SERVER] Starting TCP and UDP listeners...");

            // Inicia todas as tarefas de longa duração.
            Task tcpTask = tcpServer.StartAsync(cts.Token);
            Task udpTask = udpServer.StartAsync(cts.Token);
            Task consoleTask = ConsoleCommandListener(udpServer, cts.Token);

            // Aguarda a conclusão de todas as tarefas principais.
            // Quando o cts.Cancel() for chamado, todas elas devem terminar graciosamente.
            await Task.WhenAll(tcpTask, udpTask, consoleTask);
        }
        catch (OperationCanceledException)
        {
            // Esta exceção é normal e esperada quando o token é cancelado.
            // Apenas garante que o programa não feche com um erro não tratado.
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