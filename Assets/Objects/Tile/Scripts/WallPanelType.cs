namespace VerbGame
{
    // タイルがどの Tilemap レイヤーへ置かれるかを表す。
    public enum WallPanelLayer
    {
        Ground = 0,
        Overlay = 1,
    }

    public enum WallPanelType
    {
        Spawn = -1,
        Default = 0,
        Panel = 1,
        Ice = 2,
        HardWall = 3,
        Checkpoint = 4,
    }
}
