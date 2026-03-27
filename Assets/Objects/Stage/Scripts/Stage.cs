using UnityEngine;
using UnityEngine.Tilemaps;

namespace VerbGame
{
    // ステージ全体で共有する Grid / Tilemap / Catalog の参照置き場。
    // Player や Editor 拡張はこのコンポーネント経由でステージ情報へ触る。
    [RequireComponent(typeof(Grid))]
    public sealed class Stage : MonoBehaviour
    {
        // シーン内の Stage をまとめて引く窓口。
        // 参照先の探索ロジックを各所へ散らさないための、ゆるいシングルトン。
        private static Stage instance;

        // セル座標とワールド座標の相互変換に使う親 Grid。
        [Header("Grid")]
        [SerializeField] private Grid grid;
        // 通常地形レイヤー。
        [SerializeField] private Tilemap groundTilemap;
        // Spawn や Goal などの特殊タイル用レイヤー。
        [SerializeField] private Tilemap overlayTilemap;
        // タイルとパネル定義の対応表。
        [SerializeField] private WallPanelCatalog wallPanelCatalog;

        public static Stage Instance
        {
            get
            {
                // 未解決時だけシーンから拾ってキャッシュする。
                if (instance == null)
                {
                    instance = FindFirstObjectByType<Stage>();
                }

                return instance;
            }
        }

        public Grid Grid => grid;
        public Tilemap GroundTilemap => groundTilemap;
        public Tilemap OverlayTilemap => overlayTilemap;
        public WallPanelCatalog WallPanelCatalog => wallPanelCatalog;

        private void Awake()
        {
            RegisterAsInstance();
        }

        private void OnEnable()
        {
            RegisterAsInstance();
        }

        private void Start()
        {
            // シーン読み込み直後は Tilemap.cellBounds が過去編集分で広がっていることがある。
            // 初回リスポーンや落下判定が古い Bounds を見ないよう、開始時に圧縮して揃える。
            CompressTilemapBounds();
        }

        private void OnDisable()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 同じ GameObject に付いている Grid は自明なので、
            // Inspector 設定漏れを減らすためだけ自動で差し込む。
            if (grid == null)
            {
                grid = GetComponent<Grid>();
            }
        }
#endif

        // シーン開始時や Editor 取り込み後に、過去編集で広がった bounds を締め直す。
        public void CompressTilemapBounds()
        {
            groundTilemap?.CompressBounds();
            overlayTilemap?.CompressBounds();
        }

        private void RegisterAsInstance()
        {
            // 先に確定済みの Stage がいなければ自分を採用する。
            if (instance == null || instance == this)
            {
                instance = this;
            }
        }
    }
}
