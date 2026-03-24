using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Tile = UnityEngine.Tilemaps.Tile;

namespace VerbGame
{
    // タイル選択 UI の構築と選択状態だけを担当する。
    public sealed class LevelEditModeTilePalette : MonoBehaviour
    {
        // 選択中タイルだけ視覚的に強調するための色。
        private static readonly Color SelectedButtonColor = new(0.86f, 0.77f, 0.28f, 1f);
        // 非選択状態の通常色。
        private static readonly Color DefaultButtonColor = new(0.24f, 0.24f, 0.24f, 0.94f);

        [SerializeField] private WallPanelCatalog tileCatalog;
        [SerializeField] private float tileIconSize = 48f;

        private readonly List<TileButtonBinding> tileButtons = new();
        private readonly List<WallPanelDefinition> selectableEntries = new();

        // Ground / Overlay を別行へ分けるためのコンテナ。
        private RectTransform tileListContent;
        private RectTransform groundRow;
        private RectTransform overlayRow;
        private Button groundTileButtonTemplate;
        private Button overlayTileButtonTemplate;

        // ホイール切り替えで参照する、実際に選択可能なタイル一覧。
        public IReadOnlyList<WallPanelDefinition> SelectableEntries => selectableEntries;
        // 現在選択中のタイル定義。
        public WallPanelDefinition SelectedEntry { get; private set; }
        public WallPanelCatalog TileCatalog => tileCatalog;
        public event Action<WallPanelDefinition> SelectionChanged;

        private void Awake()
        {
            tileListContent = transform as RectTransform;
        }

        // パレットの内容からタイル選択ボタンを動的生成する。
        public void Build()
        {
            if (tileListContent == null || tileCatalog == null) return;

            // 2 段分の行とテンプレートはプレハブ側で用意済みなので、
            // ここでは参照を拾って既存の動的生成物だけを掃除する。
            if (!TryResolveRowReferences()) return;
            DestroyGeneratedButtons();

            groundTileButtonTemplate.gameObject.SetActive(false);
            overlayTileButtonTemplate.gameObject.SetActive(false);
            tileButtons.Clear();
            selectableEntries.Clear();

            IReadOnlyList<WallPanelDefinition> entries = tileCatalog.PanelDefinitions;
            if (entries == null) return;

            // 1 段目と 2 段目の順序が崩れないよう、
            // Ground -> Overlay の順で走査して配置する。
            BuildRow(entries, WallPanelLayer.Ground, groundRow, groundTileButtonTemplate);
            BuildRow(entries, WallPanelLayer.Overlay, overlayRow, overlayTileButtonTemplate);
            SelectFirstAvailableEntry(entries);
        }

        // ホイール操作で選択タイルを前後に送る。
        public void SelectEntryByOffset(int offset)
        {
            if (selectableEntries.Count == 0) return;

            int currentIndex = SelectedEntry != null ? selectableEntries.IndexOf(SelectedEntry) : -1;
            if (currentIndex < 0)
            {
                SelectEntry(selectableEntries[0]);
                return;
            }

            int nextIndex = (currentIndex + offset + selectableEntries.Count) % selectableEntries.Count;
            SelectEntry(selectableEntries[nextIndex]);
        }

        // 現在選択中のタイルを差し替え、UI 表示も同期する。
        private void SelectEntry(WallPanelDefinition entry)
        {
            SelectedEntry = entry;
            UpdateTileButtonHighlights();
            SelectionChanged?.Invoke(entry);
        }

        // 最初に選ぶタイルを、パレット先頭の有効エントリから決める。
        private void SelectFirstAvailableEntry(IReadOnlyList<WallPanelDefinition> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                WallPanelDefinition entry = entries[i];
                if (!IsSelectable(entry)) continue;
                SelectEntry(entry);
                return;
            }
        }

        // 選択中タイルのボタンだけ色を変えて分かるようにする。
        private void UpdateTileButtonHighlights()
        {
            for (int i = 0; i < tileButtons.Count; i++)
            {
                TileButtonBinding binding = tileButtons[i];
                if (binding.Button == null) continue;

                bool isSelected = binding.Entry == SelectedEntry;
                SetButtonColor(binding.Button, isSelected ? SelectedButtonColor : DefaultButtonColor);
            }
        }

        private bool TryResolveRowReferences()
        {
            groundRow = tileListContent.Find("Ground") as RectTransform;
            overlayRow = tileListContent.Find("Overlay") as RectTransform;
            groundTileButtonTemplate = groundRow != null ? groundRow.Find("TileButtonTemplate")?.GetComponent<Button>() : null;
            overlayTileButtonTemplate = overlayRow != null ? overlayRow.Find("TileButtonTemplate")?.GetComponent<Button>() : null;

            return groundRow != null &&
                   overlayRow != null &&
                   groundTileButtonTemplate != null &&
                   overlayTileButtonTemplate != null;
        }

        private void BuildRow(IReadOnlyList<WallPanelDefinition> entries, WallPanelLayer layer, RectTransform row, Button buttonTemplate)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                WallPanelDefinition entry = entries[i];
                if (!IsSelectable(entry) || entry.Layer != layer) continue;

                Button button = UnityEngine.Object.Instantiate(buttonTemplate, row);
                button.gameObject.name = $"TileButton_{entry.PaletteKey}";
                button.gameObject.SetActive(true);
                // ボタン押下時はこのエントリを選択状態へ切り替える。
                button.onClick.AddListener(() => SelectEntry(entry));

                ConfigureTileButtonVisual(button, entry);
                HideButtonLabel(button);

                selectableEntries.Add(entry);
                tileButtons.Add(new TileButtonBinding(entry, button));
            }
        }

        private void DestroyGeneratedButtons()
        {
            if (tileListContent == null) return;

            Button[] buttons = tileListContent.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null ||
                    button == groundTileButtonTemplate ||
                    button == overlayTileButtonTemplate)
                {
                    continue;
                }

                Destroy(button.gameObject);
            }
        }

        private static bool IsSelectable(WallPanelDefinition entry)
        {
            if (entry == null || entry.Tile == null) return false;
            return entry.HasId;
        }

        // タイル選択ボタンにスプライト見た目を設定する。
        private void ConfigureTileButtonVisual(Button button, WallPanelDefinition entry)
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
            Sprite sprite = GetTileSprite(entry.Tile);
            iconImage.sprite = sprite;
            iconImage.preserveAspect = true;
            iconImage.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
            iconImage.raycastTarget = false;
        }

        // テンプレートのラベルは今回は使わないので隠す。
        private static void HideButtonLabel(Button button)
        {
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.gameObject.SetActive(false);
            }
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

        // ボタン背景色を安全に差し替える共通処理。
        private static void SetButtonColor(Button button, Color color)
        {
            if (button == null) return;

            if (button.targetGraphic is Image image)
            {
                image.color = color;
            }
        }

        private readonly struct TileButtonBinding
        {
            public TileButtonBinding(WallPanelDefinition entry, Button button)
            {
                Entry = entry;
                Button = button;
            }

            public WallPanelDefinition Entry { get; }
            public Button Button { get; }
        }
    }
}
