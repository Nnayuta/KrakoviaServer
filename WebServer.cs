// Servidor/WebServer.cs
using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class WebServer
{
    private readonly HttpListener _listener = new HttpListener();

    public WebServer(string url)
    {
        // Exemplo de URL: "http://localhost:8080/"
        // Para ouvir em todas as interfaces de rede, use: "http://+:8080/"
        _listener.Prefixes.Add(url);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        Console.WriteLine($"Servidor [WEB-STATUS] iniciado. Ouvindo em {_listener.Prefixes.First()}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Espera por uma requisição HTTP
                var context = await _listener.GetContextAsync();

                // Processa a requisição sem bloquear o loop principal
                _ = Task.Run(() => ProcessRequestAsync(context), cancellationToken);
            }
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 995)
        {
            // Erro 995 é "A operação de I/O foi anulada por uma saída de thread ou por uma solicitação de aplicativo."
            // É normal acontecer no shutdown.
            Console.WriteLine("[WEB-STATUS] Listener de HTTP foi parado.");
        }
        finally
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        try
        {
            // Gera o conteúdo HTML usando a mesma classe de antes
            string htmlContent = ServerStatusHtmlGenerator.Generate();
            byte[] buffer = Encoding.UTF8.GetBytes(htmlContent);

            // Configura a resposta
            var response = context.Response;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = buffer.Length;

            // Envia a resposta
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WEB-ERROR] Erro ao processar requisição HTTP: {ex.Message}");
            // Fecha a conexão em caso de erro
            context.Response.Abort();
        }
    }

    public void Stop()
    {
        if (_listener.IsListening)
        {
            _listener.Stop();
        }
    }
}