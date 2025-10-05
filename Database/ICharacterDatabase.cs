using System.Threading.Tasks;

public interface ICharacterDatabase
{
    /// <summary>
    /// Carrega os dados de um personagem ou os cria se for a primeira vez.
    /// </summary>
    /// <param name="authInfo">Informações de autenticação que identificam o personagem.</param>
    /// <returns>Os dados completos do personagem.</returns>
    Task<CharacterData> LoadOrCreateAsync(AuthenticatedPlayerInfo authInfo);

    /// <summary>
    /// Salva os dados de um personagem de forma assíncrona.
    /// </summary>
    /// <param name="dataToSave">O objeto com os dados do personagem a serem salvos.</param>
    Task SaveAsync(CharacterData dataToSave);
}