using MidiRestyle.Core.Model;

namespace MidiRestyle.Core.Io;

/// <summary>
/// Thrown when a <see cref="RestyleResult"/> cannot be written because of a genuine IO failure - a
/// bad path, a locked file, a full disk. The domain boundary for export IO failure, mirroring
/// <see cref="MidiFileLoadException"/> on the way in.
/// </summary>
/// <remarks>
/// Anything a user's own data can cause - a microtonal target scale, an out-of-range note - is
/// reported through <see cref="ExportResult"/> instead, never thrown. This type exists only for
/// failures that have nothing to do with the data being exported.
/// </remarks>
public sealed class MidiFileExportException : Exception
{
    public MidiFileExportException(string message, string? filePath, Exception? innerException = null)
        : base(message, innerException)
    {
        FilePath = filePath;
    }

    /// <summary>The destination that could not be written, when writing came from a path.</summary>
    public string? FilePath { get; }
}
