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
        private Dictionary<int, WallPanelDefinition> idLookup;
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

        public bool TryGetPanelById(int id, out WallPanelDefinition definition)
        {
            EnsureLookup();
            return idLookup.TryGetValue(id, out definition);
        }

        public TileBase GetTile(int id)
        {
            return TryGetPanelById(id, out WallPanelDefinition definition) ? definition.Tile : null;
        }

        private void OnEnable() => InvalidateLookup();
        private void OnValidate() => InvalidateLookup();

        private void InvalidateLookup()
        {
            tileLookup = null;
            idLookup = null;
            definitions = null;
        }

        private void EnsureLookup()
        {
            if (tileLookup != null && idLookup != null && definitions != null) return;

            tileLookup = new Dictionary<TileBase, WallPanelDefinition>();
            idLookup = new Dictionary<int, WallPanelDefinition>();
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

            if (definition.Id != 0)
            {
                idLookup[definition.Id] = definition;
            }
        }
    }
}
