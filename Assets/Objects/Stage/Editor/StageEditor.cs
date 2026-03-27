#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VerbGame.Editor
{
    // Stage の Inspector に CSV 入出力ボタンを足す。
    [CustomEditor(typeof(Stage))]
    public sealed class StageEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("CSV", EditorStyles.boldLabel);

            Stage stage = (Stage)target;
            using (new EditorGUI.DisabledScope(!HasValidReferences(stage)))
            {
                if (GUILayout.Button("CSVをコピー"))
                {
                    CopyCsvToClipboard(stage);
                }

                if (GUILayout.Button("CSVを貼り付け"))
                {
                    PasteCsvFromClipboard(stage);
                }
            }

            if (!HasValidReferences(stage))
            {
                EditorGUILayout.HelpBox("Grid / Ground Tilemap / Overlay Tilemap / Wall Panel Catalog を設定してください。", MessageType.Info);
            }
        }

        private static bool HasValidReferences(Stage stage)
        {
            return stage != null &&
                   stage.Grid != null &&
                   stage.GroundTilemap != null &&
                   stage.OverlayTilemap != null &&
                   stage.WallPanelCatalog != null;
        }

        private static void CopyCsvToClipboard(Stage stage)
        {
            if (!LevelEditModeCsvUtility.TryBuildCsv(stage, out string csv, out BoundsInt bounds, out string errorMessage))
            {
                EditorUtility.DisplayDialog("CSVコピー", errorMessage, "OK");
                return;
            }

            GUIUtility.systemCopyBuffer = csv;
            EditorUtility.DisplayDialog("CSVコピー", $"CSV をクリップボードへコピーしました: {bounds.size.x}x{bounds.size.y}", "OK");
        }

        private static void PasteCsvFromClipboard(Stage stage)
        {
            string csv = GUIUtility.systemCopyBuffer;

            // 取り込みは Tilemap を直接書き換えるので、Undo と Dirty を先に積んでおく。
            Undo.RegisterCompleteObjectUndo(new Object[] { stage.GroundTilemap, stage.OverlayTilemap }, "Import Stage CSV");
            if (!LevelEditModeCsvUtility.TryImportCsv(stage, csv, out int importedCount, out string errorMessage))
            {
                EditorUtility.DisplayDialog("CSV貼り付け", errorMessage, "OK");
                return;
            }

            stage.CompressTilemapBounds();
            EditorUtility.SetDirty(stage.GroundTilemap);
            EditorUtility.SetDirty(stage.OverlayTilemap);
            if (stage.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(stage.gameObject.scene);
            }

            SceneView.RepaintAll();
            EditorUtility.DisplayDialog("CSV貼り付け", $"CSV をクリップボードから読み込みました: {importedCount} タイル", "OK");
        }
    }
}
#endif
