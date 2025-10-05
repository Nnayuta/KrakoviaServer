using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading; // Adicionado para CancellationToken
using System.Threading.Tasks;

public class TCPServer
{
    private readonly TcpListener _listener;
    private readonly IAccountDatabase _accountDb;
    private readonly ICharacterDatabase _characterDb;

    public TCPServer(int port, IAccountDatabase accountDatabase, ICharacterDatabase characterDatabase)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _accountDb = accountDatabase;
        _characterDb = characterDatabase; // Armazenamos a nova referência.
    }

    // =========================================================
    // ALTERAÇÃO 1: O método agora aceita o CancellationToken
    // =========================================================
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        Console.WriteLine($"Servidor [TCP-AUTH] iniciado na porta {_listener.LocalEndpoint}.");

        try
        {
            // O loop principal agora verifica o token de cancelamento.
            while (!cancellationToken.IsCancellationRequested)
            {
                // 2. Usamos o overload de AcceptTcpClientAsync que aceita o token.
                // A tarefa de esperar por um cliente será cancelada se o token for acionado.
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);

                // 3. Passamos o token para o handler do cliente também.
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException) // Pode ser TaskCanceledException ou OperationCanceledException
        {
            // 4. Esta exceção é esperada no desligamento. Apenas a capturamos para sair do loop.
            Console.WriteLine("[TCP-AUTH] Listener de conexões cancelado para shutdown.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TCP-ERROR] Erro fatal no listener: {ex.Message}");
        }
        finally
        {
            // 5. Garante que o listener seja parado, liberando a porta.
            _listener.Stop();
            Console.WriteLine("[TCP-AUTH] Listener parado.");
        }
    }

    // =========================================================
    // ALTERAÇÃO 2: O handler de cliente também aceita o token
    // =========================================================
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[TCP] Nova conexão de {client.Client.RemoteEndPoint}.");
        Account? loggedInAccount = null;
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);

        try
        {
            // O loop agora também verifica se o cancelamento geral do servidor foi solicitado.
            while (client.Connected && !cancellationToken.IsCancellationRequested)
            {
                // 6. Usamos o overload de ReadLineAsync que aceita o token.
                // Se o servidor desligar enquanto esperamos por uma mensagem, esta tarefa será cancelada.
                var jsonString = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(jsonString)) break; // Cliente desconectou

                var baseRequest = JsonConvert.DeserializeObject<BaseRequest>(jsonString);
                if (baseRequest == null) continue;

                switch (baseRequest.Command)
                {
                    case "register":
                        await HandleRegisterRequest(stream, jsonString);
                        client.Close();
                        break;
                    case "login":
                        loggedInAccount = await HandleLoginRequest(stream, jsonString);
                        if (loggedInAccount == null) client.Close();
                        break;
                    case "create_character":
                        if (loggedInAccount != null)
                        {
                            loggedInAccount = await _accountDb.GetAccountByUsernameAsync(loggedInAccount.Username);
                        }
                        await HandleCreateCharacterRequest(stream, jsonString, loggedInAccount);
                        break;
                    case "select_character":
                        if (loggedInAccount != null)
                        {
                            loggedInAccount = await _accountDb.GetAccountByUsernameAsync(loggedInAccount.Username);
                        }
                        await HandleSelectCharacterRequest(stream, jsonString, loggedInAccount);
                        client.Close();
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 7. Exceção esperada se o servidor desligar enquanto este handler estiver ativo.
            Console.WriteLine($"[TCP] Handler para {client.Client.RemoteEndPoint} cancelado para shutdown.");
        }
        catch (IOException) { /* Cliente desconectou (normal) */ }
        catch (Exception ex) { Console.WriteLine($"[TCP-ERROR] Erro no HandleClient: {ex}"); }
        finally
        {
            Console.WriteLine($"[TCP] Conexão com {client.Client.RemoteEndPoint} finalizada.");
            client.Close();
        }
    }

    #region Handlers de Requisição (Sem alterações necessárias aqui)

    private async Task HandleRegisterRequest(NetworkStream stream, string jsonString)
    {
        var regReq = JsonConvert.DeserializeObject<RegisterRequest>(jsonString);
        if (regReq == null) return;

        // <<< MUDANÇA 4: Usando a nova chamada assíncrona da interface.
        bool success = await _accountDb.RegisterAsync(regReq.Username, regReq.Password);

        var response = new BaseResponse
        {
            Command = "register_response",
            Success = success,
            Message = success ? "Cadastro bem-sucedido. Por favor, faça o login." : "Nome de usuário já existe."
        };
        await SendResponseAsync(stream, response);
    }

    private async Task<Account?> HandleLoginRequest(NetworkStream stream, string jsonString)
    {
        var loginReq = JsonConvert.DeserializeObject<LoginRequest>(jsonString);
        if (loginReq == null) return null;

        if (loginReq.ClientVersion != ServerConfig.GAME_VERSION)
        {
            var versionResponse = new BaseResponse { Command = "login_response", Success = false, Message = $"Versão do jogo incompatível \n Por favor, atualize o jogo." };
            await SendResponseAsync(stream, versionResponse);
            return null;
        }

        // <<< MUDANÇA 5: Usando a nova chamada assíncrona de autenticação.
        Account? account = await _accountDb.AuthenticateAsync(loginReq.Username, loginReq.Password);

        if (account != null)
        {
            var characters = account.Characters?.Select(c => c.ToSummary()).ToList() ?? new List<CharacterSummary>();
            var response = new CharacterListResponse { Command = "login_response", Success = true, Message = "Login bem-sucedido.", Characters = characters };
            await SendResponseAsync(stream, response);
            return account;
        }
        else
        {
            var response = new BaseResponse { Command = "login_response", Success = false, Message = "Usuário ou senha inválidos." };
            await SendResponseAsync(stream, response);
            return null;
        }
    }

    private async Task HandleCreateCharacterRequest(NetworkStream stream, string jsonString, Account? loggedInAccount)
    {
        if (loggedInAccount == null)
        {
            await SendResponseAsync(stream, new BaseResponse { Command = "create_character_response", Success = false, Message = "Usuário não está logado." });
            return;
        }

        var createReq = JsonConvert.DeserializeObject<CreateCharacterRequest>(jsonString);
        if (createReq == null) return;

        if (string.IsNullOrWhiteSpace(createReq.Name) || createReq.Name.Length < 3 || createReq.Name.Length > 16)
        {
            await SendResponseAsync(stream, new BaseResponse { Command = "create_character_response", Success = false, Message = "Nome inválido (3-16 caracteres)." });
            return;
        }

        var newCharacter = new Character
        {
            Name = createReq.Name,
            ClassID = createReq.ClassID ?? "WARRIOR",
            Appearance = createReq.Appearance ?? new CharacterAppearance()
        };

        bool success = await _accountDb.AddCharacterToAccountAsync(loggedInAccount.Username, newCharacter);

        // <<< MUDANÇA 7: Após a tentativa de criação, busca a lista atualizada de personagens da conta.
        var updatedAccount = await _accountDb.GetAccountByUsernameAsync(loggedInAccount.Username);
        var characters = updatedAccount?.Characters?.Select(c => c.ToSummary()).ToList() ?? new List<CharacterSummary>();

        var message = success ? "Personagem criado!" : "Nome de personagem já existe ou limite atingido.";
        var response = new CharacterListResponse { Command = "create_character_response", Success = success, Message = message, Characters = characters };
        await SendResponseAsync(stream, response);
    }

    private async Task HandleSelectCharacterRequest(NetworkStream stream, string jsonString, Account? loggedInAccount)
    {
        if (loggedInAccount == null)
        {
            await SendResponseAsync(stream, new BaseResponse { Command = "select_character_response", Success = false, Message = "Usuário não está logado." });
            return;
        }

        var selectReq = JsonConvert.DeserializeObject<SelectCharacterRequest>(jsonString);
        if (selectReq == null) return;

        var selectedCharacter = loggedInAccount.Characters.FirstOrDefault(c => c.Id == selectReq.CharacterId);
        if (selectedCharacter == null)
        {
            await SendResponseAsync(stream, new BaseResponse { Command = "select_character_response", Success = false, Message = "Personagem não encontrado." });
            return;
        }

        if (OnlineStatusManager.IsOnline(selectedCharacter.Id))
        {
            await SendResponseAsync(stream, new BaseResponse { Command = "select_character_response", Success = false, Message = "Este personagem já está conectado ao jogo." });
            return;
        }

        var authInfo = new AuthenticatedPlayerInfo
        {
            Username = loggedInAccount.Username,
            CharacterId = selectedCharacter.Id,
            CharacterName = selectedCharacter.Name,
            ClassID = selectedCharacter.ClassID,
            Level = selectedCharacter.Level,
            Appearance = selectedCharacter.Appearance
        };

        CharacterData characterData = await _characterDb.LoadOrCreateAsync(authInfo);


        // A partir daqui, o resto do método continua igual, usando o 'characterData' que foi carregado.
        var (_, _, _, knownAbilities) = CharacterStateGenerator.GenerateInitialState(selectedCharacter.ClassID, selectedCharacter.Level);
        string accessToken = AuthTokenManager.GenerateToken(loggedInAccount, selectedCharacter);

        var response = new SelectCharacterResponse
        {
            Command = "select_character_response",
            Success = true,
            AccessToken = accessToken,
            WorldServerIp = ServerConfig.SERVER_IP,
            WorldServerPort = ServerConfig.WORLD_SERVER_PORT,
            CharacterId = selectedCharacter.Id,
            ClassID = selectedCharacter.ClassID,
            Level = selectedCharacter.Level,
            KnownAbilityIDs = knownAbilities,
            Inventory = characterData.PlayerInventory.slots
                .Select(s => s == null ? null : new ItemStackSummary { InstanceID = s.InstanceID, ItemID = s.ItemID, Quantity = s.Quantity })
                .ToList(),
            Equipment = characterData.PlayerEquipment.equippedItems
                .Where(kvp => kvp.Value != null)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => new ItemStackSummary { InstanceID = kvp.Value!.InstanceID, ItemID = kvp.Value.ItemID, Quantity = kvp.Value.Quantity }
                ),
            ActionBar = characterData.PlayerActionBar
        };

        await SendResponseAsync(stream, response);
    }

    #endregion

    private async Task SendResponseAsync(NetworkStream stream, object responseObject)
    {
        var jsonResponse = JsonConvert.SerializeObject(responseObject);
        var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        await writer.WriteLineAsync(jsonResponse);
    }
}