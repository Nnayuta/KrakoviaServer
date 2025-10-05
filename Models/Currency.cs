// Models/Currency.cs

/// <summary>
/// Estrutura para representar a moeda dividida em Ouro, Prata e Bronze.
/// Usada principalmente para exibição e comunicação com o cliente.
/// </summary>
public struct Currency
{
    public readonly long Gold;
    public readonly long Silver;
    public readonly long Bronze;

    public const int BRONZE_PER_SILVER = 100;
    public const int SILVER_PER_GOLD = 100;
    public const int BRONZE_PER_GOLD = BRONZE_PER_SILVER * SILVER_PER_GOLD; // 10,000

    public Currency(long totalBronze)
    {
        Gold = totalBronze / BRONZE_PER_GOLD;
        long remainingBronze = totalBronze % BRONZE_PER_GOLD;

        Silver = remainingBronze / BRONZE_PER_SILVER;
        Bronze = remainingBronze % BRONZE_PER_SILVER;
    }

    public override string ToString()
    {
        return $"{Gold}g {Silver}s {Bronze}b";
    }
}