using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class TCPServer
{
    private readonly TcpListener _listener;
    private readonly IAccountDatabase _accountDb;
    private readonly ICharacterDatabase _characterDb;
    private readonly ConcurrentQueue<TcpClient> _pendingClients = new ConcurrentQueue<TcpClient>();
    private const int MAX_LOGIN_WORKERS = 4;

    public TCPServer(int port, IAccountDatabase accountDatabase, ICharacterDatabase characterDatabase)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _accountDb = accountDatabase;
        _characterDb = characterDatabase;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        Console.WriteLine($"Servidor [TCP-AUTH] iniciado na porta {_listener.LocalEndpoint}.");

        List<Task> workerTasks = new List<Task>();
        for (int i = 0; i < MAX_LOGIN_WORKERS; i++)
        {
            workerTasks.Add(LoginWorkerAsync(cancellationToken));
        }
        Console.WriteLine($"[TCP-AUTH] {MAX_LOGIN_WORKERS} workers de login iniciados.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _pendingClients.Enqueue(client);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[TCP-AUTH] Listener de conexões cancelado para shutdown.");
        }
        finally
        {
            _listener.Stop();
            Console.WriteLine("[TCP-AUTH] Listener parado.");
            await Task.WhenAll(workerTasks);
        }
    }

    public void Stop()
    {
        _listener.Stop();
    }

    private async Task LoginWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_pendingClients.TryDequeue(out TcpClient client))
            {
                await HandleClientAsync(client, cancellationToken);
            }
            else
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[TCP] Worker atendendo conexão de {client.Client.RemoteEndPoint}.");
        Account? loggedInAccount = null;
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);

        try
        {
            while (client.Connected && !cancellationToken.IsCancellationRequested)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var jsonString = await reader.ReadLineAsync(linkedCts.Token);
                if (string.IsNullOrEmpty(jsonString)) break;

                var baseRequest = JsonConvert.DeserializeObject<BaseRequest>(jsonString);
                if (baseRequest == null) continue;

                bool closeAfterRequest = false;
                switch (baseRequest.Command)
                {
                    case "register":
                        await HandleRegisterRequest(stream, jsonString);
                        break;
                    case "login":
                        loggedInAccount = await HandleLoginRequest(stream, jsonString);
                        break;
                    case "create_character":
                        await HandleCreateCharacterRequest(stream, jsonString, loggedInAccount);
                        break;
                    case "select_character":
                        await HandleSelectCharacterRequest(stream, jsonString, loggedInAccount);
                        closeAfterRequest = true;
                        break;
                    case "ping":
                        await SendResponseAsync(stream, new BaseResponse { Command = "pong" });
                        continue;
                }

                if (closeAfterRequest)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
                Console.WriteLine($"[TCP] Cliente {client.Client.RemoteEndPoint} desconectado por inatividade.");
        }
        catch (IOException)
        {
            // desconexão normal
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TCP-ERROR] Erro no HandleClient: {ex}");
        }
        finally
        {
            Console.WriteLine($"[TCP] Conexão com {client.Client.RemoteEndPoint} finalizada.");
            client.Close();
        }
    }

    #region Handlers

    private async Task HandleRegisterRequest(NetworkStream stream, string jsonString)
    {
        var regReq = JsonConvert.DeserializeObject<RegisterRequest>(jsonString);
        if (regReq == null) return;

        bool success = await _accountDb.RegisterAsync(regReq.Username, regReq.Password);
        var response = new BaseResponse
        {
            Command = "register_response",
            Success = success,
            Message = success ? "Cadastro bem-sucedido." : "Nome de usuário já existe."
        };
        await SendResponseAsync(stream, response);
    }

    private async Task<Account?> HandleLoginRequest(NetworkStream stream, string jsonString)
    {
        var loginReq = JsonConvert.DeserializeObject<LoginRequest>(jsonString);
        if (loginReq == null) return null;

        if (loginReq.ClientVersion != ServerConfig.GAME_VERSION)
        {
            var versionResponse = new BaseResponse
            {
                Command = "login_response",
                Success = false,
                Message = "Versão do jogo incompatível."
            };
            await SendResponseAsync(stream, versionResponse);
            return null;
        }

        Account? account = await _accountDb.AuthenticateAsync(loginReq.Username, loginReq.Password);
        if (account != null)
        {
            var characters = account.Characters?.Select(c => c.ToSummary()).ToList() ?? new List<CharacterSummary>();
            var response = new CharacterListResponse
            {
                Command = "login_response",
                Success = true,
                Message = "Login bem-sucedido.",
                Characters = characters
            };
            await SendResponseAsync(stream, response);
            return account;
        }
        else
        {
            var response = new BaseResponse
            {
                Command = "login_response",
                Success = false,
                Message = "Usuário ou senha inválidos."
            };
            await SendResponseAsync(stream, response);
            return null;
        }
    }

    private async Task HandleCreateCharacterRequest(NetworkStream stream, string jsonString, Account? loggedInAccount)
    {
        if (loggedInAccount == null)
        {
            await SendResponseAsync(stream, new BaseResponse
            {
                Command = "create_character_response",
                Success = false,
                Message = "Usuário não está logado."
            });
            return;
        }

        var createReq = JsonConvert.DeserializeObject<CreateCharacterRequest>(jsonString);
        if (createReq == null || string.IsNullOrWhiteSpace(createReq.Name) || createReq.Name.Length < 3 || createReq.Name.Length > 16)
        {
            await SendResponseAsync(stream, new BaseResponse
            {
                Command = "create_character_response",
                Success = false,
                Message = "Nome inválido (3-16 caracteres)."
            });
            return;
        }

        var newCharacter = new Character
        {
            Name = createReq.Name,
            ClassID = createReq.ClassID ?? "WARRIOR",
            Appearance = createReq.Appearance ?? new CharacterAppearance()
        };

        bool success = await _accountDb.AddCharacterToAccountAsync(loggedInAccount.Username, newCharacter);

        var updatedAccount = await _accountDb.GetAccountByUsernameAsync(loggedInAccount.Username);
        loggedInAccount.Characters = updatedAccount?.Characters ?? loggedInAccount.Characters;
        var characters = loggedInAccount.Characters?.Select(c => c.ToSummary()).ToList() ?? new List<CharacterSummary>();

        var message = success ? "Personagem criado!" : "Nome de personagem já existe ou limite atingido.";
        var response = new CharacterListResponse
        {
            Command = "create_character_response",
            Success = success,
            Message = message,
            Characters = characters
        };
        await SendResponseAsync(stream, response);
    }

    private async Task HandleSelectCharacterRequest(NetworkStream stream, string jsonString, Account? loggedInAccount)
    {
        if (loggedInAccount == null)
        {
            await SendResponseAsync(stream, new BaseResponse
            {
                Command = "select_character_response",
                Success = false,
                Message = "Usuário não está logado."
            });
            return;
        }

        var selectReq = JsonConvert.DeserializeObject<SelectCharacterRequest>(jsonString);
        if (selectReq == null) return;

        var selectedCharacter = loggedInAccount.Characters.FirstOrDefault(c => c.Id == selectReq.CharacterId);
        if (selectedCharacter == null)
        {
            await SendResponseAsync(stream, new BaseResponse
            {
                Command = "select_character_response",
                Success = false,
                Message = "Personagem não encontrado."
            });
            return;
        }

        if (OnlineStatusManager.IsOnline(selectedCharacter.Id))
        {
            await SendResponseAsync(stream, new BaseResponse
            {
                Command = "select_character_response",
                Success = false,
                Message = "Este personagem já está conectado ao jogo."
            });
            return;
        }

        var authInfo = new AuthenticatedPlayerInfo
        {
            Username = loggedInAccount.Username,
            CharacterId = selectedCharacter.Id,
            CharacterName = selectedCharacter.Name,
            ClassID = selectedCharacter.ClassID,
            Level = selectedCharacter.Level,
            Appearance = selectedCharacter.Appearance,
            PermissionLevel = loggedInAccount.PermissionLevel
        };

        CharacterData characterData = await _characterDb.LoadOrCreateAsync(authInfo);

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
            Inventory = characterData.PlayerInventory.slots.Select(s => s == null ? null : new ItemStackSummary
            {
                InstanceID = s.InstanceID,
                ItemID = s.ItemID,
                Quantity = s.Quantity
            }).ToList(),
            Equipment = characterData.PlayerEquipment.equippedItems
                .Where(kvp => kvp.Value != null)
                .ToDictionary(kvp => kvp.Key, kvp => new ItemStackSummary
                {
                    InstanceID = kvp.Value!.InstanceID,
                    ItemID = kvp.Value.ItemID,
                    Quantity = kvp.Value.Quantity
                }),
            ActionBar = characterData.PlayerActionBar,
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
