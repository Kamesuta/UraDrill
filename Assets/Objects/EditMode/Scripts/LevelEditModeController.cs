using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace VerbGame
{
    // レベル編集モードの司令塔。
    // UI の開閉、入力の振り分け、タイル適用、Spawn 表示制御を担当する。
    public sealed class LevelEditModeController : MonoBehaviour
    {
        [Header("シーン参照")]
        [SerializeField] private Camera worldCamera;
        [SerializeField] private CameraController cameraController;
        [SerializeField] private Transform playerTransform;

        [Header("UI")]
        [SerializeField] private GameObject uiRoot;
        [SerializeField] private LevelEditModeTilePalette tilePalette;
        [SerializeField] private Button respawnButton;
        [SerializeField] private Button exportButton;
        [SerializeField] private Button importButton;
        [SerializeField] private TMP_Text messageLabel;
        [SerializeField] private float previewAlpha = 0.35f;
        [SerializeField] private int previewSortingOrder = 10;

        // 編集モード全体の表示状態。
        private bool isUiVisible;
        // 左右クリックのドラッグ編集中かどうか。
        private bool isDragging;
        // 現在のドラッグが破壊モードかどうか。
        private bool isDestroyDragging;
        // 中クリックが押されている間だけ true。
        private bool isMiddleButtonHeld;
        // 中クリックが「クリック」ではなく「パン」に入ったかどうか。
        private bool isMiddleDragPanning;
        // ドラッグ編集の開始セル。
        private Vector3Int dragStartCell;
        // ドラッグ編集の現在セル。
        private Vector3Int dragCurrentCell;
        // 中クリック開始位置。クリック判定とパン量計算の基準に使う。
        private Vector2 middlePressScreenPosition;
        // パン開始時のカメラ位置。
        private Vector3 cameraPanStartPosition;
        // ドラッグ矩形プレビューの描画担当。
        private LevelEditModePreview preview;
        // Stage 探索は Stage 側へ寄せ、編集モード側は窓口だけを見る。
        private Stage CurrentStage => Stage.Instance;
        private Grid Grid => CurrentStage != null ? CurrentStage.Grid : null;
        private Tilemap GroundTilemap => CurrentStage != null ? CurrentStage.GroundTilemap : null;
        private Tilemap OverlayTilemap => CurrentStage != null ? CurrentStage.OverlayTilemap : null;
        private WallPanelCatalog TileCatalog => CurrentStage != null ? CurrentStage.WallPanelCatalog : null;

        // 起動時に補助クラスと UI を組み立てる。
        private void Awake()
        {
            // ワールドカメラから自動で CameraController を拾えるようにしておく。
            if (cameraController == null && worldCamera != null)
            {
                cameraController = worldCamera.GetComponent<CameraController>();
            }

            // ここでは司令塔から詳細実装を分離して組み立てだけ行う。
            preview = new LevelEditModePreview(transform, Grid, previewAlpha, previewSortingOrder);

            BindButtons();
            if (tilePalette != null)
            {
                tilePalette.SelectionChanged -= HandleTilePaletteSelectionChanged;
                tilePalette.SelectionChanged += HandleTilePaletteSelectionChanged;
            }
            tilePalette?.Build();
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
            if (!isUiVisible || Mouse.current == null || worldCamera == null || Grid == null || GroundTilemap == null || OverlayTilemap == null)
            {
                preview?.Hide();
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

        // 中クリックのドラッグでパンし、チョン押しならプレイヤー追従へ戻す。
        private void HandleCameraInput()
        {
            if (Mouse.current == null || worldCamera == null) return;

            // 押し始めの位置とカメラ位置を記録して、クリックかドラッグかを後で判定する。
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
                // 少しでも動いたらクリックではなくパンとして扱う。
                isMiddleDragPanning = true;
                cameraController?.SetFollowEnabled(false);
            }

            if (isMiddleDragPanning)
            {
                // 画面上の移動量をワールド座標に変換してカメラへ反映する。
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
                // 動かしていなければ「追従へ戻すクリック」とみなす。
                ReturnCameraFollowToPlayer();
            }
        }

        // ホイール上下で選択タイルを前後に切り替える。
        private void HandleScrollSelection()
        {
            if (Mouse.current == null || tilePalette == null || tilePalette.SelectableEntries.Count == 0) return;

            float scrollY = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) < 0.01f) return;

            tilePalette.SelectEntryByOffset(scrollY > 0f ? -1 : 1);
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
                // UI 上のクリックは編集入力として扱わない。
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
                // 押した瞬間から 1 マスぶんのプレビューを出しておく。
                preview?.ShowRect(dragStartCell, dragCurrentCell, isDestroyDragging, tilePalette?.SelectedEntry);
            }

            if (!isDragging)
            {
                return;
            }

            if (TryGetHoveredCell(out Vector3Int hoveredCell))
            {
                // ドラッグ中は現在位置に合わせてプレビュー矩形を更新し続ける。
                dragCurrentCell = hoveredCell;
                preview?.ShowRect(dragStartCell, dragCurrentCell, isDestroyDragging, tilePalette?.SelectedEntry);
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
            preview?.Hide();
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
            cell = Grid.WorldToCell(worldPosition);
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

            WallPanelDefinition selectedEntry = tilePalette?.SelectedEntry;
            if (selectedEntry == null)
            {
                SetMessage("選択タイルがありません");
                return;
            }

            bool isSpawnTile = selectedEntry != null && selectedEntry.IsSpawn;
            if (!destroyTiles && isSpawnTile)
            {
                // Spawn だけは矩形塗りではなく単点配置に固定する。
                minX = maxX = endCell.x;
                minY = maxY = endCell.y;
            }

            TileBase tileToPaint = selectedEntry.Tile;
            if (!destroyTiles && tileToPaint == null)
            {
                SetMessage("選択タイルがありません");
                return;
            }

            Tilemap targetTilemap = GetTargetTilemap(selectedEntry);
            if (targetTilemap == null)
            {
                SetMessage("対象レイヤーが見つかりません");
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
                    targetTilemap.SetTile(cell, destroyTiles ? null : tileToPaint);
                }
            }

            targetTilemap.CompressBounds();
            RefreshSpawnTileVisibility();
            SetMessage($"{(destroyTiles ? "破壊" : "配置")} [{minX},{minY}] - [{maxX},{maxY}]");
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
            if (!LevelEditModeCsvUtility.TryBuildCsv(CurrentStage, out string csv, out BoundsInt bounds, out string errorMessage))
            {
                SetMessage(errorMessage);
                return;
            }

            GUIUtility.systemCopyBuffer = csv;
            SetMessage($"CSVをコピーしました: {bounds.size.x}x{bounds.size.y}");
        }

        // クリップボードの CSV を読んで Ground タイルを復元する。
        private void ImportFromClipboard()
        {
            string csv = GUIUtility.systemCopyBuffer;
            if (!LevelEditModeCsvUtility.TryImportCsv(CurrentStage, csv, out int importedCount, out string errorMessage))
            {
                SetMessage(errorMessage);
                return;
            }

            RefreshSpawnTileVisibility();
            SetMessage($"CSVを読み込みました: {importedCount} タイル");
        }

        // いま置かれている Spawn タイル位置へプレイヤーを戻す。
        private void RespawnPlayerToSpawn()
        {
            if (playerTransform == null || Grid == null || !TryFindSpawnCell(out Vector3Int spawnCell))
            {
                SetMessage("Spawn が未配置です");
                return;
            }

            PlayerController playerController = playerTransform.GetComponent<PlayerController>();
            if (playerController == null)
            {
                playerController = playerTransform.GetComponentInParent<PlayerController>();
            }

            if (playerController != null)
            {
                playerController.RespawnToSpawn();
            }
            else
            {
                playerTransform.position = Grid.GetCellCenterWorld(spawnCell);
            }

            SetMessage($"Spawn へ戻しました: {spawnCell.x},{spawnCell.y}");
        }

        // 既存の Spawn タイルを全消去して 1 個だけに保つ。
        private void ClearExistingSpawnTiles()
        {
            if (OverlayTilemap == null || TileCatalog == null) return;

            // Spawn は 1 個だけに保つ。
            BoundsInt bounds = OverlayTilemap.cellBounds;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    Vector3Int cell = new(x, y, 0);
                    TileBase tile = OverlayTilemap.GetTile(cell);
                    if (!TileCatalog.TryGetPanelByTile(tile, out WallPanelDefinition entry) || !entry.IsSpawn) continue;
                    OverlayTilemap.SetTile(cell, null);
                }
            }
        }

        // Spawn タイルだけ、編集モード時に見せて通常時は隠す。
        private void RefreshSpawnTileVisibility()
        {
            if (OverlayTilemap == null || TileCatalog == null) return;

            // Spawn はデータとしては常に存在するが、通常プレイ中は視覚ノイズになるので隠す。
            BoundsInt bounds = OverlayTilemap.cellBounds;
            Color tileColor = isUiVisible ? Color.white : new Color(1f, 1f, 1f, 0f);

            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    Vector3Int cell = new(x, y, 0);
                    TileBase tile = OverlayTilemap.GetTile(cell);
                    if (!TileCatalog.TryGetPanelByTile(tile, out WallPanelDefinition entry) || !entry.IsSpawn) continue;

                    OverlayTilemap.SetTileFlags(cell, TileFlags.None);
                    OverlayTilemap.SetColor(cell, tileColor);
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

        // Overlay 上から Spawn タイルのセルを探して返す。
        private bool TryFindSpawnCell(out Vector3Int spawnCell)
        {
            spawnCell = default;
            if (OverlayTilemap == null || TileCatalog == null) return false;

            // Overlay 上に置かれた Spawn タイルそのもののセルを返す。
            BoundsInt bounds = OverlayTilemap.cellBounds;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    Vector3Int tileCell = new(x, y, 0);
                    TileBase tile = OverlayTilemap.GetTile(tileCell);
                    if (!TileCatalog.TryGetPanelByTile(tile, out WallPanelDefinition entry) || !entry.IsSpawn) continue;
                    spawnCell = tileCell;
                    return true;
                }
            }

            return false;
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

        private Tilemap GetTargetTilemap(WallPanelDefinition entry)
        {
            if (entry == null) return null;
            return entry.IsOverlay ? OverlayTilemap : GroundTilemap;
        }

        private void HandleTilePaletteSelectionChanged(WallPanelDefinition _)
        {
            SetMessage(string.Empty);
        }
    }
}
