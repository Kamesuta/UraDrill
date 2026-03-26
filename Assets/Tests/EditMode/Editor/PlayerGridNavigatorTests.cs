using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace VerbGame.Tests
{
    // PlayerGridNavigator の論理移動だけを、CSV ステージ定義から検証する。
    // 1 ケースごとに「地形」「開始位置」「入力列」「期待する最終状態」を書けるようにして、
    // 目視確認していた再現バグをそのまま回帰テストへ落とし込む。
    public sealed class PlayerGridNavigatorTests
    {
        private const string TileCatalogPath = "Assets/Objects/Tile/Data/Palette.asset";

        [TestCaseSource(nameof(MoveScenarios))]
        public void PlayerMoveTest(MoveScenario scenario)
        {
            WallPanelCatalog tileCatalog = AssetDatabase.LoadAssetAtPath<WallPanelCatalog>(TileCatalogPath);
            Assert.That(tileCatalog, Is.Not.Null, $"タイルカタログが見つかりません: {TileCatalogPath}");

            using var context = new TestContext(tileCatalog);

            bool imported = LevelEditModeCsvUtility.TryImportCsv(
                context.GroundTilemap,
                context.OverlayTilemap,
                tileCatalog,
                scenario.StageCsv,
                out int importedCount,
                out string errorMessage);

            Assert.That(imported, Is.True, $"CSV 読み込み失敗: {errorMessage}");
            Assert.That(importedCount, Is.GreaterThan(0), "テストステージにタイルがありません");

            bool hasSpawn = context.Navigator.TryFindSpawnBoundary(out Vector3Int spawnCell, out Vector2Int spawnNormal);
            Assert.That(hasSpawn, Is.True, $"{scenario.Name}: スポーン地点が見つかりません");
            context.Navigator.CommitMove(spawnCell, spawnNormal);

            for (int i = 0; i < scenario.Turns.Length; i++)
            {
                MoveTurn turn = scenario.Turns[i];
                bool moved = ApplyInput(context.Navigator, turn.Input);

                Assert.That(moved, Is.EqualTo(turn.ExpectSuccess), $"{scenario.Name}: {i + 1} 手目 {turn.Input} の成否が不一致です");
                if (!moved)
                {
                    Assert.That(context.Navigator.CurrentCell, Is.EqualTo(turn.ExpectCell), $"{scenario.Name}: {i + 1} 手目失敗時セルが不一致です");
                    Assert.That(context.Navigator.SurfaceNormal, Is.EqualTo(turn.ExpectNormal), $"{scenario.Name}: {i + 1} 手目失敗時法線が不一致です");
                    break;
                }

                ResolveSlipFalls(context.Navigator);
                Assert.That(context.Navigator.CurrentCell, Is.EqualTo(turn.ExpectCell), $"{scenario.Name}: {i + 1} 手目後セルが不一致です");
                Assert.That(context.Navigator.SurfaceNormal, Is.EqualTo(turn.ExpectNormal), $"{scenario.Name}: {i + 1} 手目後法線が不一致です");
            }
        }

        [Test]
        public void IsOutsideFallBounds_BecomesTrueFromTenCellsBelowBound()
        {
            WallPanelCatalog tileCatalog = AssetDatabase.LoadAssetAtPath<WallPanelCatalog>(TileCatalogPath);
            Assert.That(tileCatalog, Is.Not.Null, $"タイルカタログが見つかりません: {TileCatalogPath}");

            using var context = new TestContext(tileCatalog);

            bool imported = LevelEditModeCsvUtility.TryImportCsv(
                context.GroundTilemap,
                context.OverlayTilemap,
                tileCatalog,
                @"
                    0,0,1,1
                    1
                ".Trim(),
                out int importedCount,
                out string errorMessage);

            Assert.That(imported, Is.True, $"CSV 読み込み失敗: {errorMessage}");
            Assert.That(importedCount, Is.GreaterThan(0), "テストステージにタイルがありません");

            Assert.That(context.Navigator.IsOutsideFallBounds(new Vector3Int(0, -9, 0), 10), Is.False, "Bound の 9 マス下ではまだリスポーンしない想定です");
            Assert.That(context.Navigator.IsOutsideFallBounds(new Vector3Int(0, -10, 0), 10), Is.True, "Bound の 10 マス下に出た時点でリスポーンしたいです");
        }

        [Test]
        public void IsOutsideFallBounds_UsesCombinedGroundAndOverlayBounds()
        {
            WallPanelCatalog tileCatalog = AssetDatabase.LoadAssetAtPath<WallPanelCatalog>(TileCatalogPath);
            Assert.That(tileCatalog, Is.Not.Null, $"タイルカタログが見つかりません: {TileCatalogPath}");

            using var context = new TestContext(tileCatalog);

            bool imported = LevelEditModeCsvUtility.TryImportCsv(
                context.GroundTilemap,
                context.OverlayTilemap,
                tileCatalog,
                @"
                    0,0,2,1
                    1,z
                ".Trim(),
                out int importedCount,
                out string errorMessage);

            Assert.That(imported, Is.True, $"CSV 読み込み失敗: {errorMessage}");
            Assert.That(importedCount, Is.GreaterThan(0), "テストステージにタイルがありません");

            // Overlay 上の Spawn もステージ Bounds に含めて、右側の許容量を決める。
            Assert.That(context.Navigator.IsOutsideFallBounds(new Vector3Int(10, 0, 0), 10), Is.False, "Overlay を含む Bound から 9 マス右はまだ許容する想定です");
            Assert.That(context.Navigator.IsOutsideFallBounds(new Vector3Int(11, 0, 0), 10), Is.True, "Overlay を含む Bound から 10 マス右でリスポーンしたいです");
        }

        private static bool ApplyInput(PlayerGridNavigator navigator, StepKey input)
        {
            int direction = input switch
            {
                StepKey.Left => -1,
                StepKey.Right => 1,
                _ => 0,
            };

            if (direction == 0) return false;
            if (!navigator.TryGetNextStep(direction, out Vector3Int nextCell, out Vector2Int nextNormal))
            {
                return false;
            }

            navigator.CommitMove(nextCell, nextNormal);
            return true;
        }

        private static void ResolveSlipFalls(PlayerGridNavigator navigator)
        {
            // 実プレイと同様に、1 手ごとに重力落下が連鎖するぶんまで反映する。
            while (navigator.TryBuildSlipFall(out _, out Vector3Int landingCell, out Vector2Int landingNormal))
            {
                navigator.CommitMove(landingCell, landingNormal);
            }
        }

        private static IEnumerable<MoveScenario> MoveScenarios()
        {
            // ステップ無しの左壁。
            // 横向きでも勝手に90度回転せず、その場で止まることを確認する。
            yield return new MoveScenario(
                name: "LeftWallWithoutStepStops",
                stageCsv: @"
                    -1,0,2,2
                    0,1
                    1,z
                ".Trim(),
                turns: new[]
                {
                    new MoveTurn(
                        input: StepKey.Left,
                        expectSuccess: false,
                        expectCell: new Vector3Int(0, 0, 0),
                        expectNormal: Vector2Int.right),
                });

            // 凹面。
            // 現在セルの Overlay ステップで、床から右壁へ内回りできることを確認する。
            yield return new MoveScenario(
                name: "ConcaveOverlayStepWrapsToRightWall",
                stageCsv: @"
                    -1,-1,3,2
                    z,0e,1
                    1,1,0
                ".Trim(),
                turns: new[]
                {
                    new MoveTurn(
                        input: StepKey.Right,
                        expectSuccess: true,
                        expectCell: new Vector3Int(0, 0, 0),
                        expectNormal: Vector2Int.up),
                    new MoveTurn(
                        input: StepKey.Right,
                        expectSuccess: true,
                        expectCell: new Vector3Int(1, -1, 0),
                        expectNormal: Vector2Int.right),
                });

            // 凸面。
            // 左壁から上の地面へ、支持側ステップで外回りできることを確認する。
            yield return new MoveScenario(
                name: "ConvexSupportStepWrapsToTopGround",
                stageCsv: @"
                    -1,0,2,1
                    1g,z
                ".Trim(),
                turns: new[]
                {
                    new MoveTurn(
                        input: StepKey.Left,
                        expectSuccess: true,
                        expectCell: new Vector3Int(-1, 1, 0),
                        expectNormal: Vector2Int.up),
                });

            // 凹面2
            yield return new MoveScenario(
                name: "ConcaveOverlayStepWrapsToDownWall",
                stageCsv: @"
                    0,0,2,3
                    1,1
                    0f,1
                    0z,1
                ".Trim(),
                turns: new[]
                {
                    new MoveTurn(
                        input: StepKey.Right,
                        expectSuccess: true,
                        expectCell: new Vector3Int(0, 1, 0),
                        expectNormal: Vector2Int.left),
                    new MoveTurn(
                        input: StepKey.Right,
                        expectSuccess: true,
                        expectCell: new Vector3Int(0, 1, 0),
                        expectNormal: Vector2Int.down),
                });

            // 凸面→落下→反対側の壁に移動
            yield return new MoveScenario(
                name: "ConvexOverlayStepFallAndMoveToOppositeWall",
                stageCsv: @"
                    0,0,4,3
                    0z,1,1,1
                    1g,0,0,1
                    0,0,0,1
                ".Trim(),
                turns: new[]
                {
                    new MoveTurn(
                        input: StepKey.Left,
                        expectSuccess: true,
                        expectCell: new Vector3Int(-1, 1, 0),
                        expectNormal: Vector2Int.left),
                    new MoveTurn(
                        input: StepKey.Left,
                        expectSuccess: true,
                        expectCell: new Vector3Int(2, 0, 0),
                        expectNormal: Vector2Int.left),
                    new MoveTurn(
                        input: StepKey.Right,
                        expectSuccess: true,
                        expectCell: new Vector3Int(2, 1, 0),
                        expectNormal: Vector2Int.left),
                    new MoveTurn(
                        input: StepKey.Right,
                        expectSuccess: false,
                        expectCell: new Vector3Int(2, 1, 0),
                        expectNormal: Vector2Int.left),
                    new MoveTurn(
                        input: StepKey.Right,
                        expectSuccess: false,
                        expectCell: new Vector3Int(2, 1, 0),
                        expectNormal: Vector2Int.left),
                });
        }

        private sealed class TestContext : System.IDisposable
        {
            public Tilemap GroundTilemap { get; }
            public Tilemap OverlayTilemap { get; }
            public PlayerGridNavigator Navigator { get; }

            private readonly GameObject rootObject;

            public TestContext(WallPanelCatalog tileCatalog)
            {
                // 実シーンと同じく Grid 配下に Ground / Overlay の Tilemap を持つ。
                rootObject = new GameObject("PlayerGridNavigatorTests_Root");
                Grid grid = rootObject.AddComponent<Grid>();

                GroundTilemap = CreateTilemapChild("Ground");
                OverlayTilemap = CreateTilemapChild("Overlay");
                Navigator = new PlayerGridNavigator(grid, GroundTilemap, OverlayTilemap, tileCatalog);
            }

            public void Dispose()
            {
                if (rootObject != null)
                {
                    Object.DestroyImmediate(rootObject);
                }
            }

            private Tilemap CreateTilemapChild(string name)
            {
                var child = new GameObject(name);
                child.transform.SetParent(rootObject.transform, false);
                Tilemap tilemap = child.AddComponent<Tilemap>();
                child.AddComponent<TilemapRenderer>();
                return tilemap;
            }
        }

        public readonly struct MoveScenario
        {
            public MoveScenario(
                string name,
                string stageCsv,
                MoveTurn[] turns)
            {
                Name = name;
                StageCsv = stageCsv;
                Turns = turns;
            }

            public string Name { get; }
            public string StageCsv { get; }
            public MoveTurn[] Turns { get; }

            public override string ToString() => Name;
        }

        public readonly struct MoveTurn
        {
            public MoveTurn(
                StepKey input,
                bool expectSuccess,
                Vector3Int expectCell,
                Vector2Int expectNormal)
            {
                Input = input;
                ExpectSuccess = expectSuccess;
                ExpectCell = expectCell;
                ExpectNormal = expectNormal;
            }

            public StepKey Input { get; }
            public bool ExpectSuccess { get; }
            public Vector3Int ExpectCell { get; }
            public Vector2Int ExpectNormal { get; }
        }

        public enum StepKey
        {
            Left,
            Right,
        }
    }
}
