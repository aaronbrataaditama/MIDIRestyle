using Microsoft.Extensions.Logging;

namespace MidiRestyle.Mcp.Tests;

/// <summary>
/// Both this class and <see cref="McpHostTests"/> redirect <see cref="Console.Out"/>, which is
/// process-wide. xunit runs test classes in parallel, so without a shared collection one class's
/// redirection would land in the other's buffer - a flake that would look like a logging bug.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ConsoleCollection
{
    public const string Name = "console redirection";
}

/// <summary>
/// The server's only sink. Its one hard requirement is negative: stdout carries JSON-RPC frames and
/// nothing else, so a single log line written there is a malformed frame and the session is over.
/// That is asserted directly rather than inferred from the implementation.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class StderrLoggerFactoryTests
{
    private static (string Out, string Error) Capture(Action<ILogger> act, LogLevel minimum = LogLevel.Information)
    {
        TextWriter savedOut = Console.Out, savedError = Console.Error;
        var outBuffer = new StringWriter();
        var errorBuffer = new StringWriter();
        try
        {
            Console.SetOut(outBuffer);
            Console.SetError(errorBuffer);
            using var factory = new StderrLoggerFactory(minimum);
            act(factory.CreateLogger("MIDIRestyle"));
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedError);
        }

        return (outBuffer.ToString(), errorBuffer.ToString());
    }

    [Fact]
    public void EveryLineGoesToStderrAndNothingAtAllGoesToStdout()
    {
        (string stdout, string stderr) = Capture(log =>
        {
            log.LogInformation("Scale library: {Count} scales.", 171);
            log.LogWarning("Scale not loaded: {Id}", "bad.scale");
        });

        stderr.Should().Contain("Scale library: 171 scales.", "the message is formatted, not left as a template")
            .And.Contain("[Information]").And.Contain("[Warning]")
            .And.Contain("MIDIRestyle:", "the category names which component spoke");
        stdout.Should().BeEmpty("stdout carries JSON-RPC frames and nothing else");
    }

    [Fact]
    public void ALevelBelowTheMinimumIsNotWrittenAtAll()
    {
        (string stdout, string stderr) = Capture(
            log =>
            {
                log.LogDebug("chatter that must not reach the host's log");
                log.LogWarning("this one matters");
            },
            LogLevel.Warning);

        stderr.Should().NotContain("chatter").And.Contain("this one matters");
        stdout.Should().BeEmpty();
    }

    /// <summary>
    /// An exception is appended in full. Without it the only record of a failure inside the server is
    /// its message, and the stack - which says which of several call sites raised it - is lost.
    /// </summary>
    [Fact]
    public void AnExceptionIsWrittenAfterItsMessage()
    {
        (string stdout, string stderr) = Capture(log =>
            log.LogError(new InvalidOperationException("pipeline disagreed"), "Scale not loaded: {Id}", "x"));

        stderr.Should().Contain("Scale not loaded: x")
            .And.Contain("InvalidOperationException")
            .And.Contain("pipeline disagreed");
        stdout.Should().BeEmpty();
    }

    [Fact]
    public void IsEnabledFollowsTheMinimumAndNoneIsAlwaysOff()
    {
        using var factory = new StderrLoggerFactory(LogLevel.Warning);
        ILogger log = factory.CreateLogger("x");

        log.IsEnabled(LogLevel.Information).Should().BeFalse();
        log.IsEnabled(LogLevel.Warning).Should().BeTrue();
        log.IsEnabled(LogLevel.Critical).Should().BeTrue();
        log.IsEnabled(LogLevel.None).Should().BeFalse("None means 'log nothing', not 'log everything above Critical'");
    }
}
