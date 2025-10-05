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
                // Console.ReadLineAsync é a chave! Ele espera por input de forma assíncrona.
                string? commandLine = await Console.In.ReadLineAsync(token);
                if (!string.IsNullOrEmpty(commandLine))
                {
                    server.CommandManager.ProcessCommand(commandLine);
                }
            }
            catch (OperationCanceledException)
            {
                // Esperado no desligamento
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Console-ERRO] Erro ao ler comando: {ex.Message}");
            }
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

        // 1. Cria o "interruptor de emergência"
        var cts = new CancellationTokenSource();

        // 2. Configura o handler para Ctrl+C
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[SERVER] Shutdown signal received. Closing servers...");
            cts.Cancel(); // Aciona o interruptor
        };


        // <<< MUDANÇA 1: Comente ou delete as linhas do banco de dados em memória. >>>
        // Console.WriteLine("Usando banco de dados em memória...");
        // IAccountDatabase accountDatabase = new InMemoryAccountDatabase();
        // ICharacterDatabase characterDatabase = new InMemoryCharacterDatabase();


        // --- Opção 2: Usar o banco de dados MariaDB (para produção) ---
        // Descomente esta seção quando estiver pronto.
        Console.WriteLine("Conectando ao banco de dados MariaDB...");
         string connectionString = "Server=127.0.0.1;Database=krakovia;User=root;Password=;";
        IAccountDatabase accountDatabase = new MariaDBAccountDatabase(connectionString);
        ICharacterDatabase characterDatabase = new MariaDBCharacterDatabase(connectionString);

        var tcpServer = new TCPServer(ServerConfig.AUTH_SERVER_PORT, accountDatabase, characterDatabase);
        var udpServer = new UDPServer(ServerConfig.WORLD_SERVER_PORT, characterDatabase);

        try
        {
            Console.WriteLine("[SERVER] Starting TCP and UDP listeners...");
            // 3. Passa o token de cancelamento para os servidores
            Task tcpTask = tcpServer.StartAsync(cts.Token);
            Task udpTask = udpServer.StartAsync(cts.Token);

            Task consoleTask = ConsoleCommandListener(udpServer, cts.Token);

            await Task.WhenAll(tcpTask, udpTask);
        }
        catch (TaskCanceledException) { /* Exceção esperada no desligamento */ }
        catch (Exception ex) { Console.WriteLine($"[SERVER-FATAL] Unhandled exception: {ex}"); }
        finally { Console.WriteLine("[SERVER] All tasks cancelled. Server is shut down."); }
    }
}