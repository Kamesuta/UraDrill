using UnityEngine;
using UnityEngine.Tilemaps;

namespace VerbGame
{
    [CreateAssetMenu(fileName = "WallPanelDefinition", menuName = "VerbGame/Walls/Wall Panel Definition")]
    public sealed class WallPanelDefinition : ScriptableObject
    {
        [SerializeField] private int id;
        [SerializeField] private string label;
        [SerializeField] private TileBase tile;
        [SerializeField] private DrillBehavior drillBehavior = DrillBehavior.Normal;
        [SerializeField] private WallPanelDirection direction = WallPanelDirection.None;
        [SerializeField] private SlipBehavior slipBehavior = SlipBehavior.None;
        [SerializeField] private bool isGoal;
        [SerializeField] private bool isSpawn;

        public int Id => id;
        public string Label => label;
        public TileBase Tile => tile;
        public DrillBehavior DrillBehavior => drillBehavior;
        public WallPanelDirection Direction => direction;
        public SlipBehavior SlipBehavior => slipBehavior;
        public bool IsGoal => isGoal;
        public bool IsSpawn => isSpawn;
        public bool CausesSlip => slipBehavior == SlipBehavior.Slip;
        public bool BouncesDrill => drillBehavior == DrillBehavior.Bounce;
        public bool BouncesBackDrill => BouncesDrill && direction == WallPanelDirection.Rect;
        public bool BouncesDrillByDirection => BouncesDrill && direction != WallPanelDirection.None && direction != WallPanelDirection.Rect;

        public bool Matches(TileBase candidate)
        {
            return tile != null && tile == candidate;
        }
    }
}
