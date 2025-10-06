// Models/Account.cs
using System.Collections.Generic;

public class Account
{
    public int Id { get; set; }
    public string Username { get; set; }
    public string HashedPassword { get; set; }
    public int PermissionLevel { get; set; } // <<< ADICIONE ESTA LINHA
    public List<Character> Characters { get; set; } = new List<Character>();
}