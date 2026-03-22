using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace VerbGame
{
    [CreateAssetMenu(fileName = "WallPanelCatalog", menuName = "VerbGame/Walls/Wall Panel Catalog")]
    public sealed class WallPanelCatalog : ScriptableObject
    {
        [SerializeField] private WallPanelDefinition defaultPanel;
        [SerializeField] private WallPanelDefinition[] panelDefinitions;

        private Dictionary<TileBase, WallPanelDefinition> tileLookup;

        public WallPanelDefinition DefaultPanel => defaultPanel;
        public IReadOnlyList<WallPanelDefinition> PanelDefinitions => panelDefinitions;

        public WallPanelDefinition GetPanel(TileBase tile)
        {
            if (tile == null) return defaultPanel;

            EnsureLookup();
            return tileLookup.TryGetValue(tile, out WallPanelDefinition definition) ? definition : defaultPanel;
        }

        public WallPanelType GetPanelType(TileBase tile)
        {
            WallPanelDefinition panel = GetPanel(tile);
            return panel != null ? panel.PanelType : WallPanelType.Default;
        }

        private void OnEnable() => tileLookup = null;
        private void OnValidate() => tileLookup = null;

        private void EnsureLookup()
        {
            if (tileLookup != null) return;

            tileLookup = new Dictionary<TileBase, WallPanelDefinition>();
            if (panelDefinitions == null) return;

            foreach (WallPanelDefinition definition in panelDefinitions)
            {
                if (definition == null) continue;

                TileBase[] tiles = definition.Tiles;
                if (tiles == null) continue;

                for (int i = 0; i < tiles.Length; i++)
                {
                    TileBase tile = tiles[i];
                    if (tile == null) continue;
                    tileLookup[tile] = definition;
                }
            }
        }
    }
}
