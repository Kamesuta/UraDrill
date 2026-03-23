using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Tile = UnityEngine.Tilemaps.Tile;

namespace VerbGame
{
    // レベル編集モードの司令塔。
    // UI の開閉、タイル選択、ドラッグ塗り、CSV 入出力、スポーン表示制御をまとめて担当する。
    public sealed class LevelEditModeController : MonoBehaviour
    {
        [Header("シーン参照")]
        [SerializeField] private Camera worldCamera;
        [SerializeField] private CameraController cameraController;
        [SerializeField] private Grid grid;
        [SerializeField] private Tilemap groundTilemap;
        [SerializeField] private LevelEditTilePalette tilePalette;
        [SerializeField] private Transform playerTransform;

        [Header("UI")]
        [SerializeField] private GameObject uiRoot;
        [SerializeField] private RectTransform tileListContent;
        [SerializeField] private Button tileButtonTemplate;
        [SerializeField] private Button respawnButton;
        [SerializeField] private Button exportButton;
        [SerializeField] private Button importButton;
        [SerializeField] private TMP_Text messageLabel;
        [SerializeField] private float tileIconSize = 48f;
        [SerializeField] private float previewAlpha = 0.35f;
        [SerializeField] private int previewSortingOrder = 10;

        // ランタイム生成する UI ボタンと、ドラッグ中プレビューの描画プール。
        private readonly List<Button> tileButtons = new();
        private readonly List<LevelEditTilePalette.Entry> selectableEntries = new();
        private readonly List<SpriteRenderer> previewRenderers = new();

        // 編集モードの現在状態。
        private LevelEditTilePalette.Entry selectedEntry;
        private bool isUiVisible;
        private bool isDragging;
        private bool isDestroyDragging;
        private bool isMiddleButtonHeld;
        private bool isMiddleDragPanning;
        private Vector3Int dragStartCell;
        private Vector3Int dragCurrentCell;
        private Vector2 middlePressScreenPosition;
        private Vector3 cameraPanStartPosition;
        private Transform previewRoot;
        private Sprite fallbackPreviewSprite;

        // 起動時に UI と初期状態を組み立てる。
        private void Awake()
        {
            // 起動時に UI を組み立てておくが、表示自体は閉じた状態から始める。
            if (cameraController == null && worldCamera != null)
            {
                cameraController = worldCamera.GetComponent<CameraController>();
            }

            BindButtons();
            BuildTileButtons();
            SetUiVisible(false);
            SetMessage(string.Empty);
        }

        // Tab 開閉と、編集モード中のマウス入力処理をまとめる。
        private void Update()
        {
            // プレイ中にいつでも Tab で編集 UI を開閉できる。
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                SetUiVisible(!isUiVisible);
            }

            // UI を閉じている間は入力を無視し、プレビューだけ確実に消す。
            if (!isUiVisible || Mouse.current == null || worldCamera == null || grid == null || groundTilemap == null)
            {
                HidePreview();
                return;
            }

            HandleCameraInput();
            HandleScrollSelection();
            HandlePaintingInput();
        }

        // 各 UI ボタンのクリックイベントを配線する。
        private void BindButtons()
        {
            // 各ボタンはここで一括配線する。
            if (respawnButton != null) respawnButton.onClick.AddListener(RespawnPlayerToSpawn);
            if (exportButton != null) exportButton.onClick.AddListener(ExportToClipboard);
            if (importButton != null) importButton.onClick.AddListener(ImportFromClipboard);
        }

        // パレットの内容からタイル選択ボタンを動的生成する。
        private void BuildTileButtons()
        {
            if (tileButtonTemplate == null || tileListContent == null || tilePalette == null) return;

            // タイル一覧はテンプレートから横並びで動的生成する。
            ConfigureTileListLayout();
            tileButtonTemplate.gameObject.SetActive(false);
            tileButtons.Clear();
            selectableEntries.Clear();

            IReadOnlyList<LevelEditTilePalette.Entry> entries = tilePalette.Entries;
            if (entries == null) return;

            for (int i = 0; i < entries.Count; i++)
            {
                LevelEditTilePalette.Entry entry = entries[i];
                if (entry == null || entry.id == 0 || entry.tile == null) continue;

                Button button = Instantiate(tileButtonTemplate, tileListContent);
                button.gameObject.name = $"TileButton_{entry.id}";
                button.gameObject.SetActive(true);
                button.onClick.AddListener(() => SelectEntry(entry));

                ConfigureTileButtonVisual(button, entry);

                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.gameObject.SetActive(false);
                }

                selectableEntries.Add(entry);
                tileButtons.Add(button);
            }

