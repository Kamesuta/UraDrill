using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace VerbGame
{
    [CreateAssetMenu(fileName = "WallPanelDefinition", menuName = "VerbGame/Walls/Wall Panel Definition")]
    public sealed class WallPanelDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private WallPanelLayer layer = WallPanelLayer.Ground;
        [SerializeField] private string label;
        [SerializeField] private TileBase tile;
        [SerializeField] private DrillBehavior drillBehavior = DrillBehavior.Normal;
        [SerializeField] private WallPanelDirection direction = WallPanelDirection.None;
        [SerializeField] private SlipBehavior slipBehavior = SlipBehavior.None;
        [SerializeField] private SurfaceTraversalBehavior surfaceTraversalBehavior = SurfaceTraversalBehavior.None;
        [SerializeField] private bool isGoal;
        [SerializeField] private bool isSpawn;

        public string Id => id;
        public WallPanelLayer Layer => layer;
        public string Label => label;
        public TileBase Tile => tile;
        public DrillBehavior DrillBehavior => drillBehavior;
        public WallPanelDirection Direction => direction;
        public SlipBehavior SlipBehavior => slipBehavior;
        public SurfaceTraversalBehavior SurfaceTraversalBehavior => surfaceTraversalBehavior;
        public bool IsGoal => isGoal;
        public bool IsSpawn => isSpawn;
        public bool CausesSlip => slipBehavior == SlipBehavior.Slip;
        public bool AllowsStepTraversal => surfaceTraversalBehavior == SurfaceTraversalBehavior.Step;
        public bool BouncesDrill => drillBehavior == DrillBehavior.Bounce;
        public bool BouncesBackDrill => BouncesDrill && direction == WallPanelDirection.Rect;
        public bool BouncesDrillByDirection => BouncesDrill && direction != WallPanelDirection.None && direction != WallPanelDirection.Rect;
        public bool IsOverlay => layer == WallPanelLayer.Overlay;
        public bool HasId => !string.IsNullOrWhiteSpace(id);

        // パレットやデバッグ表示で使う簡易キー。
        public string PaletteKey => id;

        public bool TryGetNumericId(out int numericId)
        {
            numericId = 0;
            return !string.IsNullOrWhiteSpace(id) && int.TryParse(id, out numericId);
        }

        public bool Matches(TileBase candidate)
        {
            return tile != null && tile == candidate;
        }
    }
}
