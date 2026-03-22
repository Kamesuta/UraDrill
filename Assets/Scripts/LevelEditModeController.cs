using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace VerbGame
{
    public sealed class LevelEditModeController : MonoBehaviour
    {
        private enum EditTool
        {
            Pencil,
            Eraser,
        }

        [Serializable]
        private sealed class CellRecord
        {
            public int x;
            public int y;
            public string panelName;
        }

        [Serializable]
        private sealed class TilemapSnapshot
        {
            public int version = 1;
            public List<CellRecord> cells = new();
        }

        [Header("Scene References")]
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Grid grid;
        [SerializeField] private Tilemap groundTilemap;
        [SerializeField] private WallPanelCatalog wallPanelCatalog;

        [Header("UI")]
        [SerializeField] private GameObject uiRoot;
        [SerializeField] private RectTransform tileListContent;
        [SerializeField] private Button tileButtonTemplate;
        [SerializeField] private Button pencilButton;
        [SerializeField] private Button eraserButton;
        [SerializeField] private Button exportButton;
        [SerializeField] private Button importButton;
        [SerializeField] private TMP_Text currentTileLabel;
        [SerializeField] private TMP_Text messageLabel;

        private readonly List<Button> tileButtons = new();
        private readonly Dictionary<string, WallPanelDefinition> panelByName = new(StringComparer.Ordinal);

        private EditTool currentTool = EditTool.Pencil;
        private WallPanelDefinition selectedPanel;
        private bool isUiVisible;
        private bool isDragging;
        private Vector3Int dragStartCell;

        private void Awake()
        {
            RebuildPanelLookup();
            BindButtons();
            BuildTileButtons();
            SetUiVisible(false);
            SetTool(EditTool.Pencil);
            SetMessage("Tab で編集UI");
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                SetUiVisible(!isUiVisible);
            }

            if (!isUiVisible || Mouse.current == null || worldCamera == null || grid == null || groundTilemap == null)
            {
                return;
            }

            HandlePaintingInput();
        }

        private void BindButtons()
        {
            if (pencilButton != null) pencilButton.onClick.AddListener(() => SetTool(EditTool.Pencil));
            if (eraserButton != null) eraserButton.onClick.AddListener(() => SetTool(EditTool.Eraser));
            if (exportButton != null) exportButton.onClick.AddListener(ExportToClipboard);
            if (importButton != null) importButton.onClick.AddListener(ImportFromClipboard);
        }

        private void BuildTileButtons()
        {
            if (tileButtonTemplate == null || tileListContent == null || wallPanelCatalog == null) return;

            tileButtonTemplate.gameObject.SetActive(false);
            tileButtons.Clear();

            IReadOnlyList<WallPanelDefinition> definitions = wallPanelCatalog.PanelDefinitions;
            if (definitions == null) return;

            for (int i = 0; i < definitions.Count; i++)
            {
                WallPanelDefinition definition = definitions[i];
                if (definition == null) continue;

                Button button = Instantiate(tileButtonTemplate, tileListContent);
                button.gameObject.name = $"TileButton_{definition.name}";
                button.gameObject.SetActive(true);
                button.onClick.AddListener(() => SelectPanel(definition));

                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = definition.name;
                }

                tileButtons.Add(button);
            }

            if (definitions.Count > 0)
            {
                SelectFirstAvailablePanel(definitions);
            }
            else
            {
                UpdateCurrentTileLabel();
            }
        }

        private void SelectFirstAvailablePanel(IReadOnlyList<WallPanelDefinition> definitions)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] == null) continue;
                SelectPanel(definitions[i]);
                return;
            }
        }

        private void RebuildPanelLookup()
        {
            panelByName.Clear();
            if (wallPanelCatalog == null || wallPanelCatalog.PanelDefinitions == null) return;

            foreach (WallPanelDefinition definition in wallPanelCatalog.PanelDefinitions)
            {
                if (definition == null || string.IsNullOrEmpty(definition.name)) continue;
                panelByName[definition.name] = definition;
            }
        }

        private void HandlePaintingInput()
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
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
            }

            if (!isDragging || !Mouse.current.leftButton.wasReleasedThisFrame)
            {
                return;
            }

            isDragging = false;
            if (!TryGetHoveredCell(out Vector3Int dragEndCell))
            {
                dragEndCell = dragStartCell;
            }

            ApplyRect(dragStartCell, dragEndCell);
        }

        private bool TryGetHoveredCell(out Vector3Int cell)
        {
            cell = default;
            Vector2 screenPosition = Mouse.current.position.ReadValue();
            Ray ray = worldCamera.ScreenPointToRay(screenPosition);
            float distance = -ray.origin.z / ray.direction.z;
            if (distance < 0f) return false;

            Vector3 worldPosition = ray.GetPoint(distance);
            cell = grid.WorldToCell(worldPosition);
            cell.z = 0;
            return true;
        }

        private void ApplyRect(Vector3Int startCell, Vector3Int endCell)
        {
            int minX = Mathf.Min(startCell.x, endCell.x);
            int maxX = Mathf.Max(startCell.x, endCell.x);
            int minY = Mathf.Min(startCell.y, endCell.y);
            int maxY = Mathf.Max(startCell.y, endCell.y);

            TileBase tileToPaint = GetSelectedTile();
            if (currentTool == EditTool.Pencil && tileToPaint == null)
            {
                SetMessage("選択タイルなし");
                return;
            }

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector3Int cell = new(x, y, 0);
                    groundTilemap.SetTile(cell, currentTool == EditTool.Pencil ? tileToPaint : null);
                }
            }

            groundTilemap.CompressBounds();
            SetMessage($"{currentTool} [{minX},{minY}] - [{maxX},{maxY}]");
        }

        private void SelectPanel(WallPanelDefinition definition)
        {
            selectedPanel = definition;
            UpdateCurrentTileLabel();
            UpdateTileButtonHighlights();
        }

        private void SetTool(EditTool tool)
        {
            currentTool = tool;
            UpdateToolButtonState();
            UpdateCurrentTileLabel();
        }

        private void SetUiVisible(bool visible)
        {
            isUiVisible = visible;
            if (uiRoot != null)
            {
                uiRoot.SetActive(visible);
            }
        }

        private void ExportToClipboard()
        {
            if (groundTilemap == null || wallPanelCatalog == null)
            {
                SetMessage("Export failed");
                return;
            }

            TilemapSnapshot snapshot = new();
            BoundsInt bounds = groundTilemap.cellBounds;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    Vector3Int cell = new(x, y, 0);
                    TileBase tile = groundTilemap.GetTile(cell);
                    if (tile == null) continue;

                    WallPanelDefinition definition = wallPanelCatalog.GetPanel(tile);
                    if (definition == null || string.IsNullOrEmpty(definition.name)) continue;

                    snapshot.cells.Add(new CellRecord
                    {
                        x = x,
                        y = y,
                        panelName = definition.name,
                    });
                }
            }

            GUIUtility.systemCopyBuffer = JsonUtility.ToJson(snapshot, true);
            SetMessage($"Export {snapshot.cells.Count} cells");
        }

        private void ImportFromClipboard()
        {
            if (groundTilemap == null || wallPanelCatalog == null)
            {
                SetMessage("Import failed");
                return;
            }

            string json = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(json))
            {
                SetMessage("Clipboard empty");
                return;
            }

            TilemapSnapshot snapshot;
            try
            {
                snapshot = JsonUtility.FromJson<TilemapSnapshot>(json);
            }
            catch (Exception)
            {
                SetMessage("Import parse failed");
                return;
            }

            if (snapshot == null || snapshot.cells == null)
            {
                SetMessage("Import data invalid");
                return;
            }

            groundTilemap.ClearAllTiles();

            int importedCount = 0;
            for (int i = 0; i < snapshot.cells.Count; i++)
            {
                CellRecord cell = snapshot.cells[i];
                if (cell == null || string.IsNullOrEmpty(cell.panelName)) continue;
                if (!panelByName.TryGetValue(cell.panelName, out WallPanelDefinition definition)) continue;

                TileBase tile = GetRepresentativeTile(definition);
                if (tile == null) continue;

                groundTilemap.SetTile(new Vector3Int(cell.x, cell.y, 0), tile);
                importedCount++;
            }

            groundTilemap.CompressBounds();
            SetMessage($"Import {importedCount} cells");
        }

        private TileBase GetSelectedTile() => GetRepresentativeTile(selectedPanel);

        private static TileBase GetRepresentativeTile(WallPanelDefinition definition)
        {
            if (definition == null || definition.Tiles == null || definition.Tiles.Length == 0) return null;
            return definition.Tiles[0];
        }

        private void UpdateCurrentTileLabel()
        {
            if (currentTileLabel == null) return;

            if (currentTool == EditTool.Eraser)
            {
                currentTileLabel.text = "Tool: Eraser";
                return;
            }

            string tileName = selectedPanel != null ? selectedPanel.name : "None";
            currentTileLabel.text = $"Tool: Pencil / Tile: {tileName}";
        }

        private void UpdateToolButtonState()
        {
            SetButtonColor(pencilButton, currentTool == EditTool.Pencil ? new Color(0.28f, 0.55f, 0.82f, 1f) : new Color(0.18f, 0.18f, 0.18f, 0.95f));
            SetButtonColor(eraserButton, currentTool == EditTool.Eraser ? new Color(0.82f, 0.38f, 0.28f, 1f) : new Color(0.18f, 0.18f, 0.18f, 0.95f));
        }

        private void UpdateTileButtonHighlights()
        {
            for (int i = 0; i < tileButtons.Count; i++)
            {
                Button button = tileButtons[i];
                if (button == null) continue;

                bool isSelected = button.name == $"TileButton_{selectedPanel?.name}";
                SetButtonColor(button, isSelected ? new Color(0.86f, 0.77f, 0.28f, 1f) : new Color(0.24f, 0.24f, 0.24f, 0.94f));
            }
        }

        private void SetButtonColor(Button button, Color color)
        {
            if (button == null) return;
            Image image = button.targetGraphic as Image;
            if (image != null)
            {
                image.color = color;
            }
        }

        private void SetMessage(string message)
        {
            if (messageLabel != null)
            {
                messageLabel.text = message;
            }
        }
    }
}
