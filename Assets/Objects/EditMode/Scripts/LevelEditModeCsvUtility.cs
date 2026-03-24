using System;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace VerbGame
{
    // Ground Tilemap の CSV 入出力だけを担当する。
    public static class LevelEditModeCsvUtility
    {
        // Ground 全体を CSV に変換し、文字列として返す。
        public static bool TryBuildCsv(Tilemap groundTilemap, WallPanelCatalog tileCatalog, out string csv, out BoundsInt bounds, out string errorMessage)
        {
            csv = string.Empty;
            bounds = default;
            errorMessage = string.Empty;

            if (groundTilemap == null || tileCatalog == null)
            {
                errorMessage = "エクスポートに失敗しました";
                return false;
            }

            bounds = groundTilemap.cellBounds;
            if (bounds.size.x <= 0 || bounds.size.y <= 0)
            {
                errorMessage = "Ground にタイルがありません";
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

                    TileBase tile = groundTilemap.GetTile(new Vector3Int(x, y, 0));
                    int tileId = 0;
                    if (tileCatalog.TryGetPanelByTile(tile, out WallPanelDefinition entry))
                    {
                        tileId = entry.Id;
                    }

                    builder.Append(tileId);
                }
            }

            csv = builder.ToString();
            return true;
        }

        // クリップボードなどから受け取った CSV を Ground Tilemap へ復元する。
        public static bool TryImportCsv(Tilemap groundTilemap, WallPanelCatalog tileCatalog, string csv, out int importedCount, out string errorMessage)
        {
            importedCount = 0;
            errorMessage = string.Empty;

            if (groundTilemap == null || tileCatalog == null)
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

            int rowCount = Mathf.Min(height, lines.Length - 1);
            for (int row = 0; row < rowCount; row++)
            {
                int y = yMin + (height - 1 - row);
                string[] ids = SplitCsvLine(lines[row + 1]);
                for (int xOffset = 0; xOffset < width; xOffset++)
                {
                    int tileId = 0;
                    if (xOffset < ids.Length && !string.IsNullOrWhiteSpace(ids[xOffset]))
                    {
                        int.TryParse(ids[xOffset], out tileId);
                    }

                    TileBase tile = tileCatalog.GetTile(tileId);
                    if (tile == null)
                    {
                        continue;
                    }

                    groundTilemap.SetTile(new Vector3Int(xMin + xOffset, y, 0), tile);
                    importedCount++;
                }
            }

            groundTilemap.CompressBounds();
            return true;
        }

        // CSV 1 行を単純なカンマ区切りで分解する。
        private static string[] SplitCsvLine(string line)
        {
            return line.Split(',', StringSplitOptions.None);
        }
    }
}
