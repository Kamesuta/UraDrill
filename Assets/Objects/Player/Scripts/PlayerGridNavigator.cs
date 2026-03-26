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
        // 座標変換用。
        private readonly Grid grid;
        // 地形が存在するかどうかを判定する相手。
        private readonly Tilemap groundTilemap;
        // Ground に重ねる特殊タイル用レイヤー。
        private readonly Tilemap overlayTilemap;
        // 地形タイルと特殊パネル定義の対応表。
        private readonly WallPanelCatalog wallPanelCatalog;

        // プレイヤーが今いる空セル。
        public Vector3Int CurrentCell { get; private set; }
        // 今張り付いている地形面の法線。
        public Vector2Int SurfaceNormal { get; private set; } = Vector2Int.up;

        public PlayerGridNavigator(Grid grid, Tilemap groundTilemap, Tilemap overlayTilemap, WallPanelCatalog wallPanelCatalog)
        {
            this.grid = grid;
            this.groundTilemap = groundTilemap;
            this.overlayTilemap = overlayTilemap;
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

            BoundsInt bounds = GetSearchBounds();
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
            bestCell = CurrentCell;
            bestNormal = SurfaceNormal;

            // 1. 同じ面をそのまま進めるなら、それが最も自然な1手。
            Vector3Int straightCell = CurrentCell + ToCell(tangent);
            if (IsBoundaryCell(straightCell, SurfaceNormal))
            {
                bestCell = straightCell;
                return true;
            }

            // 2. 前方が壁なら、現在セルに重なっているステップだけ内回りを許可する。
            // 通常地形はここで停止する。
            if (HasGround(straightCell))
            {
                if (TryTraverseCornerFromCurrentStep(tangent, out bestCell, out bestNormal))
                {
                    return true;
                }

                return false;
            }

            // 3. 前方が崖なら、踏んでいるステップだけ外回りを許可する。
            // それも無ければ、そのまま空中へ踏み出してから落下する。
            if (TryTraverseCornerFromSupportStep(tangent, out bestCell, out bestNormal))
            {
                return true;
            }

            bestCell = straightCell;
            return true;
        }

        private bool TryTraverseCornerFromCurrentStep(Vector2Int tangent, out Vector3Int bestCell, out Vector2Int bestNormal)
        {
            bestCell = CurrentCell;
            bestNormal = SurfaceNormal;

            // 凹面は「今いるセルに重なっているステップ」を見る。
            WallPanelDefinition currentPanel = GetOverlayPanel(CurrentCell);
            if (!CanTraverseStepPanel(currentPanel, tangent))
            {
                return false;
            }

            return TryTraverseCorner(tangent, out bestCell, out bestNormal);
        }

        private bool TryTraverseCornerFromSupportStep(Vector2Int tangent, out Vector3Int bestCell, out Vector2Int bestNormal)
        {
            bestCell = CurrentCell;
            bestNormal = SurfaceNormal;

            // 凸面は「今足を乗せている側の地形」に置かれたステップを見る。
            WallPanelDefinition supportPanel = GetPanel(CurrentCell - ToCell(SurfaceNormal));
            if (!CanTraverseStepPanel(supportPanel, tangent))
            {
                return false;
            }

            return TryTraverseCorner(tangent, out bestCell, out bestNormal);
        }

        private bool TryTraverseCorner(Vector2Int tangent, out Vector3Int bestCell, out Vector2Int bestNormal)
        {
            bestCell = CurrentCell;
            bestNormal = SurfaceNormal;

            // ステップで許可された時だけ、角を回り込む。
            // 凸角でも凹角でも、論理上は「斜め1セル + 面法線変更」で表せる。
            Vector2Int cornerNormal = tangent;
            Vector3Int cornerCell = CurrentCell + ToCell(tangent - SurfaceNormal);
            if (IsBoundaryCell(cornerCell, cornerNormal))
            {
                bestCell = cornerCell;
                bestNormal = cornerNormal;
                return true;
            }

            // 最後に、その場で面だけ切り替える凹角ターンを試す。
            // 窪みの底や天井際では、この1手で向き直してから次の直進へつなぐ。
            Vector2Int turnNormal = -tangent;
            if (IsBoundaryCell(CurrentCell, turnNormal) && HasGround(CurrentCell + ToCell(tangent)))
            {
                bestNormal = turnNormal;
                return true;
            }

            return false;
        }

        public bool TryBuildDrillPath(out Vector2Int drillDirection, out List<Vector3Int> drillPath, out Vector3Int endCell, out Vector2Int endNormal, out bool bouncedByHardWall, out List<int> drillTurnIndices, out List<Vector2Int> drillTurnDirections)
        {
            // ドリルは常に面法線の逆、つまり地面の中へ掘る。
            drillDirection = -SurfaceNormal;
            drillPath = new List<Vector3Int>();
            endCell = CurrentCell;
            endNormal = SurfaceNormal;
            bouncedByHardWall = false;
            drillTurnIndices = new List<int>();
            drillTurnDirections = new List<Vector2Int>();

            Vector2Int currentDirection = drillDirection;
            Vector3Int cell = CurrentCell + ToCell(currentDirection);
            if (!HasGround(cell)) return false;

            // 地面セルが切れるまで一直線に進む。
            // 硬い壁にぶつかったら、通過ではなく反射ルートへ切り替える。
            while (HasGround(cell))
            {
                WallPanelDefinition panel = GetPanel(cell);
                if (panel != null && panel.BouncesDrill)
                {
                    if (!TryResolveBounce(panel, currentDirection, out Vector2Int bounceDirection, out bool bounceAtSurface))
                    {
                        return false;
                    }

                    // 1セル目から硬い壁なら掘れないので不発にする。
                    if (drillPath.Count == 0) return false;

                    bouncedByHardWall = true;
                    if (bounceAtSurface)
                    {
                        ApplySurfaceBounce(drillPath, out endCell, out endNormal);
                        BuildDrillTurns(drillDirection, drillPath, drillTurnIndices, drillTurnDirections);
                        return true;
                    }

                    drillPath.Add(cell);
                    currentDirection = bounceDirection;
                    cell += ToCell(currentDirection);
                    continue;
                }

                drillPath.Add(cell);
                cell += ToCell(currentDirection);
            }

            // 通常の壁なら、最後は地中を抜けた先の空セルへ出る。
            drillPath.Add(cell);
            endCell = cell;
            endNormal = currentDirection;
            BuildDrillTurns(drillDirection, drillPath, drillTurnIndices, drillTurnDirections);

            return true;
        }

        public bool TryBuildSlipFall(out List<Vector3Int> fallPath, out Vector3Int landingCell, out Vector2Int landingNormal)
        {
            fallPath = new List<Vector3Int>();
            landingCell = CurrentCell;
            landingNormal = SurfaceNormal;

            // いまの面に重力で踏ん張れない時だけ、その法線に対して逆向きへ落ちる。
            // つまり床なら下、天井なら上、右壁なら左へ落下する。
            if (CanStayAttachedToSurface(landingCell, SurfaceNormal)) return false;

            Vector2Int gravityDirection = GetGravityDirection(SurfaceNormal);

            while (true)
            {
                // 落下中も、同じ向きの面へ再び接地できたらそこで止まる。
                if (CanStayAttachedToSurface(landingCell, SurfaceNormal))
                {
                    landingNormal = SurfaceNormal;
                    return landingCell != CurrentCell;
                }

                // まだ支えが無ければ、重力方向へ1セルぶん落とす。
                Vector3Int nextCell = landingCell + ToCell(gravityDirection);
                fallPath.Add(nextCell);
                landingCell = nextCell;

                // タイルマップ範囲の外へ抜けたら、それ以上は追わない。
                if (groundTilemap != null && IsOutsideFallBounds(landingCell))
                {
                    break;
                }
            }

            landingNormal = SurfaceNormal;
            return landingCell != CurrentCell;
        }

        public bool IsOutsideFallBounds(Vector3Int cell, int margin = 0)
        {
            // Ground / Overlay を合わせたステージ Bounds から、
            // 指定マージンぶん外へ出たかどうかを判定する。
            BoundsInt bounds = GetSearchBounds();
            if (bounds.size.x <= 0 || bounds.size.y <= 0)
            {
                return false;
            }

            int safeMargin = Mathf.Max(0, margin);
            int minX = bounds.xMin - safeMargin;
            int maxX = bounds.xMax - 1 + safeMargin;
            int minY = bounds.yMin - safeMargin;
            int maxY = bounds.yMax - 1 + safeMargin;
            return cell.x <= minX || cell.x >= maxX || cell.y <= minY || cell.y >= maxY;
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
            // 長方形の硬い壁なら開始地点へ戻り、
            // 斜めブロックなら反射後の出口と向きを採用する。
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
            // Overlay があれば、まずそちらの特殊効果を優先する。
            TileBase tile = overlayTilemap != null ? overlayTilemap.GetTile(cell) : null;
            tile = tile != null ? tile : groundTilemap != null ? groundTilemap.GetTile(cell) : null;
            return wallPanelCatalog != null ? wallPanelCatalog.GetPanel(tile) : null;
        }

        private WallPanelDefinition GetGroundPanel(Vector3Int cell)
        {
            TileBase tile = groundTilemap != null ? groundTilemap.GetTile(cell) : null;
            return wallPanelCatalog != null ? wallPanelCatalog.GetPanel(tile) : null;
        }

        private WallPanelDefinition GetOverlayPanel(Vector3Int cell)
        {
            TileBase tile = overlayTilemap != null ? overlayTilemap.GetTile(cell) : null;
            return wallPanelCatalog != null ? wallPanelCatalog.GetPanel(tile) : null;
        }

        public bool IsTouchingCheckpoint()
        {
            // Checkpoint は Spawn と同様に「プレイヤーがいる空セル側」の Overlay へ置く前提。
            return GetPanel(CurrentCell)?.IsGoal ?? false;
        }

        private bool CanStayAttachedToSurface(Vector3Int cell, Vector2Int normal)
        {
            // 支えの地形が消えたら、その面にはもう立てない。
            Vector3Int supportCell = cell - ToCell(normal);
            if (!HasGround(supportCell)) return false;

            // 氷だけは従来どおり、床以外に留まれない。
            WallPanelDefinition supportPanel = GetPanel(supportCell);
            if (supportPanel?.CausesSlip ?? false)
            {
                return normal == Vector2Int.up;
            }

            return true;
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

        private bool IsBoundaryCell(Vector3Int cell, Vector2Int normal)
        {
            // プレイヤーが存在できるのは空セルかつ、
            // 指定法線の反対側に支えとなる地形がある位置だけ。
            if (HasGround(cell)) return false;
            return HasGround(cell - ToCell(normal));
        }

        private bool IsSpawnCell(Vector3Int cell)
        {
            return GetOverlayPanel(cell)?.IsSpawn ?? false;
        }

        private bool CanTraverseStepPanel(WallPanelDefinition panel, Vector2Int moveDirection)
        {
            if (panel == null || !panel.AllowsStepTraversal) return false;

            if (moveDirection == Vector2Int.right)
            {
                return panel.Direction == WallPanelDirection.RightDown ||
                       panel.Direction == WallPanelDirection.RightUp;
            }

            if (moveDirection == Vector2Int.left)
            {
                return panel.Direction == WallPanelDirection.LeftUp ||
                       panel.Direction == WallPanelDirection.LeftDown;
            }

            if (moveDirection == Vector2Int.up)
            {
                return panel.Direction == WallPanelDirection.RightUp ||
                       panel.Direction == WallPanelDirection.LeftUp;
            }

            if (moveDirection == Vector2Int.down)
            {
                return panel.Direction == WallPanelDirection.RightDown ||
                       panel.Direction == WallPanelDirection.LeftDown;
            }

            return false;
        }

        private Vector2Int GetGravityDirection(Vector2Int normal) => -normal;

        private BoundsInt GetSearchBounds()
        {
            BoundsInt groundBounds = groundTilemap != null ? groundTilemap.cellBounds : default;
            BoundsInt overlayBounds = overlayTilemap != null ? overlayTilemap.cellBounds : default;

            if (overlayTilemap == null)
            {
                return groundBounds;
            }

            int xMin = Mathf.Min(groundBounds.xMin, overlayBounds.xMin);
            int yMin = Mathf.Min(groundBounds.yMin, overlayBounds.yMin);
            int xMax = Mathf.Max(groundBounds.xMax, overlayBounds.xMax);
            int yMax = Mathf.Max(groundBounds.yMax, overlayBounds.yMax);
            return new BoundsInt(xMin, yMin, 0, xMax - xMin, yMax - yMin, 1);
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

        private void ApplySurfaceBounce(List<Vector3Int> drillPath, out Vector3Int endCell, out Vector2Int endNormal)
        {
            // 壁セルへは入らず、直前セルを折り返し位置としてそのまま戻す。
            BuildHardWallBouncePath(drillPath);
            endCell = CurrentCell;
            endNormal = SurfaceNormal;
        }

        private bool TryGetDirectionalBounce(WallPanelDirection direction, Vector2Int incomingDirection, out Vector2Int bounceDirection, out bool bounceAtSurface)
        {
            bounceDirection = default;
            bounceAtSurface = false;

            // inVec は「そのブロックへ進入してくるドリルの移動方向」。
            // 各斜めブロックは対角の法線を持つものとして扱い、
            // Dot が正なら平らな辺に当たっているので真後ろへ返す。
            // Dot が負なら斜面側から当たっているので、
            // Cross の符号で時計回り / 反時計回りの 90 度反射を決める。
            if (!TryGetDirectionalNormal(direction, out Vector2Int panelNormal))
            {
                return false;
            }

            return TryResolveDirectionalBounce(incomingDirection, panelNormal, out bounceDirection, out bounceAtSurface);
        }

        private bool TryResolveBounce(WallPanelDefinition panel, Vector2Int incomingDirection, out Vector2Int bounceDirection, out bool bounceAtSurface)
        {
            bounceDirection = default;
            bounceAtSurface = false;
            if (panel == null || !panel.BouncesDrill) return false;

            // 長方形の硬い壁も、斜めブロックの平らな面も、
            // 「壁セルへ入らず手前で折り返す」は同じ処理へ寄せる。
            if (panel.BouncesBackDrill)
            {
                bounceDirection = -incomingDirection;
                bounceAtSurface = true;
                return true;
            }

            if (panel.BouncesDrillByDirection)
            {
                return TryGetDirectionalBounce(panel.Direction, incomingDirection, out bounceDirection, out bounceAtSurface);
            }

            return false;
        }

        private bool TryGetDirectionalNormal(WallPanelDirection direction, out Vector2Int panelNormal)
        {
            panelNormal = default;
            switch (direction)
            {
                case WallPanelDirection.RightDown:
                    panelNormal = new Vector2Int(-1, 1);
                    return true;
                case WallPanelDirection.RightUp:
                    panelNormal = new Vector2Int(-1, -1);
                    return true;
                case WallPanelDirection.LeftUp:
                    panelNormal = new Vector2Int(1, -1);
                    return true;
                case WallPanelDirection.LeftDown:
                    panelNormal = new Vector2Int(1, 1);
                    return true;
                default:
                    return false;
            }
        }

        private bool TryResolveDirectionalBounce(Vector2Int incomingDirection, Vector2Int panelNormal, out Vector2Int bounceDirection, out bool bounceAtSurface)
        {
            bounceDirection = default;
            bounceAtSurface = false;
            int dot = incomingDirection.x * panelNormal.x + incomingDirection.y * panelNormal.y;
            if (dot > 0)
            {
                // 平らな辺に当たった時は、長方形硬壁と同じく真後ろへ返す。
                bounceDirection = -incomingDirection;
                bounceAtSurface = true;
                return true;
            }

            if (dot < 0)
            {
                int crossZ = panelNormal.x * incomingDirection.y - panelNormal.y * incomingDirection.x;
                if (crossZ > 0)
                {
                    bounceDirection = RotateClockwise(incomingDirection);
                    return true;
                }

                if (crossZ < 0)
                {
                    bounceDirection = RotateCounterClockwise(incomingDirection);
                    return true;
                }
            }

            return false;
        }

        private void BuildDrillTurns(Vector2Int initialDirection, List<Vector3Int> drillPath, List<int> drillTurnIndices, List<Vector2Int> drillTurnDirections)
        {
            if (drillPath == null || drillPath.Count < 2) return;

            Vector2Int currentDirection = initialDirection;
            for (int i = 0; i < drillPath.Count - 1; i++)
            {
                Vector3Int delta = drillPath[i + 1] - drillPath[i];
                Vector2Int nextDirection = new(delta.x, delta.y);
                if (nextDirection == currentDirection) continue;

                // そのセルへ到達した直後に向きが変わる位置を記録する。
                drillTurnIndices.Add(i);
                drillTurnDirections.Add(nextDirection);
                currentDirection = nextDirection;
            }
        }

        private Vector2Int RotateClockwise(Vector2Int value) => new(value.y, -value.x);
        private Vector2Int RotateCounterClockwise(Vector2Int value) => new(-value.y, value.x);
    }
}
