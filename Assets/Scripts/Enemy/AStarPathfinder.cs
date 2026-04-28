using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DungeonData 그리드를 대상으로 하는 재사용형 A* 탐색기입니다.
/// 탐색마다 배열을 새로 만들지 않고, 맵 크기가 바뀔 때만 내부 버퍼를 재할당해서 GC Spike를 줄입니다.
/// </summary>
public sealed class AStarPathfinder
{
    private const int MOVE_COST = 10;

    private readonly List<int> _open = new List<int>(256);

    private int[] _gCost;
    private int[] _hCost;
    private int[] _parent;
    private int[] _openedStamp;
    private int[] _closedStamp;

    private int _width;
    private int _height;
    private int _searchId;

    /// <summary>
    /// start에서 goal까지의 최단 경로를 result에 기록합니다.
    /// result는 호출자가 소유하며, 이 메서드는 Clear 후 재사용합니다.
    /// </summary>
    public bool FindPath(DungeonData data, Vector2Int start, Vector2Int goal, List<Vector2Int> result)
    {
        result.Clear();

        if (data == null) return false;
        EnsureCapacity(data.MapWidth, data.MapHeight);

        if (!IsWalkable(data, start.x, start.y) || !IsWalkable(data, goal.x, goal.y))
            return false;

        if (start == goal)
        {
            result.Add(start);
            return true;
        }

        BeginSearch();

        int startIndex = ToIndex(start.x, start.y);
        int goalIndex = ToIndex(goal.x, goal.y);

        _gCost[startIndex] = 0;
        _hCost[startIndex] = Heuristic(start.x, start.y, goal.x, goal.y);
        _parent[startIndex] = -1;
        _openedStamp[startIndex] = _searchId;
        _open.Add(startIndex);

        while (_open.Count > 0)
        {
            int bestOpenSlot = FindBestOpenSlot();
            int current = _open[bestOpenSlot];
            RemoveOpenAt(bestOpenSlot);

            _closedStamp[current] = _searchId;

            if (current == goalIndex)
            {
                BuildResultPath(goalIndex, result);
                return true;
            }

            int col = current % _width;
            int row = current / _width;

            // 4방향만 사용합니다. 대각선 이동을 막아 벽 모서리를 뚫고 지나가는 일을 방지합니다.
            TryAddNeighbor(data, current, col + 1, row, goal);
            TryAddNeighbor(data, current, col - 1, row, goal);
            TryAddNeighbor(data, current, col, row + 1, goal);
            TryAddNeighbor(data, current, col, row - 1, goal);
        }

        return false;
    }

    private void EnsureCapacity(int width, int height)
    {
        int count = width * height;
        if (_gCost != null && _gCost.Length == count && _width == width && _height == height)
            return;

        _width = width;
        _height = height;
        _gCost = new int[count];
        _hCost = new int[count];
        _parent = new int[count];
        _openedStamp = new int[count];
        _closedStamp = new int[count];
        _open.Capacity = Mathf.Max(_open.Capacity, Mathf.Min(count, 512));
    }

    private void BeginSearch()
    {
        _searchId++;

        // int overflow는 매우 드문 상황이지만, 장시간 테스트 중에도 stamp 비교가 깨지지 않도록 초기화합니다.
        if (_searchId == int.MaxValue)
        {
            System.Array.Clear(_openedStamp, 0, _openedStamp.Length);
            System.Array.Clear(_closedStamp, 0, _closedStamp.Length);
            _searchId = 1;
        }

        _open.Clear();
    }

    private void TryAddNeighbor(DungeonData data, int current, int col, int row, Vector2Int goal)
    {
        if (!IsWalkable(data, col, row)) return;

        int next = ToIndex(col, row);
        if (_closedStamp[next] == _searchId) return;

        int nextG = _gCost[current] + MOVE_COST;
        bool wasOpened = _openedStamp[next] == _searchId;

        if (!wasOpened || nextG < _gCost[next])
        {
            _gCost[next] = nextG;
            _hCost[next] = Heuristic(col, row, goal.x, goal.y);
            _parent[next] = current;

            if (!wasOpened)
            {
                _openedStamp[next] = _searchId;
                _open.Add(next);
            }
        }
    }

    private int FindBestOpenSlot()
    {
        int bestSlot = 0;
        int bestIndex = _open[0];
        int bestF = _gCost[bestIndex] + _hCost[bestIndex];
        int bestH = _hCost[bestIndex];

        // 맵 크기가 작고 갱신 주기가 낮기 때문에, 힙보다 단순 선형 탐색이 할당 없이 충분히 빠릅니다.
        for (int i = 1; i < _open.Count; i++)
        {
            int index = _open[i];
            int f = _gCost[index] + _hCost[index];
            int h = _hCost[index];

            if (f < bestF || (f == bestF && h < bestH))
            {
                bestSlot = i;
                bestF = f;
                bestH = h;
            }
        }

        return bestSlot;
    }

    private void RemoveOpenAt(int slot)
    {
        int last = _open.Count - 1;
        _open[slot] = _open[last];
        _open.RemoveAt(last);
    }

    private void BuildResultPath(int goalIndex, List<Vector2Int> result)
    {
        int current = goalIndex;
        while (current >= 0)
        {
            result.Add(new Vector2Int(current % _width, current / _width));
            current = _parent[current];
        }

        // 부모를 따라가면 goal->start 순서이므로 in-place로 뒤집어 추가 할당을 피합니다.
        int left = 0;
        int right = result.Count - 1;
        while (left < right)
        {
            Vector2Int temp = result[left];
            result[left] = result[right];
            result[right] = temp;
            left++;
            right--;
        }
    }

    private int ToIndex(int col, int row)
    {
        return row * _width + col;
    }

    private static int Heuristic(int col, int row, int goalCol, int goalRow)
    {
        return (Mathf.Abs(goalCol - col) + Mathf.Abs(goalRow - row)) * MOVE_COST;
    }

    private static bool IsWalkable(DungeonData data, int col, int row)
    {
        if (!data.InBounds(col, row)) return false;

        // 복도 추적 수정 지점:
        // DungeonData.IsWalkable은 ROOM(1), CORRIDOR(2), STAIR_UP(3)을 허용합니다.
        // 따라서 플레이어가 복도에 있어도 목표 타일이 유효해져 A* 경로가 정상 생성됩니다.
        return data.IsWalkable(col, row);
    }
}
