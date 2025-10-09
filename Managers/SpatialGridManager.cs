// Managers/SpatialGridManager.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

public class SpatialGridManager
{
    private readonly ConcurrentDictionary<Vector2Int, HashSet<IWorldEntity>> _grid = new();
    private readonly ConcurrentDictionary<string, Vector2Int> _entityPositions = new();
    private readonly int _cellSize;
    private readonly object _gridLock = new object();

    public SpatialGridManager(int cellSize = 50) // Células de 50x50 metros
    {
        _cellSize = cellSize;
    }

    private Vector2Int GetCellCoordinates(Vector3 position)
    {
        int x = (int)Math.Floor(position.X / _cellSize);
        int z = (int)Math.Floor(position.Z / _cellSize);
        return new Vector2Int(x, z);
    }

    /// <summary>
    /// Adiciona ou atualiza a posição de uma entidade na grade.
    /// </summary>
    public void UpdateEntity(IWorldEntity entity)
    {
        Vector2Int newCellPos = GetCellCoordinates(entity.Position);

        lock (_gridLock)
        {
            // Se a entidade já existe na grade, verifica se ela mudou de célula
            if (_entityPositions.TryGetValue(entity.Id, out Vector2Int oldCellPos))
            {
                if (oldCellPos == newCellPos) return; // Continua na mesma célula, não faz nada

                // Remove da célula antiga
                if (_grid.TryGetValue(oldCellPos, out var oldCell))
                {
                    oldCell.Remove(entity);
                }
            }

            // Adiciona à nova célula
            var newCell = _grid.GetOrAdd(newCellPos, _ => new HashSet<IWorldEntity>());
            newCell.Add(entity);
            _entityPositions[entity.Id] = newCellPos;
        }
    }

    /// <summary>
    /// Remove uma entidade da grade (ex: quando desconecta ou morre permanentemente).
    /// </summary>
    public void RemoveEntity(IWorldEntity entity)
    {
        lock (_gridLock)
        {
            if (_entityPositions.TryRemove(entity.Id, out Vector2Int cellPos))
            {
                if (_grid.TryGetValue(cellPos, out var cell))
                {
                    cell.Remove(entity);
                }
            }
        }
    }

    /// <summary>
    /// Encontra todas as entidades em um raio ao redor de uma posição,
    /// buscando apenas nas células relevantes.
    /// </summary>
    public List<IWorldEntity> GetEntitiesInRadius(Vector3 center, float radius)
    {
        var results = new List<IWorldEntity>();
        Vector2Int centerCell = GetCellCoordinates(center);

        // Calcula quantas células precisamos checar em cada direção
        int searchRadius = (int)Math.Ceiling(radius / _cellSize);

        lock (_gridLock)
        {
            for (int x = centerCell.X - searchRadius; x <= centerCell.X + searchRadius; x++)
            {
                for (int z = centerCell.Y - searchRadius; z <= centerCell.Y + searchRadius; z++)
                {
                    if (_grid.TryGetValue(new Vector2Int(x, z), out var cell))
                    {
                        results.AddRange(cell);
                    }
                }
            }
        }

        // Filtra os resultados pela distância exata (já que as células externas podem conter entidades fora do raio)
        float radiusSqr = radius * radius;
        return results.Where(e => Vector3.DistanceSquared(e.Position, center) < radiusSqr).ToList();
    }

    // Pequena struct auxiliar para as coordenadas da grade
    public readonly record struct Vector2Int(int X, int Y);
}