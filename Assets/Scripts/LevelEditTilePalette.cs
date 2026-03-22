using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace VerbGame
{
    [CreateAssetMenu(fileName = "LevelEditTilePalette", menuName = "VerbGame/Editing/Level Edit Tile Palette")]
    public sealed class LevelEditTilePalette : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public int id;
            public string label;
            public TileBase tile;
        }

        [SerializeField] private Entry[] entries;

        private Dictionary<int, Entry> entryById;
        private Dictionary<TileBase, Entry> entryByTile;

        public IReadOnlyList<Entry> Entries => entries;

        private void OnEnable() => InvalidateLookup();
        private void OnValidate() => InvalidateLookup();

        public bool TryGetEntryById(int id, out Entry entry)
        {
            EnsureLookup();
            return entryById.TryGetValue(id, out entry);
        }

        public bool TryGetEntryByTile(TileBase tile, out Entry entry)
        {
            EnsureLookup();
            if (tile == null)
            {
                entry = null;
                return false;
            }

            return entryByTile.TryGetValue(tile, out entry);
        }

        public TileBase GetTile(int id)
        {
            return TryGetEntryById(id, out Entry entry) ? entry.tile : null;
        }

        private void InvalidateLookup()
        {
            entryById = null;
            entryByTile = null;
        }

        private void EnsureLookup()
        {
            if (entryById != null && entryByTile != null) return;

            entryById = new Dictionary<int, Entry>();
            entryByTile = new Dictionary<TileBase, Entry>();

            if (entries == null) return;

            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry == null || entry.id == 0) continue;

                entryById[entry.id] = entry;
                if (entry.tile != null)
                {
                    entryByTile[entry.tile] = entry;
                }
            }
        }
    }
}
