// Models/Account.cs
using System.Collections.Generic;

public class Account
{
    public int Id { get; set; }
    public string Username { get; set; }
    public string HashedPassword { get; set; }
    public List<Character> Characters { get; set; } = new List<Character>();
}