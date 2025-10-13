// Servidor/Data/GatherableInstance.cs
using System;
using System.Globalization;
using System.Numerics;

public class GatherableInstance : IWorldEntity
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");
    public GatherableData BaseData { get; }
    public Vector3 Position { get; private set; }
    public Quaternion Rotation { get; private set; }
    public bool IsDepleted { get; set; } = false;
    public DateTime RespawnTime { get; set; } = DateTime.MinValue;

    public string Id => InstanceId;
    public bool IsStationary => true;

    public GatherableInstance(GatherableData baseData, Vector3 position, Quaternion rotation)
    {
        BaseData = baseData;
        Position = position;
        Rotation = rotation;
    }

    public void SetNewPositionAndRotation(Vector3 newPosition, Quaternion newRotation)
    {
        Position = newPosition;
        Rotation = newRotation;
    }

    public string GetSpawnMessage()
    {
        var eulerAngles = ToEulerAngles(Rotation); // Precisaremos da função auxiliar aqui
        return $"SPAWN_GATHERABLE|{InstanceId}|{BaseData.ID}|{Position.X.ToString(CultureInfo.InvariantCulture)},{Position.Y.ToString(CultureInfo.InvariantCulture)},{Position.Z.ToString(CultureInfo.InvariantCulture)}|{eulerAngles.X.ToString(CultureInfo.InvariantCulture)},{eulerAngles.Y.ToString(CultureInfo.InvariantCulture)},{eulerAngles.Z.ToString(CultureInfo.InvariantCulture)}";
    }

    public Vector3 ToEulerAngles(Quaternion q)
    {
        Vector3 angles = new();

        // Roll (eixo x)
        double sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        double cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        angles.X = (float)Math.Atan2(sinr_cosp, cosr_cosp);

        // Pitch (eixo y)
        double sinp = 2 * (q.W * q.Y - q.Z * q.X);
        if (Math.Abs(sinp) >= 1)
            angles.Y = (float)Math.CopySign(Math.PI / 2, sinp); // Use 90 graus se estiver olhando para cima/baixo
        else
            angles.Y = (float)Math.Asin(sinp);

        // Yaw (eixo z)
        double siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        double cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        angles.Z = (float)Math.Atan2(siny_cosp, cosy_cosp);

        // Converte de radianos para graus para ser compatível com o Unity
        angles.X *= (float)(180.0 / Math.PI);
        angles.Y *= (float)(180.0 / Math.PI);
        angles.Z *= (float)(180.0 / Math.PI);

        return angles;
    }

}