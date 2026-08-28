namespace MidiRestyle.Core.Io;

/// <summary>
/// Thrown when a file cannot be read as a Standard MIDI File. The domain boundary for IO failure.
/// </summary>
/// <remarks>
/// <para>
/// Every DryWetMIDI read exception is translated into this type, so nothing above
/// <see cref="MidiFileLoader"/> ever has to reference the library to handle a bad file - the same
/// reason <c>MidiProject</c> holds no DryWetMIDI types.
/// </para>
/// <para>
/// <b>The message deliberately does not name a byte offset.</b> No DryWetMIDI exception carries a
/// stream position, so any offset shown would be invented. What is genuinely available is the
/// exception type, and - for some failures only - a chunk id and an expected/actual size pair;
/// <see cref="InvalidChunkSizeException"/> carries all three, while a truncated file yields
/// <c>NotEnoughBytesException</c> with <c>ExpectedCount = 0, ActualCount = 0</c>, which is why the
/// size fields here are nullable and the message falls back to describing the likely cause instead.
/// </para>
/// </remarks>
public sealed class MidiFileLoadException : Exception
{
    public MidiFileLoadException(
        string message,
        string? filePath = null,
        string? causeTypeName = null,
        string? chunkId = null,
        long? expectedSize = null,
        long? actualSize = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FilePath = filePath;
        CauseTypeName = causeTypeName;
        ChunkId = chunkId;
        ExpectedSize = expectedSize;
        ActualSize = actualSize;
    }

    /// <summary>The file that failed to load, when the load came from a path.</summary>
    public string? FilePath { get; }

    /// <summary>Simple name of the underlying exception type, e.g. <c>NotEnoughBytesException</c>.</summary>
    public string? CauseTypeName { get; }

    /// <summary>The four-character chunk id involved, where the underlying failure reported one.</summary>
    public string? ChunkId { get; }

    /// <summary>Declared size, where the underlying failure reported one.</summary>
    public long? ExpectedSize { get; }

    /// <summary>Size actually available, where the underlying failure reported one.</summary>
    public long? ActualSize { get; }
}
