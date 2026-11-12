// Copyright 2026 Niantic Spatial.

namespace NianticSpatial.NSDK.Samples
{
    /// <summary>
    /// A sample contributed at runtime by the active build variant (see <see cref="SampleRegistry"/>).
    ///
    /// Lives in the <c>NianticSpatial.NSDK.Samples</c> assembly so the launcher can render it, but
    /// entries are only ever added by a variant's registrar: the default build registers none; a
    /// build variant may self-register additional samples at startup. The launcher never references
    /// a concrete registered sample — it only renders whatever the registry holds.
    ///
    /// A Unity sample is a scene loaded by name, so an entry carries the scene <b>name</b>
    /// (+ label/section) rather than a view — contrast with Swift/Kotlin, where the entry carries
    /// the screen itself.
    /// </summary>
    public readonly struct SampleEntry
    {
        /// <summary>Name of the scene to load (must be in the build for the active variant).</summary>
        public readonly string SceneName;

        /// <summary>Label shown on the generated menu button.</summary>
        public readonly string Title;

        /// <summary>Menu section this sample is grouped under.</summary>
        public readonly string Section;

        public SampleEntry(string sceneName, string title, string section = "Samples")
        {
            SceneName = sceneName;
            Title = title;
            Section = section;
        }
    }
}
