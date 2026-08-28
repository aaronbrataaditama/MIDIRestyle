namespace MidiRestyle.Core.Scales;

/// <summary>
/// Thrown when a <see cref="Scale"/> would violate an invariant the rest of the domain relies on.
/// </summary>
/// <remarks>
/// Validation lives in the <see cref="Scale"/> constructor so the JSON loader, the Scala
/// <c>.scl</c> importer and the custom scale editor all inherit it without each remembering to
/// check. The failures it prevents surface far from their cause otherwise: a zero-degree scale
/// reaches the degree mapper's modulo and throws <see cref="DivideByZeroException"/> from deep inside
/// the transform, and a scale with a degree at 1200 cents quietly emits two identical pitches in
/// every octave.
/// </remarks>
public sealed class ScaleValidationException(string scaleId, string reason)
    : Exception($"Scale '{scaleId}' is invalid: {reason}")
{
    public string ScaleId { get; } = scaleId;

    public string Reason { get; } = reason;
}
