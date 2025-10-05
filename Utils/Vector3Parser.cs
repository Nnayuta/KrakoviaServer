// Utils/Vector3Parser.cs
using System.Globalization;
using System.Numerics;

public static class Vector3Parser
{
    public static Vector3 Parse(string pos)
    {
        try
        {
            string[] parts = pos.Split(',');
            return new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture));
        }
        catch
        {
            // Retorna Zero se houver erro de formatação para evitar crashes.
            return Vector3.Zero;
        }
    }
}