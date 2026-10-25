namespace Wiina.Core.Settings;

/// <summary>
/// How timeline thumbnails are decoded. Thumbnail extraction runs a headless
/// libmpv instance; <see cref="Auto"/> lets libmpv use the GPU decoder when the
/// driver is usable (it falls back on its own), while <see cref="Cpu"/> forces
/// software decoding for GPUs whose drivers misbehave.
/// </summary>
public enum ThumbnailAcceleration
{
    /// <summary>Hardware decoding when available, CPU otherwise (default).</summary>
    Auto = 0,

    /// <summary>Always decode on the CPU.</summary>
    Cpu = 1,
}