            if (entries.Count > 0)
            {
                SelectFirstAvailableEntry(entries);
            }
            else
            {
                // パレットが空でも操作説明だけは表示する。
                SetMessage(string.Empty);
            }
        }

        // 最初に選ぶタイルを、パレット先頭の有効エントリから決める。
        private void SelectFirstAvailableEntry(IReadOnlyList<LevelEditTilePalette.Entry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                LevelEditTilePalette.Entry entry = entries[i];
                if (entry == null || entry.id == 0 || entry.tile == null) continue;
                SelectEntry(entry);
                return;
            }
        }

        // 中クリックのドラッグでパンし、チョン押しならプレイヤー追従へ戻す。
        private void HandleCameraInput()
        {
            if (Mouse.current == null || worldCamera == null) return;

            if (Mouse.current.middleButton.wasPressedThisFrame)
            {
                isMiddleButtonHeld = true;
                isMiddleDragPanning = false;
                middlePressScreenPosition = Mouse.current.position.ReadValue();
                cameraPanStartPosition = worldCamera.transform.position;
            }

            if (!isMiddleButtonHeld)
            {
                return;
            }

            Vector2 currentScreenPosition = Mouse.current.position.ReadValue();
            if (!isMiddleDragPanning && (currentScreenPosition - middlePressScreenPosition).sqrMagnitude >= 25f)
            {
                isMiddleDragPanning = true;
                cameraController?.SetFollowEnabled(false);
            }

            if (isMiddleDragPanning)
            {
                Vector3 targetCameraPosition = cameraPanStartPosition + GetCameraPanDelta(middlePressScreenPosition, currentScreenPosition);
                targetCameraPosition.z = cameraPanStartPosition.z;

                if (cameraController != null)
                {
                    cameraController.SetManualPosition(targetCameraPosition);
                }
                else
                {
                    worldCamera.transform.position = targetCameraPosition;
                }
            }

            if (!Mouse.current.middleButton.wasReleasedThisFrame)
            {
                return;
            }

            bool wasPanning = isMiddleDragPanning;
            isMiddleButtonHeld = false;
            isMiddleDragPanning = false;

            if (!wasPanning)
            {
                ReturnCameraFollowToPlayer();
            }
        }

        // ホイール上下で選択タイルを前後に切り替える。
        private void HandleScrollSelection()
        {
            if (Mouse.current == null || selectableEntries.Count == 0) return;

            float scrollY = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) < 0.01f) return;

            SelectEntryByOffset(scrollY > 0f ? -1 : 1);
        }

        // 左クリックで配置、右クリックで破壊の矩形編集を行う。
        private void HandlePaintingInput()
        {
            if (Mouse.current == null || Mouse.current.middleButton.isPressed)
            {
                return;
            }

            bool placePressed = Mouse.current.leftButton.wasPressedThisFrame;
            bool destroyPressed = Mouse.current.rightButton.wasPressedThisFrame;
            if (placePressed || destroyPressed)
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                {
                    return;
                }

                if (!TryGetHoveredCell(out dragStartCell))
                {
                    return;
                }

                isDragging = true;
                isDestroyDragging = destroyPressed;
                dragCurrentCell = dragStartCell;
                UpdatePreview(dragStartCell, dragCurrentCell);
            }

            if (!isDragging)
            {
                return;
            }

            if (TryGetHoveredCell(out Vector3Int hoveredCell))
            {
                // ドラッグ中は現在位置に合わせてプレビュー矩形を更新し続ける。
                dragCurrentCell = hoveredCell;
                UpdatePreview(dragStartCell, dragCurrentCell);
            }

            bool dragReleased = isDestroyDragging
                ? Mouse.current.rightButton.wasReleasedThisFrame
                : Mouse.current.leftButton.wasReleasedThisFrame;
            if (!dragReleased)
            {
                return;
            }

            isDragging = false;
            if (!TryGetHoveredCell(out Vector3Int dragEndCell))
            {
                dragEndCell = dragStartCell;
            }

            ApplyRect(dragStartCell, dragEndCell, isDestroyDragging);
            HidePreview();
        }

        // マウスカーソル位置を Ground のセル座標へ変換する。
        private bool TryGetHoveredCell(out Vector3Int cell)
        {
            cell = default;

            // 2D グリッド前提なので、マウス位置から z=0 平面との交点を取ってセル化する。
            Vector2 screenPosition = Mouse.current.position.ReadValue();
            Ray ray = worldCamera.ScreenPointToRay(screenPosition);
            float distance = -ray.origin.z / ray.direction.z;
            if (distance < 0f) return false;

            Vector3 worldPosition = ray.GetPoint(distance);
            cell = grid.WorldToCell(worldPosition);
            cell.z = 0;
            return true;
        }

        // ドラッグ範囲に対して配置または破壊を適用する。
        private void ApplyRect(Vector3Int startCell, Vector3Int endCell, bool destroyTiles)
        {
            // ドラッグ範囲を矩形に正規化する。
            int minX = Mathf.Min(startCell.x, endCell.x);
            int maxX = Mathf.Max(startCell.x, endCell.x);
            int minY = Mathf.Min(startCell.y, endCell.y);
            int maxY = Mathf.Max(startCell.y, endCell.y);
            bool isSpawnTile = selectedEntry != null && selectedEntry.id < 0;
            if (!destroyTiles && isSpawnTile)
            {
                // Spawn だけは矩形塗りではなく単点配置に固定する。
                minX = maxX = endCell.x;
                minY = maxY = endCell.y;
            }

            TileBase tileToPaint = selectedEntry?.tile;
            if (!destroyTiles && tileToPaint == null)
            {
                SetMessage("選択タイルがありません");
                return;
            }

            if (!destroyTiles && isSpawnTile)
            {
                // Spawn は複数あると意味が曖昧になるので、置く前に既存を消す。
                ClearExistingSpawnTiles();
            }

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector3Int cell = new(x, y, 0);
                    groundTilemap.SetTile(cell, destroyTiles ? null : tileToPaint);
                }
            }

            groundTilemap.CompressBounds();
            RefreshSpawnTileVisibility();
            SetMessage($"{(destroyTiles ? "破壊" : "配置")} [{minX},{minY}] - [{maxX},{maxY}]");
        }

        // 現在選択中のタイルを差し替え、UI 表示も同期する。
        private void SelectEntry(LevelEditTilePalette.Entry entry)
        {
            selectedEntry = entry;
            SetMessage(string.Empty);
            UpdateTileButtonHighlights();
        }

        // ホイール操作で選択タイルを前後に送る。
        private void SelectEntryByOffset(int offset)
        {
            if (selectableEntries.Count == 0) return;

            int currentIndex = selectedEntry != null ? selectableEntries.IndexOf(selectedEntry) : -1;
            if (currentIndex < 0)
            {
                SelectEntry(selectableEntries[0]);
                return;
            }

            int nextIndex = (currentIndex + offset + selectableEntries.Count) % selectableEntries.Count;
            SelectEntry(selectableEntries[nextIndex]);
        }

        // 編集 UI の表示切替と、Spawn タイルの可視状態を同期する。
        private void SetUiVisible(bool visible)
        {
            isUiVisible = visible;
            if (uiRoot != null)
            {
                uiRoot.SetActive(visible);
            }

            // Spawn タイルは編集時だけ見せる。
            RefreshSpawnTileVisibility();
        }

        // Ground 全体を CSV にしてクリップボードへ書き出す。
        private void ExportToClipboard()
        {
            if (groundTilemap == null || tilePalette == null)
            {
                SetMessage("エクスポートに失敗しました");
                return;
            }

            BoundsInt bounds = groundTilemap.cellBounds;
            if (bounds.size.x <= 0 || bounds.size.y <= 0)
            {
                SetMessage("Ground にタイルがありません");
                return;
            }

            // 1 行目は bounds、2 行目以降はタイル ID の表にする。
            StringBuilder csv = new();
            csv.Append(bounds.xMin).Append(',').Append(bounds.yMin).Append(',').Append(bounds.size.x).Append(',').Append(bounds.size.y);

            for (int y = bounds.yMax - 1; y >= bounds.yMin; y--)
            {
                csv.AppendLine();
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    if (x > bounds.xMin) csv.Append(',');

                    TileBase tile = groundTilemap.GetTile(new Vector3Int(x, y, 0));
                    int tileId = 0;
                    if (tilePalette.TryGetEntryByTile(tile, out LevelEditTilePalette.Entry entry))
                    {
                        tileId = entry.id;
                    }

                    csv.Append(tileId);
                }
            }

            GUIUtility.systemCopyBuffer = csv.ToString();
            SetMessage($"CSVをコピーしました: {bounds.size.x}x{bounds.size.y}");
        }

        // クリップボードの CSV を読んで Ground タイルを復元する。
        private void ImportFromClipboard()
        {
            if (groundTilemap == null || tilePalette == null)
            {
                SetMessage("インポートに失敗しました");
                return;
            }

            string csv = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(csv))
            {
                SetMessage("クリップボードが空です");
                return;
            }

            string[] lines = csv.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                SetMessage("CSV の形式が不正です");
                return;
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
                SetMessage("CSV ヘッダーが不正です");
                return;
            }

            // いったん全消ししてから CSV 内容を敷き直す。
            groundTilemap.ClearAllTiles();

            int importedCount = 0;
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

                    TileBase tile = tilePalette.GetTile(tileId);
                    if (tile == null)
                    {
                        continue;
                    }

                    groundTilemap.SetTile(new Vector3Int(xMin + xOffset, y, 0), tile);
                    importedCount++;
                }
            }

            groundTilemap.CompressBounds();
            RefreshSpawnTileVisibility();
            SetMessage($"CSVを読み込みました: {importedCount} タイル");
        }

        // 選択中タイルのボタンだけ色を変えて分かるようにする。
        private void UpdateTileButtonHighlights()
        {
            for (int i = 0; i < tileButtons.Count; i++)
            {
                Button button = tileButtons[i];
                if (button == null) continue;

                bool isSelected = button.name == $"TileButton_{selectedEntry?.id}";
                SetButtonColor(button, isSelected ? new Color(0.86f, 0.77f, 0.28f, 1f) : new Color(0.24f, 0.24f, 0.24f, 0.94f));
            }
        }

        // タイル一覧コンテナを横並びレイアウトに整える。
        private void ConfigureTileListLayout()
        {
            // タイル一覧は横スクロール前提の横並び。
            if (!tileListContent.TryGetComponent<HorizontalLayoutGroup>(out var horizontalLayout))
            {
                horizontalLayout = tileListContent.gameObject.AddComponent<HorizontalLayoutGroup>();
            }

            horizontalLayout.spacing = 8f;
            horizontalLayout.padding = new RectOffset(8, 8, 8, 8);
            horizontalLayout.childAlignment = TextAnchor.MiddleLeft;
            horizontalLayout.childControlWidth = false;
            horizontalLayout.childControlHeight = false;
            horizontalLayout.childForceExpandWidth = false;
            horizontalLayout.childForceExpandHeight = false;

            if (tileListContent.TryGetComponent<VerticalLayoutGroup>(out var verticalLayout))
            {
                verticalLayout.enabled = false;
            }
        }

        // タイル選択ボタンにスプライト見た目を設定する。
        private void ConfigureTileButtonVisual(Button button, LevelEditTilePalette.Entry entry)
        {
            // テキストではなくスプライトそのものを見せる。
            RectTransform rectTransform = button.transform as RectTransform;
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = new Vector2(tileIconSize, tileIconSize);
            }

            if (button.TryGetComponent<LayoutElement>(out var layoutElement))
            {
                layoutElement.preferredWidth = tileIconSize;
                layoutElement.preferredHeight = tileIconSize;
                layoutElement.flexibleWidth = 0f;
                layoutElement.flexibleHeight = 0f;
            }

            Image iconImage = GetOrCreateIconImage(button);
            Sprite sprite = GetTileSprite(entry.tile);
            iconImage.sprite = sprite;
            iconImage.preserveAspect = true;
            iconImage.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
            iconImage.raycastTarget = false;
        }

        // いま置かれている Spawn タイル位置へプレイヤーを戻す。
        private void RespawnPlayerToSpawn()
        {
            if (playerTransform == null || grid == null || !TryFindSpawnCell(out Vector3Int spawnCell))
            {
                SetMessage("Spawn が未配置です");
                return;
            }

            playerTransform.position = grid.GetCellCenterWorld(spawnCell);
            SetMessage($"Spawn へ戻しました: {spawnCell.x},{spawnCell.y}");
        }

        // ボタン内のアイコン Image を取得し、無ければ生成する。
        private static Image GetOrCreateIconImage(Button button)
        {
            Transform existingChild = button.transform.Find("Icon");
            if (existingChild != null && existingChild.TryGetComponent(out Image existingImage))
            {
                return existingImage;
            }

            GameObject iconObject = new("Icon", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(button.transform, false);

            RectTransform rectTransform = iconObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(4f, 4f);
            rectTransform.offsetMax = new Vector2(-4f, -4f);

            return iconObject.GetComponent<Image>();
        }

        // Tile アセットから表示用スプライトを取り出す。
        private static Sprite GetTileSprite(TileBase tileBase)
        {
            return tileBase is Tile tile ? tile.sprite : null;
        }

        // ドラッグ中の矩形プレビューを更新する。
        private void UpdatePreview(Vector3Int startCell, Vector3Int endCell)
        {
            if (!isUiVisible)
            {
                HidePreview();
                return;
            }

            int minX = Mathf.Min(startCell.x, endCell.x);
            int maxX = Mathf.Max(startCell.x, endCell.x);
            int minY = Mathf.Min(startCell.y, endCell.y);
            int maxY = Mathf.Max(startCell.y, endCell.y);
            if (!isDestroyDragging && selectedEntry != null && selectedEntry.id < 0)
            {
                // Spawn プレビューだけは最後に指している 1 マスだけ見せる。
                minX = maxX = endCell.x;
                minY = maxY = endCell.y;
            }

            int requiredCount = (maxX - minX + 1) * (maxY - minY + 1);

            EnsurePreviewPool(requiredCount);

            Sprite previewSprite = !isDestroyDragging
                ? GetTileSprite(selectedEntry?.tile) ?? GetFallbackPreviewSprite()
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

        // 必要数ぶんのプレビュー用 SpriteRenderer を確保する。
        private void EnsurePreviewPool(int requiredCount)
        {
            if (previewRoot == null)
            {
                // プレビューは専用ルート配下にまとめて生成する。
                GameObject root = new("EditPreview");
                root.transform.SetParent(transform, false);
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

        // いま出ているプレビューをすべて隠す。
        private void HidePreview()
        {
            for (int i = 0; i < previewRenderers.Count; i++)
            {
                if (previewRenderers[i] != null)
                {
                    previewRenderers[i].gameObject.SetActive(false);
                }
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

        // 既存の Spawn タイルを全消去して 1 個だけに保つ。
        private void ClearExistingSpawnTiles()
        {
            if (groundTilemap == null || tilePalette == null) return;

            // Spawn は 1 個だけに保つ。
            BoundsInt bounds = groundTilemap.cellBounds;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    Vector3Int cell = new(x, y, 0);
                    TileBase tile = groundTilemap.GetTile(cell);
                    if (!tilePalette.TryGetEntryByTile(tile, out LevelEditTilePalette.Entry entry) || entry.id >= 0) continue;
                    groundTilemap.SetTile(cell, null);
                }
            }
        }

        // Spawn タイルだけ、編集モード時に見せて通常時は隠す。
        private void RefreshSpawnTileVisibility()
        {
            if (groundTilemap == null || tilePalette == null) return;

            // Spawn はデータとしては常に存在するが、通常プレイ中は視覚ノイズになるので隠す。
            BoundsInt bounds = groundTilemap.cellBounds;
            Color tileColor = isUiVisible ? Color.white : new Color(1f, 1f, 1f, 0f);

            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    Vector3Int cell = new(x, y, 0);
                    TileBase tile = groundTilemap.GetTile(cell);
                    if (!tilePalette.TryGetEntryByTile(tile, out LevelEditTilePalette.Entry entry) || entry.id >= 0) continue;

                    groundTilemap.SetTileFlags(cell, TileFlags.None);
                    groundTilemap.SetColor(cell, tileColor);
                }
            }
        }

        // スクリーン座標差分を、カメラ移動量のワールド差分へ変換する。
        private Vector3 GetCameraPanDelta(Vector2 startScreenPosition, Vector2 currentScreenPosition)
        {
            float depth = Mathf.Abs(worldCamera.transform.position.z);
            Vector3 startWorld = worldCamera.ScreenToWorldPoint(new Vector3(startScreenPosition.x, startScreenPosition.y, depth));
            Vector3 currentWorld = worldCamera.ScreenToWorldPoint(new Vector3(currentScreenPosition.x, currentScreenPosition.y, depth));
            return startWorld - currentWorld;
        }

        // 中クリックのチョン押し時に、カメラをプレイヤー追従へ戻す。
        private void ReturnCameraFollowToPlayer()
        {
            if (cameraController != null)
            {
                cameraController.ResumeFollowToTarget();
                SetMessage("プレイヤー追従に戻しました");
                return;
            }

            if (worldCamera != null && playerTransform != null)
            {
                Vector3 position = playerTransform.position;
                position.z = worldCamera.transform.position.z;
                worldCamera.transform.position = position;
                SetMessage("プレイヤー追従に戻しました");
            }
        }

        // Ground 上から Spawn タイルのセルを探して返す。
        private bool TryFindSpawnCell(out Vector3Int spawnCell)
        {
            spawnCell = default;
            if (groundTilemap == null || tilePalette == null) return false;

            // Ground 上に置かれた Spawn タイルそのもののセルを返す。
            BoundsInt bounds = groundTilemap.cellBounds;

            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    Vector3Int tileCell = new(x, y, 0);
                    TileBase tile = groundTilemap.GetTile(tileCell);
                    if (!tilePalette.TryGetEntryByTile(tile, out LevelEditTilePalette.Entry entry) || entry.id >= 0) continue;
                    spawnCell = tileCell;
                    return true;
                }
            }

            return false;
        }

        // タイル選択欄に出す表示名を ID 付きで組み立てる。
        private static string BuildEntryLabel(LevelEditTilePalette.Entry entry)
        {
            if (entry == null) return "なし";

            string label = string.IsNullOrWhiteSpace(entry.label) ? $"ID {entry.id}" : entry.label.Trim();
            return $"{entry.id}: {label}";
        }

        // CSV 1 行を単純なカンマ区切りで分解する。
        private static string[] SplitCsvLine(string line)
        {
            return line.Split(',', StringSplitOptions.None);
        }

        // ボタン背景色を安全に差し替える共通処理。
        private void SetButtonColor(Button button, Color color)
        {
            if (button == null) return;
            Image image = button.targetGraphic as Image;
            if (image != null)
            {
                image.color = color;
            }
        }

        // 画面下メッセージ欄へ短い状態表示を出す。
        private void SetMessage(string message)
        {
            if (messageLabel != null)
            {
                string operationGuide = "左:配置 / 右:破壊 / 中ドラッグ:パン / 中クリック:追従 / ホイール:切替";
                messageLabel.text = string.IsNullOrWhiteSpace(message)
                    ? operationGuide
                    : $"{operationGuide}\n{message}";
            }
        }
    }
}
