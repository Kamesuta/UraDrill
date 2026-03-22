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
    public sealed class LevelEditModeController : MonoBehaviour
    {
        private enum EditTool
        {
            Pencil,
            Eraser,
        }

        [Header("Scene References")]
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Grid grid;
        [SerializeField] private Tilemap groundTilemap;
        [SerializeField] private LevelEditTilePalette tilePalette;

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
        [SerializeField] private float tileIconSize = 48f;
        [SerializeField] private float previewAlpha = 0.35f;
        [SerializeField] private int previewSortingOrder = 10;

        private readonly List<Button> tileButtons = new();
        private readonly List<SpriteRenderer> previewRenderers = new();

        private EditTool currentTool = EditTool.Pencil;
        private LevelEditTilePalette.Entry selectedEntry;
        private bool isUiVisible;
        private bool isDragging;
        private Vector3Int dragStartCell;
        private Vector3Int dragCurrentCell;
        private Transform previewRoot;
        private Sprite fallbackPreviewSprite;

        private void Awake()
        {
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
                HidePreview();
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
            if (tileButtonTemplate == null || tileListContent == null || tilePalette == null) return;

            ConfigureTileListLayout();
            tileButtonTemplate.gameObject.SetActive(false);
            tileButtons.Clear();

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

                tileButtons.Add(button);
            }

            if (entries.Count > 0)
            {
                SelectFirstAvailableEntry(entries);
            }
            else
            {
                UpdateCurrentTileLabel();
            }
        }

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
                dragCurrentCell = dragStartCell;
                UpdatePreview(dragStartCell, dragCurrentCell);
            }

            if (!isDragging)
            {
                return;
            }

            if (TryGetHoveredCell(out Vector3Int hoveredCell))
            {
                dragCurrentCell = hoveredCell;
                UpdatePreview(dragStartCell, dragCurrentCell);
            }

            if (!Mouse.current.leftButton.wasReleasedThisFrame)
            {
                return;
            }

            isDragging = false;
            if (!TryGetHoveredCell(out Vector3Int dragEndCell))
            {
                dragEndCell = dragStartCell;
            }

            ApplyRect(dragStartCell, dragEndCell);
            HidePreview();
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

            TileBase tileToPaint = selectedEntry?.tile;
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

        private void SelectEntry(LevelEditTilePalette.Entry entry)
        {
            selectedEntry = entry;
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
            if (groundTilemap == null || tilePalette == null)
            {
                SetMessage("Export failed");
                return;
            }

            BoundsInt bounds = groundTilemap.cellBounds;
            if (bounds.size.x <= 0 || bounds.size.y <= 0)
            {
                SetMessage("Ground empty");
                return;
            }

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
            SetMessage($"Export {bounds.size.x}x{bounds.size.y} CSV");
        }

        private void ImportFromClipboard()
        {
            if (groundTilemap == null || tilePalette == null)
            {
                SetMessage("Import failed");
                return;
            }

            string csv = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(csv))
            {
                SetMessage("Clipboard empty");
                return;
            }

            string[] lines = csv.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                SetMessage("CSV invalid");
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
                SetMessage("CSV header invalid");
                return;
            }

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
            SetMessage($"Import {importedCount} tiles");
        }

        private void UpdateCurrentTileLabel()
        {
            if (currentTileLabel == null) return;

            if (currentTool == EditTool.Eraser)
            {
                currentTileLabel.text = "Tool: Eraser";
                return;
            }

            string tileName = selectedEntry != null ? BuildEntryLabel(selectedEntry) : "None";
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

                bool isSelected = button.name == $"TileButton_{selectedEntry?.id}";
                SetButtonColor(button, isSelected ? new Color(0.86f, 0.77f, 0.28f, 1f) : new Color(0.24f, 0.24f, 0.24f, 0.94f));
            }
        }

        private void ConfigureTileListLayout()
        {
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

        private void ConfigureTileButtonVisual(Button button, LevelEditTilePalette.Entry entry)
        {
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

        private static Sprite GetTileSprite(TileBase tileBase)
        {
            return tileBase is Tile tile ? tile.sprite : null;
        }

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
            int requiredCount = (maxX - minX + 1) * (maxY - minY + 1);

            EnsurePreviewPool(requiredCount);

            Sprite previewSprite = currentTool == EditTool.Pencil
                ? GetTileSprite(selectedEntry?.tile) ?? GetFallbackPreviewSprite()
                : GetFallbackPreviewSprite();

            Color previewColor = currentTool == EditTool.Pencil
                ? new Color(1f, 1f, 1f, previewAlpha)
                : new Color(1f, 0.35f, 0.35f, previewAlpha);

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

        private void EnsurePreviewPool(int requiredCount)
        {
            if (previewRoot == null)
            {
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

        private static string BuildEntryLabel(LevelEditTilePalette.Entry entry)
        {
            if (entry == null) return "None";

            string label = string.IsNullOrWhiteSpace(entry.label) ? $"ID {entry.id}" : entry.label.Trim();
            return $"{entry.id}: {label}";
        }

        private static string[] SplitCsvLine(string line)
        {
            return line.Split(',', StringSplitOptions.None);
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
