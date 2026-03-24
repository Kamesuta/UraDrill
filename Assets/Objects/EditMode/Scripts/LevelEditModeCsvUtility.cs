using System;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace VerbGame
{
    // Ground Tilemap の CSV 入出力だけを担当する。
    public static class LevelEditModeCsvUtility
    {
        // Ground / Overlay を 1 セル 1 トークンの CSV に変換して返す。
        public static bool TryBuildCsv(Tilemap groundTilemap, Tilemap overlayTilemap, WallPanelCatalog tileCatalog, out string csv, out BoundsInt bounds, out string errorMessage)
        {
            csv = string.Empty;
            bounds = default;
            errorMessage = string.Empty;

            if (groundTilemap == null || overlayTilemap == null || tileCatalog == null)
            {
                errorMessage = "エクスポートに失敗しました";
                return false;
            }

            if (!TryGetCombinedBounds(groundTilemap, overlayTilemap, out bounds))
            {
                errorMessage = "Ground / Overlay にタイルがありません";
                return false;
            }

            StringBuilder builder = new();
            // 1 行目は bounds、2 行目以降はタイル ID の表にする。
            builder.Append(bounds.xMin).Append(',').Append(bounds.yMin).Append(',').Append(bounds.size.x).Append(',').Append(bounds.size.y);

            for (int y = bounds.yMax - 1; y >= bounds.yMin; y--)
            {
                builder.AppendLine();
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    if (x > bounds.xMin)
                    {
                        builder.Append(',');
                    }

                    Vector3Int cell = new(x, y, 0);
                    int groundId = 0;
                    string overlayId = string.Empty;

                    if (tileCatalog.TryGetPanelByTile(groundTilemap.GetTile(cell), out WallPanelDefinition groundEntry) &&
                        groundEntry != null &&
                        !groundEntry.IsOverlay &&
                        groundEntry.TryGetNumericId(out int parsedGroundId))
                    {
                        groundId = parsedGroundId;
                    }

                    if (tileCatalog.TryGetPanelByTile(overlayTilemap.GetTile(cell), out WallPanelDefinition overlayEntry) &&
                        overlayEntry != null &&
                        overlayEntry.IsOverlay)
                    {
                        overlayId = overlayEntry.Id;
                    }

                    builder.Append(BuildCellToken(groundId, overlayId));
                }
            }

            csv = builder.ToString();
            return true;
        }

        // クリップボードなどから受け取った CSV を Ground / Overlay へ復元する。
        public static bool TryImportCsv(Tilemap groundTilemap, Tilemap overlayTilemap, WallPanelCatalog tileCatalog, string csv, out int importedCount, out string errorMessage)
        {
            importedCount = 0;
            errorMessage = string.Empty;

            if (groundTilemap == null || overlayTilemap == null || tileCatalog == null)
            {
                errorMessage = "インポートに失敗しました";
                return false;
            }

            if (string.IsNullOrWhiteSpace(csv))
            {
                errorMessage = "クリップボードが空です";
                return false;
            }

            string[] lines = csv.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                errorMessage = "CSV の形式が不正です";
                return false;
            }

            string[] header = SplitCsvLine(lines[0]);
            if (header.Length < 4 ||
                !int.TryParse(header[0], out int xMin) ||
                !int.TryParse(header[1], out int yMin) ||
                !int.TryParse(header[2], out int width) ||
                !int.TryParse(header[3], out int height) ||
                width <= 0 ||
                height <= 0)
            {
                errorMessage = "CSV ヘッダーが不正です";
                return false;
            }

            // いったん全消ししてから CSV 内容を敷き直す。
            groundTilemap.ClearAllTiles();
            overlayTilemap.ClearAllTiles();

            int rowCount = Mathf.Min(height, lines.Length - 1);
            for (int row = 0; row < rowCount; row++)
            {
                int y = yMin + (height - 1 - row);
                string[] ids = SplitCsvLine(lines[row + 1]);
                for (int xOffset = 0; xOffset < width; xOffset++)
                {
                    string token = xOffset < ids.Length ? ids[xOffset] : string.Empty;
                    if (!TryParseCellToken(token, out int groundId, out string overlayId))
                    {
                        errorMessage = $"CSV セルの形式が不正です: {token}";
                        return false;
                    }

                    Vector3Int cell = new(xMin + xOffset, y, 0);
                    TileBase groundTile = tileCatalog.GetGroundTile(groundId);
                    TileBase overlayTile = tileCatalog.GetOverlayTile(overlayId);

                    if (groundId != 0 && groundTile == null)
                    {
                        errorMessage = $"Ground ID が未定義です: {groundId}";
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(overlayId) && overlayTile == null)
                    {
                        errorMessage = $"Overlay ID が未定義です: {overlayId}";
                        return false;
                    }

                    if (groundTile != null)
                    {
                        groundTilemap.SetTile(cell, groundTile);
                        importedCount++;
                    }

                    if (overlayTile != null)
                    {
                        overlayTilemap.SetTile(cell, overlayTile);
                        importedCount++;
                    }
                }
            }

            groundTilemap.CompressBounds();
            overlayTilemap.CompressBounds();
            return true;
        }

        // CSV 1 行を単純なカンマ区切りで分解する。
        private static string[] SplitCsvLine(string line)
        {
            return line.Split(',', StringSplitOptions.None);
        }

        private static string BuildCellToken(int groundId, string overlayId)
        {
            return string.IsNullOrWhiteSpace(overlayId)
                ? groundId.ToString()
                : $"{groundId}{overlayId}";
        }

        private static bool TryParseCellToken(string token, out int groundId, out string overlayId)
        {
            groundId = 0;
            overlayId = string.Empty;

            if (string.IsNullOrWhiteSpace(token))
            {
                return true;
            }

            token = token.Trim().ToLowerInvariant();
            int splitIndex = 0;
            while (splitIndex < token.Length && char.IsDigit(token[splitIndex]))
            {
                splitIndex++;
            }

            string numericPart = token[..splitIndex];
            string overlayPart = token[splitIndex..];
            if (numericPart.Length > 0 && !int.TryParse(numericPart, out groundId))
            {
                return false;
            }

            if (overlayPart.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < overlayPart.Length; i++)
            {
                if (!char.IsLetter(overlayPart[i]))
                {
                    return false;
                }
            }

            overlayId = overlayPart;
            return true;
        }

        private static bool TryGetCombinedBounds(Tilemap groundTilemap, Tilemap overlayTilemap, out BoundsInt bounds)
        {
            bounds = default;
            bool hasGround = TryGetUsedBounds(groundTilemap, out BoundsInt groundBounds);
            bool hasOverlay = TryGetUsedBounds(overlayTilemap, out BoundsInt overlayBounds);
            if (!hasGround && !hasOverlay)
            {
                return false;
            }

            if (!hasGround)
            {
                bounds = overlayBounds;
                return true;
            }

            if (!hasOverlay)
            {
                bounds = groundBounds;
                return true;
            }

            int xMin = Mathf.Min(groundBounds.xMin, overlayBounds.xMin);
            int yMin = Mathf.Min(groundBounds.yMin, overlayBounds.yMin);
            int xMax = Mathf.Max(groundBounds.xMax, overlayBounds.xMax);
            int yMax = Mathf.Max(groundBounds.yMax, overlayBounds.yMax);
            bounds = new BoundsInt(xMin, yMin, 0, xMax - xMin, yMax - yMin, 1);
            return true;
        }

        private static bool TryGetUsedBounds(Tilemap tilemap, out BoundsInt bounds)
        {
            bounds = default;
            if (tilemap == null)
            {
                return false;
            }

            BoundsInt cellBounds = tilemap.cellBounds;
            bool foundAnyTile = false;
            int minX = 0;
            int minY = 0;
            int maxX = 0;
            int maxY = 0;

            for (int y = cellBounds.yMin; y < cellBounds.yMax; y++)
            {
                for (int x = cellBounds.xMin; x < cellBounds.xMax; x++)
                {
                    if (!tilemap.HasTile(new Vector3Int(x, y, 0))) continue;

                    if (!foundAnyTile)
                    {
                        minX = x;
                        minY = y;
                        maxX = x + 1;
                        maxY = y + 1;
                        foundAnyTile = true;
                        continue;
                    }

                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x + 1);
                    maxY = Mathf.Max(maxY, y + 1);
                }
            }

            if (!foundAnyTile)
            {
                return false;
            }

            bounds = new BoundsInt(minX, minY, 0, maxX - minX, maxY - minY, 1);
            return true;
        }
    }
}
