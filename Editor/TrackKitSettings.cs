using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TrackKit.EditorTools
{
    /// <summary>
    /// Per-project TrackKit configuration: which road prefabs become kit pieces, physics layers,
    /// wall sizes, the start-grid layout and where output goes. One asset per project, created by
    /// Tools/TrackKit/Settings (or by the project's adapter with game-specific defaults).
    /// </summary>
    public sealed class TrackKitSettings : ScriptableObject
    {
        public const string DefaultPath = "Assets/TrackKit/TrackKitSettings.asset";

        [Serializable]
        public sealed class PieceDef
        {
            public string id = "tk_piece";
            [Tooltip("Road prefab to turn into a kit piece. It must have a non-trigger collider.")]
            public GameObject source;
            public TrackPiece.PieceKind kind = TrackPiece.PieceKind.Straight;
            [Tooltip("Bends only: expected turn in degrees (90, 45...).")]
            public float expectedTurn;
            [Tooltip("Gate markers spread along the piece (the start piece gets its start gate instead).")]
            public int gates = 1;
            [Tooltip("Local travel axis for straights/inclines. Zero = estimate from the mesh.")]
            public Vector3 axisHint = Vector3.forward;
        }

        [Header("Kit pieces")]
        public List<PieceDef> pieces = new List<PieceDef>();

        [Header("Output")]
        public string kitFolder = "Assets/TrackKit/Prefabs";
        public string materialFolder = "Assets/TrackKit/Materials";
        [Tooltip("Prefab Builder catalog the kit is added to. Empty = do not touch Prefab Builder.")]
        public string prefabBuilderCatalogPath = "Assets/PrefabBuilder/Data/PrefabBuilderCatalog.asset";
        public float catalogGrid = 20f;

        [Header("Physics")]
        public int floorLayer = TrackChain.DefaultFloorLayer;
        public string wallTag = "wall";
        public int wallLayer = 0;
        public float joinTolerance = TrackChain.DefaultJoinTolerance;

        [Header("Walls")]
        public float wallThickness = 1.6f;
        public float wallHeight = 4.5f;
        [Tooltip("Distance kept between the road edge and the wall centre.")]
        public float wallInset = 1.1f;
        public Color wallColor = new Color(0.12f, 0.75f, 0.85f, 1f);

        [Header("Start piece")]
        public float startGateFromEntry = 1f;
        [Tooltip("Front edge of the start gate's trigger, measured forward from the gate.")]
        public float gateTriggerFront = 1.5f;
        public float spawnClearance = 3f;
        public float spawnHeight = 0.5f;
        [Tooltip("Start grid, slot 0 first: x = right, y = forward (metres, in the grid's frame).")]
        public Vector2[] spawnSlots =
        {
            new Vector2(5f, 0f), new Vector2(-5f, -2f), new Vector2(5f, -4f), new Vector2(-5f, -6f),
            new Vector2(5f, -8f), new Vector2(-5f, -10f), new Vector2(5f, -12f)
        };

        [Header("Bake")]
        [Tooltip("If set, Bake only runs in scenes under this folder (protects shipped levels).")]
        public string bakeSceneFolder = "";

        static TrackKitSettings cached;

        /// <summary>The project's settings asset, created with defaults if missing.</summary>
        public static TrackKitSettings Get()
        {
            if (cached != null) return cached;
            cached = AssetDatabase.LoadAssetAtPath<TrackKitSettings>(DefaultPath);
            if (cached == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:" + nameof(TrackKitSettings));
                if (guids.Length > 0) cached = AssetDatabase.LoadAssetAtPath<TrackKitSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            if (cached == null)
            {
                cached = CreateInstance<TrackKitSettings>();
                TrackKitAdapters.Active?.ConfigureDefaults(cached);
                TrackKitKitBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(DefaultPath).Replace('\\', '/'));
                AssetDatabase.CreateAsset(cached, DefaultPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[TrackKit] Created " + DefaultPath + (TrackKitAdapters.Active != null ? " with defaults from " + TrackKitAdapters.Active.DisplayName : "") + ".");
            }
            return cached;
        }

        [MenuItem("Tools/TrackKit/Settings", false, 390)]
        static void SelectSettings()
        {
            Selection.activeObject = Get();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }
}
