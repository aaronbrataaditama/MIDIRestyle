namespace MidiRestyle.Mcp;

/// <summary>A source track-channel, the loader's own scope key (Format 0 splits into one per channel).</summary>
public sealed record TrackChannelRef(int Track, int Channel);

/// <summary>Everything the two write tools accept, as the tools accept it: strings for enum-like values
/// and tonics, so the resolver owns every error message.</summary>
public sealed record RestyleRequest(
    string InputPath,
    string TargetScaleId,
    string? TargetTonic,
    string? SourceScaleId,
    string? SourceTonic,
    IReadOnlyList<TrackChannelRef>? Exclude,
    string? Strategy,
    string? NonScaleNotes,
    string? Collisions,
    string? Range,
    double? ToleranceCents,
    string? OutputPath,
    bool Overwrite);

public sealed record TonicEcho(string Name, int Midi);

public sealed record MappingEcho(string Strategy, string NonScaleNotes, string Collisions, string Range);

/// <summary>What the engine actually received - every value, supplied or defaulted.</summary>
public sealed record ResolvedSettings(
    string TargetScaleId,
    TonicEcho TargetTonic,
    string? SourceScaleId,
    TonicEcho? SourceTonic,
    IReadOnlyList<TrackChannelRef> Excluded,
    MappingEcho Mapping,
    double ToleranceCents,
    bool KeyDetectionUsed);
