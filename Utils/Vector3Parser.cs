// Servidor/Utils/Vector3Parser.cs (ou onde quer que esteja)
using System.Globalization;
using System.Numerics;

public static class Vector3Parser
{
    public static Vector3 Parse(string s)
    {
        if (string.IsNullOrEmpty(s)) return Vector3.Zero;

        string[] parts = s.Split(',');
        if (parts.Length != 3)
        {
            // Adicionar um log de erro aqui é útil para depuração
            Console.WriteLine($"[AVISO] Formato de Vector3 inválido ao tentar parsear: '{s}'");
            return Vector3.Zero;
        }

        try
        {
            float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
            float y = float.Parse(parts[1], CultureInfo.InvariantCulture);
            float z = float.Parse(parts[2], CultureInfo.InvariantCulture);
            return new Vector3(x, y, z);
        }
        catch (FormatException e)
        {
            Console.WriteLine($"[ERRO] Falha ao parsear Vector3 da string '{s}': {e.Message}");
            return Vector3.Zero;
        }
    }
}