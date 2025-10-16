// Helpers/Vector3Helper.cs
using System.Numerics;

public static class Vector3Helper
{
    /// <summary>
    /// Calcula a distância horizontal (no plano XZ) entre dois vetores.
    /// </summary>
    public static float Distance2D(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)System.Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Calcula o quadrado da distância horizontal (no plano XZ) entre dois vetores.
    /// É mais performático que Distance2D para comparações.
    /// </summary>
    public static float Distance2DSquared(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }
}