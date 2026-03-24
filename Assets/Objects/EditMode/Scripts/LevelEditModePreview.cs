using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Tile = UnityEngine.Tilemaps.Tile;

namespace VerbGame
{
    // ドラッグ中の矩形プレビュー描画だけを担当する。
    public sealed class LevelEditModePreview
    {
        // プレビュー用オブジェクトをぶら下げる先。
        private readonly Transform ownerTransform;
        // セル中心座標を引くための Grid。
        private readonly Grid grid;
        // 配置プレビューの半透明度。
        private readonly float previewAlpha;
        // Tilemap より前に見せるための描画順。
        private readonly int previewSortingOrder;
        // 必要数だけ再利用する SpriteRenderer プール。
        private readonly List<SpriteRenderer> previewRenderers = new();

        // プレビュー専用の親オブジェクト。
        private Transform previewRoot;
        // 配置対象にスプライトが無い時に使う代替白スプライト。
        private Sprite fallbackPreviewSprite;

        public LevelEditModePreview(Transform ownerTransform, Grid grid, float previewAlpha, int previewSortingOrder)
        {
            this.ownerTransform = ownerTransform;
            this.grid = grid;
            this.previewAlpha = previewAlpha;
            this.previewSortingOrder = previewSortingOrder;
        }

        // ドラッグ中の矩形プレビューを更新する。
        public void ShowRect(Vector3Int startCell, Vector3Int endCell, bool isDestroyDragging, WallPanelDefinition selectedEntry)
        {
            if (grid == null)
            {
                Hide();
                return;
            }

            int minX = Mathf.Min(startCell.x, endCell.x);
            int maxX = Mathf.Max(startCell.x, endCell.x);
            int minY = Mathf.Min(startCell.y, endCell.y);
            int maxY = Mathf.Max(startCell.y, endCell.y);
            if (!isDestroyDragging && selectedEntry != null && selectedEntry.IsSpawn)
            {
                // Spawn プレビューだけは最後に指している 1 マスだけ見せる。
                minX = maxX = endCell.x;
                minY = maxY = endCell.y;
            }

            int requiredCount = (maxX - minX + 1) * (maxY - minY + 1);
            EnsurePreviewPool(requiredCount);

            Sprite previewSprite = !isDestroyDragging
                ? GetTileSprite(selectedEntry?.Tile) ?? GetFallbackPreviewSprite()
                : GetFallbackPreviewSprite();

            Color previewColor = isDestroyDragging
                ? new Color(1f, 0.35f, 0.35f, previewAlpha)
                : new Color(1f, 1f, 1f, previewAlpha);

            int index = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    SpriteRenderer renderer = previewRenderers[index++];
                    renderer.sprite = previewSprite;
                    renderer.color = previewColor;
                    renderer.transform.position = grid.GetCellCenterWorld(new Vector3Int(x, y, 0));
                    renderer.gameObject.SetActive(true);
                }
            }

            for (int i = index; i < previewRenderers.Count; i++)
            {
                previewRenderers[i].gameObject.SetActive(false);
            }
        }

        // いま出ているプレビューをすべて隠す。
        public void Hide()
        {
            for (int i = 0; i < previewRenderers.Count; i++)
            {
                if (previewRenderers[i] != null)
                {
                    previewRenderers[i].gameObject.SetActive(false);
                }
            }
        }

        // 必要数ぶんのプレビュー用 SpriteRenderer を確保する。
        private void EnsurePreviewPool(int requiredCount)
        {
            if (previewRoot == null)
            {
                // プレビューは専用ルート配下にまとめて生成する。
                GameObject root = new("EditPreview");
                root.transform.SetParent(ownerTransform, false);
                previewRoot = root.transform;
            }

            while (previewRenderers.Count < requiredCount)
            {
                GameObject previewObject = new($"Preview_{previewRenderers.Count}");
                previewObject.transform.SetParent(previewRoot, false);
                SpriteRenderer renderer = previewObject.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = previewSortingOrder;
                renderer.gameObject.SetActive(false);
                previewRenderers.Add(renderer);
            }
        }

        // プレビュー用の簡易白スプライトを遅延生成で作る。
        private Sprite GetFallbackPreviewSprite()
        {
            if (fallbackPreviewSprite != null) return fallbackPreviewSprite;

            Texture2D texture = new(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            fallbackPreviewSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            fallbackPreviewSprite.name = "EditPreviewSprite";
            return fallbackPreviewSprite;
        }

        // Tile アセットから表示用スプライトを取り出す。
        private static Sprite GetTileSprite(TileBase tileBase)
        {
            return tileBase is Tile tile ? tile.sprite : null;
        }
    }
}
