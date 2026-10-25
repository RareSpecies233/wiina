namespace Wiina.Core.State;

/// <summary>
/// Why a hover thumbnail request produced (or did not produce) a frame. The
/// service used to answer <c>null</c> for two very different situations — "the
/// policy forbids previews" and "still working" — which made the overlay flash
/// black on every pointer move (v1.3.8 plan §3.4).
/// </summary>
public enum ThumbnailOutcome
{
    /// <summary>A frame is available (memory, disk or a freshly decoded one).</summary>
    Frame,

    /// <summary>The request is still queued or decoding; keep the previous frame.</summary>
    Pending,

    /// <summary>
    /// The policy forbids previews (thumbnails switched off, or the whole-file
    /// pass has not finished while "preview while generating" is off).
    /// </summary>
    NotAllowed,
}

/// <summary>Visual phase of the timeline hover bubble (v1.3.8 plan §3.2).</summary>
public enum ThumbnailHoverPhase
{
    /// <summary>Not over the timeline: the bubble is collapsed and emptied.</summary>
    Hidden,

    /// <summary>Over the timeline but this file has no frame yet: black backplate.</summary>
    Empty,

    /// <summary>A new position is being resolved; the previous frame stays visible.</summary>
    Showing,

    /// <summary>A fresh frame was just applied.</summary>
    Updated,

    /// <summary>Previews are not allowed: the bubble stays visible/black but empty.</summary>
    Unavailable,
}

/// <summary>
/// State machine behind <c>ThumbnailPeek</c>. Kept in Core so the "keeps the
/// previous frame / never spins / empties on a new file" rules are unit-testable
/// without WinUI: the control only mirrors <see cref="Phase"/> and
/// <see cref="HasFrame"/>.
/// </summary>
public sealed class ThumbnailHoverState
{
    /// <summary>Current phase; starts hidden.</summary>
    public ThumbnailHoverPhase Phase { get; private set; } = ThumbnailHoverPhase.Hidden;

    /// <summary>True once a frame of the current file was applied.</summary>
    public bool HasFrame { get; private set; }

    /// <summary>True while the bubble should be on screen.</summary>
    public bool IsVisible => Phase != ThumbnailHoverPhase.Hidden;

    /// <summary>
    /// Pointer entered a new timeline position. Returns <see cref="ThumbnailHoverPhase.Showing"/>
    /// when a frame of this file is already on screen (it must be kept while the
    /// new one loads) and <see cref="ThumbnailHoverPhase.Empty"/> on the first
    /// hover of a file.
    /// </summary>
    public ThumbnailHoverPhase BeginRequest() =>
        Phase = HasFrame ? ThumbnailHoverPhase.Showing : ThumbnailHoverPhase.Empty;

    /// <summary>
    /// Applies the service outcome. <see cref="ThumbnailOutcome.Pending"/> keeps
    /// the current frame (or the empty backplate). <see cref="ThumbnailOutcome.NotAllowed"/>
    /// empties the bubble: showing a stale frame of the same file would claim a
    /// preview the policy forbids.
    /// </summary>
    public ThumbnailHoverPhase Complete(ThumbnailOutcome outcome)
    {
        switch (outcome)
        {
            case ThumbnailOutcome.Frame:
                HasFrame = true;
                Phase = ThumbnailHoverPhase.Updated;
                break;
            case ThumbnailOutcome.Pending:
                Phase = HasFrame ? ThumbnailHoverPhase.Showing : ThumbnailHoverPhase.Empty;
                break;
            default:
                HasFrame = false;
                Phase = ThumbnailHoverPhase.Unavailable;
                break;
        }

        return Phase;
    }

    /// <summary>Pointer left the timeline / the bubble was closed: drop everything.</summary>
    public void Clear()
    {
        HasFrame = false;
        Phase = ThumbnailHoverPhase.Hidden;
    }

    /// <summary>
    /// A different file was opened. The bubble is usually already hidden by the
    /// media-change hooks; this guarantees a stale frame of the previous file can
    /// never be shown as the new file's first hover.
    /// </summary>
    public void ClearFrame()
    {
        HasFrame = false;
        if (Phase != ThumbnailHoverPhase.Hidden)
        {
            Phase = ThumbnailHoverPhase.Empty;
        }
    }
}
