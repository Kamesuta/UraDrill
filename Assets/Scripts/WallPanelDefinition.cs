using UnityEngine;
using UnityEngine.Tilemaps;

namespace VerbGame
{
    [CreateAssetMenu(fileName = "WallPanelDefinition", menuName = "VerbGame/Walls/Wall Panel Definition")]
    public sealed class WallPanelDefinition : ScriptableObject
    {
        [SerializeField] private WallPanelType panelType = WallPanelType.Default;
        [SerializeField] private TileBase[] tiles;

        public WallPanelType PanelType => panelType;
        public TileBase[] Tiles => tiles;

        public bool Contains(TileBase tile)
        {
            if (tile == null || tiles == null) return false;

            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == tile) return true;
            }

            return false;
        }
    }
}
