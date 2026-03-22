using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace VerbGame
{
    // このクラスは「論理移動」だけを担当する。
    // 見た目の補間や LitMotion は一切知らず、
    // 現在セル・次セル・法線・ドリル経路だけを計算する。
    public sealed class PlayerGridNavigator
    {
        // 地形面の候補になる4方向。
        private static readonly Vector2Int[] Cardinals = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        // 次セル探索に使う近傍。
        // 角回り込みのため、斜めも含めて見る。
        private static readonly Vector2Int[] Neighbors = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left, new(1, 1), new(1, -1), new(-1, -1), new(-1, 1) };

        // 座標変換用。
        private readonly Grid grid;
        // 地形が存在するかどうかを判定する相手。
        private readonly Tilemap groundTilemap;

        // プレイヤーが今いる空セル。
        public Vector3Int CurrentCell { get; private set; }
        // 今張り付いている地形面の法線。
        public Vector2Int SurfaceNormal { get; private set; } = Vector2Int.up;

        public PlayerGridNavigator(Grid grid, Tilemap groundTilemap)
        {
            this.grid = grid;
            this.groundTilemap = groundTilemap;
        }

        public void SnapToNearestBoundary(Vector3 worldPosition)
        {
            // 起動時は適当なワールド座標から始まるので、
            // 周囲3x3の中から一番近い境界セルを探して現在地にする。
            Vector3Int origin = grid.WorldToCell(worldPosition);
            float bestDistance = float.PositiveInfinity;

            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    Vector3Int cell = origin + new Vector3Int(x, y, 0);
                    if (HasGround(cell)) continue;

                    foreach (var normal in Cardinals)
                    {
                        if (!HasGround(cell - ToCell(normal))) continue;

                        float distance = Vector3.Distance(worldPosition, GetCellCenter(cell));
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        CurrentCell = cell;
                        SurfaceNormal = normal;
                    }
                }
            }
        }

        public bool TryGetNextStep(int direction, out Vector3Int bestCell, out Vector2Int bestNormal)
        {
            // 現在の面法線に直交するベクトルが「壁沿いの進行方向」。
            Vector2Int tangent = GetTangent(SurfaceNormal) * direction;
            float bestScore = float.NegativeInfinity;
            bestCell = CurrentCell;
            bestNormal = SurfaceNormal;

            // 凹角では、まずその場で別面に張り付く1手を優先する。
            Vector2Int turnNormal = -tangent;
            if (HasGround(CurrentCell + ToCell(tangent)) && HasGround(CurrentCell - ToCell(turnNormal)))
            {
                bestNormal = turnNormal;
                return true;
            }

            // 近傍候補を全部なめて、最も自然に進める1手を探す。
            foreach (var offset in Neighbors)
            {
                Vector3Int candidate = CurrentCell + ToCell(offset);
                if (HasGround(candidate)) continue;

                foreach (var normal in Cardinals)
                {
                    // 候補セルが地形に接していなければ、境界移動としては無効。
                    if (!HasGround(candidate - ToCell(normal))) continue;

                    // 前進成分を最優先しつつ、今の面と近い法線を少し優遇する。
                    float forward = Vector2.Dot(((Vector2)offset).normalized, tangent);
                    float score = forward * 10f + Vector2.Dot(normal, SurfaceNormal) * 2f + (offset.sqrMagnitude == 1 ? 0.25f : 0f);
                    if (forward < 0f) score += forward * 4f;
                    if (score <= bestScore) continue;

                    bestScore = score;
                    bestCell = candidate;
                    bestNormal = normal;
                }
            }

            return bestScore > float.NegativeInfinity;
        }

        public bool TryBuildDrillPath(out Vector2Int drillDirection, out List<Vector3Int> drillPath)
        {
            // ドリルは常に面法線の逆、つまり地面の中へ掘る。
            drillDirection = -SurfaceNormal;
            drillPath = new List<Vector3Int>();

            Vector3Int cell = CurrentCell + ToCell(drillDirection);
            if (!HasGround(cell)) return false;

            // 地面セルが切れるまで一直線に進み、
            // 最初の空セルまでを通過ルートとして保存する。
            do
            {
                cell += ToCell(drillDirection);
                drillPath.Add(cell);
            }
            while (HasGround(cell));

            return true;
        }

        public void CommitMove(Vector3Int nextCell, Vector2Int nextNormal)
        {
            // 通常移動1手の論理結果を確定する。
            CurrentCell = nextCell;
            SurfaceNormal = nextNormal;
        }

        public void FinishDrill(Vector3Int endCell, Vector2Int drillDirection)
        {
            // ドリル終了後の位置と向きを確定する。
            CurrentCell = endCell;
            SurfaceNormal = drillDirection;
        }

        public bool IsConvexCornerTurn(Vector3Int nextCell, Vector2Int nextNormal)
        {
            // 対角移動かつ法線が変わるケースを、凸角ターンとして扱う。
            Vector3Int delta = nextCell - CurrentCell;
            return delta.x != 0 && delta.y != 0 && nextNormal != SurfaceNormal;
        }

        // 凸角ターンの中間点。
        // 現在の面法線方向へ少し回り込んでから、次セルへ入る。
        public Vector3Int GetConvexCornerWaypoint(Vector3Int nextCell) => CurrentCell + (nextCell - CurrentCell) + ToCell(SurfaceNormal);
        // セル中心ワールド座標を返す。
        public Vector3 GetCellCenter(Vector3Int cell) => grid.GetCellCenterWorld(cell);
        // 4方向法線を、見た目用の Z 回転へ変換する。
        public Quaternion GetRotation(Vector2Int normal) => Quaternion.Euler(0f, 0f, -Mathf.Atan2(normal.x, normal.y) * Mathf.Rad2Deg);

        // 法線から接線を作る。
        private Vector2Int GetTangent(Vector2Int normal) => new(normal.y, -normal.x);
        // 2D方向をセル座標へ拡張する。
        private Vector3Int ToCell(Vector2Int value) => new(value.x, value.y, 0);
        // そのセルに地形タイルがあるかどうかだけを見る。
        private bool HasGround(Vector3Int cell) => groundTilemap != null && groundTilemap.HasTile(cell);
    }
}
