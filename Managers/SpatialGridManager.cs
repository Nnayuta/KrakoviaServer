using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

public class SpatialGridManager
{
    private readonly Dictionary<Vector2Int, HashSet<IWorldEntity>> _grid = new();
    private readonly Dictionary<string, Vector2Int> _entityPositions = new();
    private readonly int _cellSize;
    private readonly object _gridLock = new object();

    public SpatialGridManager(int cellSize = 50)
    {
        _cellSize = cellSize;
    }

    private Vector2Int GetCellCoordinates(Vector3 position)
    {
        int x = (int)Math.Floor(position.X / _cellSize);
        int z = (int)Math.Floor(position.Z / _cellSize);
        return new Vector2Int(x, z);
    }

    public void UpdateEntity(IWorldEntity entity)
    {
        if (entity == null) return;
        Vector2Int newCellPos = GetCellCoordinates(entity.Position);

        lock (_gridLock)
        {
            if (_entityPositions.TryGetValue(entity.Id, out Vector2Int oldCellPos))
            {
                if (oldCellPos == newCellPos) return; // Se não mudou de célula, não faz nada.

                // Se mudou de célula, remove da célula antiga
                if (_grid.TryGetValue(oldCellPos, out var oldCell))
                {
                    // Console.WriteLine($"[GRID-UPDATE-REMOVE] Removendo {entity.Id} | da célula antiga {oldCellPos} para mover para {newCellPos}.");
                    oldCell.Remove(entity);
                    if (oldCell.Count == 0) _grid.Remove(oldCellPos);
                }
            }

            // Adiciona à nova célula
            var newCell = _grid.TryGetValue(newCellPos, out var cell) ? cell : new HashSet<IWorldEntity>();
            newCell.Add(entity);
            _grid[newCellPos] = newCell;

            _entityPositions[entity.Id] = newCellPos;
        }
    }

    public void RemoveEntity(IWorldEntity entity)
    {
        if (entity == null) return;

        // Console.WriteLine($"[GRID-REMOVE] Tentativa de remover a entidade {entity.Id} da grade.");
        // var stackTrace = new System.Diagnostics.StackTrace();
        // Console.WriteLine(stackTrace.ToString()); // Isso nos dirá QUEM chamou a função

        lock (_gridLock)
        {
            if (_entityPositions.Remove(entity.Id, out Vector2Int cellPos))
            {
                if (_grid.TryGetValue(cellPos, out var cell))
                {
                    cell.Remove(entity);
                    if (cell.Count == 0) _grid.Remove(cellPos);
                }
            }
        }
    }

    public List<IWorldEntity> GetEntitiesInRadius(Vector3 center, float radius)
    {
        var resultSet = new HashSet<IWorldEntity>(); // Usa HashSet para garantir unicidade
        Vector2Int centerCell = GetCellCoordinates(center);
        int searchRadius = (int)Math.Ceiling(radius / _cellSize);

        // Não precisa de lock aqui se a iteração sobre o dicionário for segura,
        // mas vamos manter por segurança extra.
        lock (_gridLock)
        {
            for (int x = centerCell.X - searchRadius; x <= centerCell.X + searchRadius; x++)
            {
                for (int z = centerCell.Y - searchRadius; z <= centerCell.Y + searchRadius; z++)
                {
                    if (_grid.TryGetValue(new Vector2Int(x, z), out var cell))
                    {
                        // Adiciona todos os itens da célula. Duplicatas são ignoradas pelo HashSet.
                        resultSet.UnionWith(cell);
                    }
                }
            }
        }

        float radiusSqr = radius * radius;
        return resultSet.Where(e => Vector3.DistanceSquared(e.Position, center) < radiusSqr).ToList();
    }

    public readonly record struct Vector2Int(int X, int Y);
}