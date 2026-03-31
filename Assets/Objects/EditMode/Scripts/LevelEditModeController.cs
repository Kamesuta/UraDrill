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
        // 編集入力の主モード。
        private enum EditInteractionMode
        {
            Place,
            Camera,
        }

        // 右クリック破壊時の対象レイヤー。
        private enum DestroyLayerMode
        {
            SelectedOnly,
            AllLayers,
        }

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
        [SerializeField] private Button placeModeButton;
        [SerializeField] private Button cameraModeButton;
        [SerializeField] private TMP_Text placeModeIcon;
        [SerializeField] private TMP_Text cameraModeIcon;
        [SerializeField] private TMP_Text messageLabel;
        [SerializeField] private float previewAlpha = 0.35f;
        [SerializeField] private int previewSortingOrder = 10;

        [Header("カメラ操作")]
        [SerializeField] private float minOrthographicSize = 4f;
        [SerializeField] private float maxOrthographicSize = 18f;
        [SerializeField] private float zoomStep = 1.25f;
        [SerializeField] private float cameraDragThresholdSqr = 25f;
        [SerializeField] private float defaultOrthographicSize = 5f;

        [Header("破壊設定")]
        [SerializeField] private DestroyLayerMode destroyLayerMode = DestroyLayerMode.AllLayers;

        // 編集モード全体の表示状態。
        private bool isUiVisible;
        // 左右クリックのドラッグ編集中かどうか。
        private bool isDragging;
        // 現在のドラッグが破壊モードかどうか。
        private bool isDestroyDragging;
        // ドラッグ編集の開始セル。
        private Vector3Int dragStartCell;
        // ドラッグ編集の現在セル。
        private Vector3Int dragCurrentCell;
        // カメラドラッグ開始位置。パン量計算の基準に使う。
        private Vector2 cameraDragStartScreenPosition;
        // パン開始時のカメラ位置。
        private Vector3 cameraPanStartPosition;
        // ドラッグ矩形プレビューの描画担当。
        private LevelEditModePreview preview;
        // クリップボード権限まわりの警告表示。
        private string clipboardWarningMessage;
        // Ctrl による一時カメラモードかどうか。
        private bool isTemporaryCameraMode;
        // アイコンボタンで固定した基本モード。
        private EditInteractionMode selectedMode = EditInteractionMode.Place;
        // カメラドラッグ継続中かどうか。
        private bool isCameraDragging;
        // 現在のカメラドラッグが中クリック起点かどうか。
        private bool isCameraDraggingWithMiddleButton;
        // 今回の押下がクリックではなくドラッグとして成立したかどうか。
        private bool hasCameraDragMoved;
        // 今回の中クリック押し込み中にズームしたかどうか。
        private bool didZoomDuringCurrentMiddleHold;
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

            if (worldCamera != null && worldCamera.orthographic)
            {
                defaultOrthographicSize = worldCamera.orthographicSize;
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
            SetSelectedMode(EditInteractionMode.Place);
            SetUiVisible(false);
            clipboardWarningMessage = string.Empty;
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
            HandleZoomInput();
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
            if (placeModeButton != null) placeModeButton.onClick.AddListener(() => SetSelectedMode(EditInteractionMode.Place));
            if (cameraModeButton != null) cameraModeButton.onClick.AddListener(() => SetSelectedMode(EditInteractionMode.Camera));
        }

        // Ctrl / 中クリック押し込みの一時モードと、パン入力を処理する。
        private void HandleCameraInput()
        {
            if (Mouse.current == null || worldCamera == null || Keyboard.current == null) return;

            // Ctrl または中クリック押し込み中は、一時的にカメラモードへ切り替える。
            bool shouldUseTemporaryCameraMode =
                Keyboard.current.leftCtrlKey.isPressed ||
                Keyboard.current.rightCtrlKey.isPressed ||
                Mouse.current.middleButton.isPressed;

            if (isTemporaryCameraMode != shouldUseTemporaryCameraMode)
            {
                isTemporaryCameraMode = shouldUseTemporaryCameraMode;
                UpdateModeVisuals();
            }

            bool canPan = CurrentMode == EditInteractionMode.Camera || isCameraDragging;
            if (!canPan)
            {
                if (isCameraDragging)
                {
                    isCameraDragging = false;
                }

                return;
            }

            // カメラモード中は左ドラッグでも中ドラッグでも視点を平行移動できる。
            bool middlePressedThisFrame = Mouse.current.middleButton.wasPressedThisFrame;
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                {
                    return;
                }

                isCameraDragging = true;
                isCameraDraggingWithMiddleButton = false;
                hasCameraDragMoved = false;
                cameraDragStartScreenPosition = Mouse.current.position.ReadValue();
                cameraPanStartPosition = worldCamera.transform.position;
                cameraController?.SetFollowEnabled(false);
            }
            else if (middlePressedThisFrame)
            {
                isCameraDragging = true;
                isCameraDraggingWithMiddleButton = true;
                hasCameraDragMoved = false;
                didZoomDuringCurrentMiddleHold = false;
                cameraDragStartScreenPosition = Mouse.current.position.ReadValue();
                cameraPanStartPosition = worldCamera.transform.position;
                cameraController?.SetFollowEnabled(false);
            }

            if (!isCameraDragging)
            {
                return;
            }

            Vector2 currentScreenPosition = Mouse.current.position.ReadValue();
            if ((currentScreenPosition - cameraDragStartScreenPosition).sqrMagnitude >= cameraDragThresholdSqr)
            {
                hasCameraDragMoved = true;
                // 画面上の移動量をワールド座標に変換してカメラへ反映する。
                Vector3 targetCameraPosition = cameraPanStartPosition + GetCameraPanDelta(cameraDragStartScreenPosition, currentScreenPosition);
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

            bool dragReleased = isCameraDraggingWithMiddleButton
                ? Mouse.current.middleButton.wasReleasedThisFrame
                : Mouse.current.leftButton.wasReleasedThisFrame;
            if (!dragReleased)
            {
                return;
            }

            // Ctrl+クリックまたは中クリックのチョン押し時は、プレイヤー追従へ戻す。
            bool shouldReturnToFollow =
                !hasCameraDragMoved &&
                (isTemporaryCameraMode || isCameraDraggingWithMiddleButton) &&
                (!isCameraDraggingWithMiddleButton || !didZoomDuringCurrentMiddleHold);
            if (shouldReturnToFollow)
            {
                ReturnCameraFollowToPlayer();
            }

            isCameraDragging = false;
            isCameraDraggingWithMiddleButton = false;
            hasCameraDragMoved = false;
            didZoomDuringCurrentMiddleHold = false;
        }

        // カメラモード中だけ、ホイール上下で表示倍率を変更する。
        private void HandleZoomInput()
        {
            if (Mouse.current == null || worldCamera == null || !worldCamera.orthographic) return;
            if (CurrentMode != EditInteractionMode.Camera) return;

            float scrollY = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) < 0.01f) return;

            if (Mouse.current.middleButton.isPressed)
            {
                didZoomDuringCurrentMiddleHold = true;
            }

            // ホイール上でズームイン、下でズームアウトにする。
            float zoomDirection = scrollY > 0f ? -1f : 1f;
            float nextSize = worldCamera.orthographicSize + (zoomStep * zoomDirection);
            worldCamera.orthographicSize = Mathf.Clamp(nextSize, minOrthographicSize, maxOrthographicSize);
            SetMessage($"表示倍率: {worldCamera.orthographicSize:0.0}");
        }

        // 配置モード中だけ、ホイール上下で選択タイルを前後に切り替える。
        private void HandleScrollSelection()
        {
            if (Mouse.current == null || tilePalette == null || tilePalette.SelectableEntries.Count == 0) return;
            if (CurrentMode != EditInteractionMode.Place) return;

            float scrollY = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) < 0.01f) return;

            tilePalette.SelectEntryByOffset(scrollY > 0f ? -1 : 1);
        }

        // 左クリックで配置、右クリックで破壊の矩形編集を行う。
        private void HandlePaintingInput()
        {
            if (Mouse.current == null || CurrentMode == EditInteractionMode.Camera)
            {
                if (isDragging)
                {
                    isDragging = false;
                    preview?.Hide();
                }

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
            if (!destroyTiles && targetTilemap == null)
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
                    if (destroyTiles)
                    {
                        DestroyTilesAt(cell, selectedEntry);
                    }
                    else
                    {
                        targetTilemap.SetTile(cell, tileToPaint);
                    }
                }
            }

            GroundTilemap?.CompressBounds();
            OverlayTilemap?.CompressBounds();
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

            if (!visible)
            {
                isCameraDragging = false;
                isCameraDraggingWithMiddleButton = false;
                hasCameraDragMoved = false;
                didZoomDuringCurrentMiddleHold = false;
                isDragging = false;
                isTemporaryCameraMode = false;
            }
            else
            {
                RefreshClipboardAvailabilityWarning();
            }

            UpdateModeVisuals();
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

            // WebGL 実機では systemCopyBuffer だけだと unityroom 上で失敗しやすいため、
            // 実行環境に応じたコピー窓口へ寄せる。
            if (!LevelEditModeClipboardBridge.TryCopyText(csv, (success, copyError) => HandleClipboardCopyResult(success, copyError, bounds)))
            {
                SetMessage("CSV のコピーに失敗しました");
                return;
            }

            // WebGL 実機ではコピー完了が非同期になるため、待機メッセージを出しておく。
            if (LevelEditModeClipboardBridge.UsesAsyncClipboard)
            {
                SetMessage("CSVをコピー中です");
            }
        }

        // クリップボードの CSV を読んで Ground タイルを復元する。
        private void ImportFromClipboard()
        {
            // WebGL 実機では貼り付けが非同期になるため、結果はコールバックで受け取る。
            if (!LevelEditModeClipboardBridge.TryPasteText(HandleClipboardPasteResult))
            {
                SetMessage("CSV の貼り付け開始に失敗しました");
                return;
            }

            // WebGL 実機ではこの直後にまだ結果が返らないため、待機メッセージを出しておく。
            if (LevelEditModeClipboardBridge.UsesAsyncClipboard)
            {
                SetMessage("クリップボードを読み込み中です");
            }
        }

        // クリップボードから受け取った文字列を CSV として復元する。
        private void HandleClipboardPasteResult(bool success, string csv, string errorMessage)
        {
            if (this == null)
            {
                return;
            }

            if (!success)
            {
                SetMessage(BuildClipboardFailureMessage(errorMessage, true));
                return;
            }

            if (!LevelEditModeCsvUtility.TryImportCsv(CurrentStage, csv, out int importedCount, out errorMessage))
            {
                SetMessage(errorMessage);
                return;
            }

            RefreshSpawnTileVisibility();
            SetMessage($"CSVを読み込みました: {importedCount} タイル");
        }

        // クリップボードへのコピー完了結果を受けてメッセージ表示を確定する。
        private void HandleClipboardCopyResult(bool success, string errorMessage, BoundsInt bounds)
        {
            if (this == null)
            {
                return;
            }

            if (!success)
            {
                SetMessage(BuildClipboardFailureMessage(errorMessage, false));
                return;
            }

            SetMessage($"CSVをコピーしました: {bounds.size.x}x{bounds.size.y}");
        }

        // 編集モードを開いた時点で、クリップボード機能が使えない環境なら警告を出す。
        private void RefreshClipboardAvailabilityWarning()
        {
            if (!LevelEditModeClipboardBridge.TryCheckClipboardAvailability(HandleClipboardAvailabilityChecked))
            {
                clipboardWarningMessage = "!!警告!!: クリップボードが無効なためステージはエクスポートできません！せっかく作ったステージを保存するためにも「別タブで開く」で開き直して下さい！";
                SetMessage(string.Empty);
            }
        }

        // クリップボード可用性チェック結果を UI 警告へ反映する。
        private void HandleClipboardAvailabilityChecked(bool available, string reason)
        {
            if (this == null)
            {
                return;
            }

            clipboardWarningMessage = available
                ? string.Empty
                : BuildClipboardWarningMessage(reason);
            SetMessage(string.Empty);
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
            if (worldCamera != null && worldCamera.orthographic)
            {
                worldCamera.orthographicSize = Mathf.Clamp(defaultOrthographicSize, minOrthographicSize, maxOrthographicSize);
            }

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
                string operationGuide = "左:配置 / ホイール:タイル切替 / Ctrl+左 or 中ドラッグ:視点移動 / Ctrl+クリック or 中クリック:追従復帰 / カメラ中ホイール:ズーム / 右:範囲内を全削除 / アイコン:モード切替";
                if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(clipboardWarningMessage))
                {
                    messageLabel.text = operationGuide;
                    return;
                }

                if (string.IsNullOrWhiteSpace(message))
                {
                    messageLabel.text = $"{operationGuide}\n{clipboardWarningMessage}";
                    return;
                }

                if (string.IsNullOrWhiteSpace(clipboardWarningMessage))
                {
                    messageLabel.text = $"{operationGuide}\n{message}";
                    return;
                }

                messageLabel.text = $"{operationGuide}\n{clipboardWarningMessage}\n{message}";
            }
        }

        // ブラウザ由来のエラーコードや生文言を、ユーザー向けの日本語メッセージへ寄せる。
        private static string BuildClipboardFailureMessage(string errorMessage, bool isPaste)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return isPaste ? "CSV の貼り付けに失敗しました" : "CSV のコピーに失敗しました";
            }

            if (ContainsClipboardPolicyBlocked(errorMessage))
            {
                return "UnityRoomの埋め込み表示ではコピー/ペーストが使えません。「別タブで開く」を使ってください";
            }

            if (ContainsClipboardPermissionDenied(errorMessage))
            {
                return "クリップボードが使えません。「別タブで開く」で開き直してください";
            }

            if (errorMessage.Contains("ClipboardUnsupported", System.StringComparison.OrdinalIgnoreCase))
            {
                return "この環境ではコピー/ペーストが使えません。「別タブで開く」で開き直してください";
            }

            return errorMessage;
        }

        // 編集モードを開いた時の常設警告メッセージを作る。
        private static string BuildClipboardWarningMessage(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "!!警告!!: クリップボードが無効なためステージは保存できません！せっかく作ったステージを保存するためにも「別タブで開く」で開き直してください！";
            }

            if (ContainsClipboardPolicyBlocked(reason))
            {
                return "!!警告!!: UnityRoomの埋め込み表示ではコピー/ペーストが使えません！せっかく作ったステージを保存するためにも「別タブで開く」で開き直してください！";
            }

            if (ContainsClipboardPermissionDenied(reason))
            {
                return "!!警告!!: クリップボードが使えません！せっかく作ったステージを保存するためにも「別タブで開く」で開き直してください！";
            }

            if (reason.Contains("ClipboardUnsupported", System.StringComparison.OrdinalIgnoreCase))
            {
                return "!!警告!!: この環境ではコピー/ペーストが使えません！「別タブで開く」で開き直してください！";
            }

            return "!!警告!!: クリップボードが無効なためステージは保存できません！せっかく作ったステージを保存するためにも「別タブで開く」で開き直してください！";
        }

        // 権限ポリシーで API が無効化されている典型文言をまとめて判定する。
        private static bool ContainsClipboardPolicyBlocked(string message)
        {
            return !string.IsNullOrWhiteSpace(message) &&
                   (message.Contains("ClipboardBlockedByPermissionsPolicy", System.StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("PermissionsPolicy", System.StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("permissions policy", System.StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("crbug.com/414348233", System.StringComparison.OrdinalIgnoreCase));
        }

        // 権限拒否の典型文言をまとめて判定する。
        private static bool ContainsClipboardPermissionDenied(string message)
        {
            return !string.IsNullOrWhiteSpace(message) &&
                   (message.Contains("ClipboardPermissionDenied", System.StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("NotAllowedError", System.StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("PermissionDenied", System.StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("denied", System.StringComparison.OrdinalIgnoreCase));
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

        // 固定モードを切り替え、ボタン見た目も同期する。
        private void SetSelectedMode(EditInteractionMode mode)
        {
            selectedMode = mode;
            UpdateModeVisuals();
            SetMessage(mode == EditInteractionMode.Camera ? "カメラモード" : "配置モード");
        }

        // 現在有効なモードを返す。
        private EditInteractionMode CurrentMode => isTemporaryCameraMode ? EditInteractionMode.Camera : selectedMode;

        // モード切替ボタンの色と押下状態を同期する。
        private void UpdateModeVisuals()
        {
            UpdateModeButtonVisual(placeModeButton, placeModeIcon, CurrentMode == EditInteractionMode.Place);
            UpdateModeButtonVisual(cameraModeButton, cameraModeIcon, CurrentMode == EditInteractionMode.Camera);
        }

        // 選択中モードだけ目立つ色にして、UIから状態が読めるようにする。
        private static void UpdateModeButtonVisual(Button button, Graphic iconGraphic, bool isSelected)
        {
            if (button == null)
            {
                return;
            }

            Color backgroundColor = isSelected
                ? new Color(0.86f, 0.77f, 0.28f, 1f)
                : new Color(0.18f, 0.2f, 0.24f, 0.96f);
            Color iconColor = isSelected
                ? new Color(0.1f, 0.11f, 0.14f, 1f)
                : new Color(0.95f, 0.96f, 0.98f, 1f);

            if (button.targetGraphic is Graphic targetGraphic)
            {
                targetGraphic.color = backgroundColor;
            }

            if (iconGraphic != null)
            {
                iconGraphic.color = iconColor;
            }
        }

        // 右ドラッグ破壊は選択レイヤーだけでなく、必要に応じて全レイヤーを消す。
        private void DestroyTilesAt(Vector3Int cell, WallPanelDefinition selectedEntry)
        {
            if (destroyLayerMode == DestroyLayerMode.AllLayers)
            {
                GroundTilemap?.SetTile(cell, null);
                OverlayTilemap?.SetTile(cell, null);
                return;
            }

            Tilemap targetTilemap = GetTargetTilemap(selectedEntry);
            targetTilemap?.SetTile(cell, null);
        }
    }
}
