// Data/IAccountDatabase.cs
using System.Threading.Tasks;

public interface IAccountDatabase
{
    /// <summary>
    /// Tenta registrar uma nova conta de forma assíncrona.
    /// </summary>
    /// <param name="username">O nome de usuário.</param>
    /// <param name="password">A senha em texto puro.</param>
    /// <returns>Retorna true se o registro for bem-sucedido, false se o usuário já existir.</returns>
    Task<bool> RegisterAsync(string username, string password);

    /// <summary>
    /// Tenta autenticar um usuário de forma assíncrona.
    /// </summary>
    /// <param name="username">O nome de usuário.</param>
    /// <param name="password">A senha em texto puro.</param>
    /// <returns>Retorna o objeto Account se o login for bem-sucedido, caso contrário, null.</returns>
    Task<Account?> AuthenticateAsync(string username, string password);

    /// <summary>
    /// Adiciona um novo personagem a uma conta existente de forma assíncrona.
    /// </summary>
    /// <param name="username">O nome de usuário da conta.</param>
    /// <param name="newCharacter">O novo personagem a ser adicionado.</param>
    /// <returns>Retorna true se o personagem for adicionado com sucesso.</returns>
    Task<bool> AddCharacterToAccountAsync(string username, Character newCharacter);

    /// <summary>
    /// Busca uma conta pelo nome de usuário.
    /// </summary>
    /// <param name="username">O nome de usuário a ser buscado.</param>
    /// <returns>O objeto Account ou null se não for encontrado.</returns>
    Task<Account?> GetAccountByUsernameAsync(string username);
}