namespace MidiRestyle.Core.Notation;

/// <summary>
/// Thrown when a <see cref="NotationScore"/> cannot be written as MusicXML - a bad path, a locked
/// file, a full disk, or a score with nothing in it to write.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="MidiRestyle.Core.Io.MidiFileExportException"/> on the MIDI side, and for the
/// same reason: the caller needs one type to catch and a sentence it can put in front of a user,
/// rather than the raw <see cref="IOException"/> zoo.
/// </para>
/// <para>
/// Anything the <em>music</em> can cause - a scale with no staff spelling, a pitch outside MIDI
/// range, more voices than a staff can hold - is not an error here. Those are decided earlier and
/// reported through <see cref="NotationScore.Diagnostics"/>, because a score that is hard to notate
/// is still a score worth exporting.
/// </para>
/// </remarks>
public sealed class MusicXmlExportException : Exception
{
    public MusicXmlExportException(
        string message, string? filePath = null, Exception? innerException = null)
        : base(message, innerException)
    {
        FilePath = filePath;
    }

    /// <summary>The destination that could not be written, when the failure came from a path.</summary>
    public string? FilePath { get; }
}
