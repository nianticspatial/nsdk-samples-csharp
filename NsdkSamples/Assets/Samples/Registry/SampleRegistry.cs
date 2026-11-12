// Copyright 2026 Niantic Spatial.

using System.Collections.Generic;
using UnityEngine;

namespace NianticSpatial.NSDK.Samples
{
    /// <summary>
    /// Registry of samples contributed at runtime by the active build variant.
    ///
    /// Empty by default: the default build registers nothing, so the launcher shows only the
    /// built-in (scene-authored) samples. A build variant may self-register additional samples at
    /// startup (before the first scene loads), so they appear without this assembly ever naming
    /// them. The launcher (<c>HomeNavManager</c>) reads <see cref="Entries"/>.
    ///
    /// Lives in this assembly so both the launcher and any registrar assembly can reach it; a
    /// registrar references <c>NianticSpatial.NSDK.Samples</c> and registers into it.
    /// </summary>
    public static class SampleRegistry
    {
        private static readonly List<SampleEntry> _entries = new List<SampleEntry>();

        /// <summary>Samples registered by the active variant. Empty in the external build.</summary>
        public static IReadOnlyList<SampleEntry> Entries => _entries;

        /// <summary>Called by a variant's registrar to contribute a sample.</summary>
        public static void Register(SampleEntry entry) => _entries.Add(entry);

        /// <summary>Called by a variant's registrar to contribute multiple samples.</summary>
        public static void Register(IEnumerable<SampleEntry> entries) => _entries.AddRange(entries);

        // Clear before any registrar contributes (SubsystemRegistration runs before the registrars'
        // BeforeSceneLoad). Without this, disabling domain reload (Enter Play Mode Options) would keep
        // stale statics and duplicate entries across play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode() => _entries.Clear();
    }
}
