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
    public sealed class LevelEditModeTilePalette
    {
        // 選択中タイルだけ視覚的に強調するための色。
        private static readonly Color SelectedButtonColor = new(0.86f, 0.77f, 0.28f, 1f);
        // 非選択状態の通常色。
        private static readonly Color DefaultButtonColor = new(0.24f, 0.24f, 0.24f, 0.94f);

        private readonly RectTransform tileListContent;
        private readonly Button tileButtonTemplate;
        private readonly WallPanelCatalog tileCatalog;
        private readonly float tileIconSize;
        private readonly Action<WallPanelDefinition> onSelectionChanged;
        private readonly List<Button> tileButtons = new();
        private readonly List<WallPanelDefinition> selectableEntries = new();

        public LevelEditModeTilePalette(
            RectTransform tileListContent,
            Button tileButtonTemplate,
            WallPanelCatalog tileCatalog,
            float tileIconSize,
            Action<WallPanelDefinition> onSelectionChanged)
        {
            this.tileListContent = tileListContent;
            this.tileButtonTemplate = tileButtonTemplate;
            this.tileCatalog = tileCatalog;
            this.tileIconSize = tileIconSize;
            this.onSelectionChanged = onSelectionChanged;
        }

        // ホイール切り替えで参照する、実際に選択可能なタイル一覧。
        public IReadOnlyList<WallPanelDefinition> SelectableEntries => selectableEntries;
        // 現在選択中のタイル定義。
        public WallPanelDefinition SelectedEntry { get; private set; }

        // パレットの内容からタイル選択ボタンを動的生成する。
        public void Build()
        {
            if (tileButtonTemplate == null || tileListContent == null || tileCatalog == null) return;

            // タイル一覧はテンプレートから横並びで動的生成する。
            ConfigureTileListLayout();
            tileButtonTemplate.gameObject.SetActive(false);
            tileButtons.Clear();
            selectableEntries.Clear();

            IReadOnlyList<WallPanelDefinition> entries = tileCatalog.PanelDefinitions;
            if (entries == null) return;

            for (int i = 0; i < entries.Count; i++)
            {
                WallPanelDefinition entry = entries[i];
                if (entry == null || entry.Id == 0 || entry.Tile == null) continue;

                Button button = UnityEngine.Object.Instantiate(tileButtonTemplate, tileListContent);
                button.gameObject.name = $"TileButton_{entry.Id}";
                button.gameObject.SetActive(true);
                // ボタン押下時はこのエントリを選択状態へ切り替える。
                button.onClick.AddListener(() => SelectEntry(entry));

                ConfigureTileButtonVisual(button, entry);
                HideButtonLabel(button);

                selectableEntries.Add(entry);
                tileButtons.Add(button);
            }

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
            onSelectionChanged?.Invoke(entry);
        }

        // 最初に選ぶタイルを、パレット先頭の有効エントリから決める。
        private void SelectFirstAvailableEntry(IReadOnlyList<WallPanelDefinition> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                WallPanelDefinition entry = entries[i];
                if (entry == null || entry.Id == 0 || entry.Tile == null) continue;
                SelectEntry(entry);
                return;
            }
        }

        // 選択中タイルのボタンだけ色を変えて分かるようにする。
        private void UpdateTileButtonHighlights()
        {
            for (int i = 0; i < tileButtons.Count; i++)
            {
                Button button = tileButtons[i];
                if (button == null) continue;

                bool isSelected = button.name == $"TileButton_{SelectedEntry?.Id}";
                SetButtonColor(button, isSelected ? SelectedButtonColor : DefaultButtonColor);
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
    }
}
