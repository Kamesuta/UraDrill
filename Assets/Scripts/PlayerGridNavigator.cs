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
        // 地形タイルと特殊パネル定義の対応表。
        private readonly WallPanelCatalog wallPanelCatalog;

        // プレイヤーが今いる空セル。
        public Vector3Int CurrentCell { get; private set; }
        // 今張り付いている地形面の法線。
        public Vector2Int SurfaceNormal { get; private set; } = Vector2Int.up;

        public PlayerGridNavigator(Grid grid, Tilemap groundTilemap, WallPanelCatalog wallPanelCatalog)
        {
            this.grid = grid;
            this.groundTilemap = groundTilemap;
            this.wallPanelCatalog = wallPanelCatalog;
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

        public bool TryFindSpawnBoundary(out Vector3Int spawnCell, out Vector2Int spawnNormal)
        {
            spawnCell = default;
            spawnNormal = Vector2Int.up;
            if (groundTilemap == null) return false;

            BoundsInt bounds = groundTilemap.cellBounds;
            Vector2Int[] searchOrder = { Vector2Int.up, Vector2Int.right, Vector2Int.left, Vector2Int.down };

            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    Vector3Int candidateCell = new(x, y, 0);
                    if (!IsSpawnCell(candidateCell)) continue;

                    for (int i = 0; i < searchOrder.Length; i++)
                    {
                        Vector2Int normal = searchOrder[i];
                        Vector3Int supportCell = candidateCell - ToCell(normal);
                        if (!HasGround(supportCell)) continue;

                        spawnCell = candidateCell;
                        spawnNormal = normal;
                        return true;
                    }
                }
            }

            return false;
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

        public bool TryBuildDrillPath(out Vector2Int drillDirection, out List<Vector3Int> drillPath, out Vector3Int endCell, out Vector2Int endNormal, out bool bouncedByHardWall, out int bounceTurnIndex)
        {
            // ドリルは常に面法線の逆、つまり地面の中へ掘る。
            drillDirection = -SurfaceNormal;
            drillPath = new List<Vector3Int>();
            endCell = CurrentCell;
            endNormal = SurfaceNormal;
            bouncedByHardWall = false;
            bounceTurnIndex = -1;

            Vector3Int cell = CurrentCell + ToCell(drillDirection);
            if (!HasGround(cell)) return false;

            // 地面セルが切れるまで一直線に進む。
            // 硬い壁にぶつかったら、通過ではなく反射ルートへ切り替える。
            while (HasGround(cell))
            {
                WallPanelDefinition panel = GetPanel(cell);
                if (panel != null && panel.BouncesDrill)
                {
                    // 1セル目から硬い壁なら掘れないので不発にする。
                    if (drillPath.Count == 0) return false;

                    bouncedByHardWall = true;
                    // 硬い壁セルそのものには入らない。
                    // 直前の通常壁セルをそのまま折り返し位置にする。
                    bounceTurnIndex = drillPath.Count - 1;
                    BuildHardWallBouncePath(drillPath);
                    endCell = CurrentCell;
                    endNormal = SurfaceNormal;
                    return true;
                }

                drillPath.Add(cell);
                cell += ToCell(drillDirection);
            }

            // 通常の壁なら、最後は地中を抜けた先の空セルへ出る。
            drillPath.Add(cell);
            endCell = cell;
            endNormal = drillDirection;

            return true;
        }

        public bool TryBuildSlipFall(out List<Vector3Int> fallPath, out Vector3Int landingCell, out Vector2Int landingNormal)
        {
            fallPath = new List<Vector3Int>();
            landingCell = CurrentCell;
            landingNormal = SurfaceNormal;

            // 床の氷は普通に立てるが、
            // 壁・天井の氷には張り付けないので滑落対象になる。
            if (!ShouldSlipOnCurrentSurface()) return false;

            while (true)
            {
                // 落下途中で同じ向きの非氷面が現れたら、
                // そこへ再び張り付ける。
                if (CanAttachToNonIceSurface(landingCell, SurfaceNormal))
                {
                    landingNormal = SurfaceNormal;
                    return landingCell != CurrentCell;
                }

                // 真下に床があるなら、最後は床へ着地する。
                if (HasGround(landingCell + Vector3Int.down))
                {
                    landingNormal = Vector2Int.up;
                    return landingCell != CurrentCell || landingNormal != SurfaceNormal;
                }

                // まだ支えが無ければ、重力方向へ1セルぶん落とす。
                Vector3Int nextCell = landingCell + Vector3Int.down;
                fallPath.Add(nextCell);
                landingCell = nextCell;

                // タイルマップ範囲の外へ抜けたら、それ以上は追わない。
                if (groundTilemap != null && landingCell.y < groundTilemap.cellBounds.yMin - 1)
                {
                    break;
                }
            }

            landingNormal = Vector2Int.up;
            return landingCell != CurrentCell || landingNormal != SurfaceNormal;
        }

        public void CommitMove(Vector3Int nextCell, Vector2Int nextNormal)
        {
            // 通常移動1手の論理結果を確定する。
            CurrentCell = nextCell;
            SurfaceNormal = nextNormal;
        }

        public void FinishDrill(Vector3Int endCell, Vector2Int endNormal)
        {
            // ドリル終了後の位置と向きを確定する。
            // 硬い壁で跳ね返った時は、開始地点と元の法線に戻る。
            CurrentCell = endCell;
            SurfaceNormal = endNormal;
        }

        public bool IsConvexCornerTurn(Vector3Int nextCell, Vector2Int nextNormal)
        {
            // 対角移動かつ法線が変わるケースを、凸角ターンとして扱う。
            Vector3Int delta = nextCell - CurrentCell;
            return delta.x != 0 && delta.y != 0 && nextNormal != SurfaceNormal;
        }

        public WallPanelDefinition GetPanel(Vector3Int cell)
        {
            TileBase tile = groundTilemap != null ? groundTilemap.GetTile(cell) : null;
            return wallPanelCatalog != null ? wallPanelCatalog.GetPanel(tile) : null;
        }

        public bool ShouldSlipOnCurrentSurface()
        {
            // 上向き法線は床なので滑らない。
            if (SurfaceNormal == Vector2Int.up) return false;
            return GetPanel(CurrentCell - ToCell(SurfaceNormal))?.CausesSlip ?? false;
        }

        public bool IsTouchingCheckpoint()
        {
            return GetPanel(CurrentCell - ToCell(SurfaceNormal))?.IsGoal ?? false;
        }

        private bool CanAttachToNonIceSurface(Vector3Int cell, Vector2Int normal)
        {
            // 今向いている面の地形を見て、
            // 氷以外ならそこへ張り直せる。
            Vector3Int supportCell = cell - ToCell(normal);
            if (!HasGround(supportCell)) return false;
            return !(GetPanel(supportCell)?.CausesSlip ?? false);
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
        private bool HasGround(Vector3Int cell)
        {
            if (groundTilemap == null) return false;

            TileBase tile = groundTilemap.GetTile(cell);
            if (tile == null) return false;
            return !IsSpawnCell(cell);
        }

        private bool IsSpawnCell(Vector3Int cell)
        {
            return GetPanel(cell)?.IsSpawn ?? false;
        }

        private void BuildHardWallBouncePath(List<Vector3Int> drillPath)
        {
            // 進んだ経路をそのまま逆順になぞって、元の位置へ戻す。
            for (int i = drillPath.Count - 2; i >= 0; i--)
            {
                drillPath.Add(drillPath[i]);
            }

            drillPath.Add(CurrentCell);
        }
    }
}
