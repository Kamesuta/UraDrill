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
        private Dictionary<int, WallPanelDefinition> numericIdLookup;
        private Dictionary<string, WallPanelDefinition> stringIdLookup;
        private List<WallPanelDefinition> definitions;

        public WallPanelDefinition DefaultPanel => defaultPanel;
        public IReadOnlyList<WallPanelDefinition> PanelDefinitions
        {
            get
            {
                EnsureLookup();
                return definitions;
            }
        }

        public WallPanelDefinition GetPanel(TileBase tile)
        {
            if (tile == null) return null;

            EnsureLookup();
            return tileLookup.TryGetValue(tile, out WallPanelDefinition definition) ? definition : defaultPanel;
        }

        public bool TryGetPanelByTile(TileBase tile, out WallPanelDefinition definition)
        {
            definition = GetPanel(tile);
            return definition != null;
        }

        public bool TryGetPanelByGroundId(int id, out WallPanelDefinition definition)
        {
            EnsureLookup();
            return numericIdLookup.TryGetValue(id, out definition);
        }

        public bool TryGetPanelByOverlayId(string overlayId, out WallPanelDefinition definition)
        {
            EnsureLookup();
            if (string.IsNullOrWhiteSpace(overlayId))
            {
                definition = null;
                return false;
            }

            return stringIdLookup.TryGetValue(overlayId, out definition);
        }

        public TileBase GetGroundTile(int id)
        {
            return TryGetPanelByGroundId(id, out WallPanelDefinition definition) ? definition.Tile : null;
        }

        public TileBase GetOverlayTile(string overlayId)
        {
            return TryGetPanelByOverlayId(overlayId, out WallPanelDefinition definition) ? definition.Tile : null;
        }

        private void OnEnable() => InvalidateLookup();
        private void OnValidate() => InvalidateLookup();

        private void InvalidateLookup()
        {
            tileLookup = null;
            numericIdLookup = null;
            stringIdLookup = null;
            definitions = null;
        }

        private void EnsureLookup()
        {
            if (tileLookup != null && numericIdLookup != null && stringIdLookup != null && definitions != null) return;

            tileLookup = new Dictionary<TileBase, WallPanelDefinition>();
            numericIdLookup = new Dictionary<int, WallPanelDefinition>();
            stringIdLookup = new Dictionary<string, WallPanelDefinition>();
            definitions = new List<WallPanelDefinition>();

            AddDefinition(defaultPanel);
            if (panelDefinitions == null) return;

            foreach (WallPanelDefinition definition in panelDefinitions)
            {
                AddDefinition(definition);
            }
        }

        private void AddDefinition(WallPanelDefinition definition)
        {
            if (definition == null || definitions.Contains(definition)) return;

            definitions.Add(definition);

            if (definition.Tile != null)
            {
                tileLookup[definition.Tile] = definition;
            }

            if (definition.TryGetNumericId(out int numericId))
            {
                numericIdLookup[numericId] = definition;
            }

            if (definition.HasId)
            {
                stringIdLookup[definition.Id] = definition;
            }
        }
    }
}
