using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrackKit.EditorTools
{
    /// <summary>Everything Bake has worked out, handed to the game's adapter.</summary>
    public sealed class TrackBakeContext
    {
        public Scene scene;
        public TrackKitSettings settings;
        public GameObject layoutRoot;              // PREFAB_LAYOUT
        public List<TrackChain.Link> chain;        // pieces in driving order
        public List<TrackChain.GateSource> gates;  // gate markers in driving order, start gate first
        public List<TrackSpawnMarker> spawns;      // start grid, slot order
    }

    /// <summary>A button the adapter adds to the TrackKit window.</summary>
    public sealed class TrackKitAction
    {
        public string label;
        public Action run;
        public TrackKitAction(string label, Action run) { this.label = label; this.run = run; }
    }

    /// <summary>
    /// The game-specific half of TrackKit. The package knows nothing about a particular game's
    /// waypoint, spawn or race-manager scripts; a project supplies one class implementing this
    /// (in an Editor folder of the project) and TrackKit finds it automatically.
    /// </summary>
    public interface ITrackKitAdapter
    {
        string DisplayName { get; }

        /// <summary>Fill game defaults into a new settings asset (kit pieces, layers, scene folder...).</summary>
        void ConfigureDefaults(TrackKitSettings settings);

        /// <summary>Turn the ordered markers into the game's race objects and validate them. Record Undo as needed.</summary>
        void Bake(TrackBakeContext context, TrackReport report);

        /// <summary>Extra buttons for the TrackKit window (test scenes, play helpers...).</summary>
        IEnumerable<TrackKitAction> Actions { get; }
    }

    public static class TrackKitAdapters
    {
        static ITrackKitAdapter active;
        static bool searched;

        /// <summary>The project's adapter, or null if the project has none.</summary>
        public static ITrackKitAdapter Active
        {
            get
            {
                if (searched) return active;
                searched = true;
                var found = new List<Type>();
                foreach (Type t in TypeCache.GetTypesDerivedFrom<ITrackKitAdapter>())
                    if (!t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null) found.Add(t);
                if (found.Count > 1) Debug.LogWarning("[TrackKit] " + found.Count + " adapters found; using " + found[0].FullName + ".");
                if (found.Count > 0) active = (ITrackKitAdapter)Activator.CreateInstance(found[0]);
                return active;
            }
        }
    }
}
