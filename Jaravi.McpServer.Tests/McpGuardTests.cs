using Jaravi.Core;
using ModelContextProtocol;

namespace Jaravi.McpServer.Tests;

/// <summary>
/// The single translation point between engine errors and protocol errors.
/// Shared by tools and resources so both surfaces fail identically.
///
/// Note the explicitly typed delegates below: a bare <c>() =&gt; throw ...</c>
/// lambda has no inferred return type, so it matches both the sync and async
/// overloads and the call is ambiguous (CS0121). Typing them also makes it
/// unambiguous which overload each test is pinning.
/// </summary>
public class McpGuardTests
{
    [Fact]
    public void Expected_engine_failures_become_protocol_errors_with_the_message_intact()
    {
        Func<string> notFound = () => throw new SessionNotFoundException("abc123");

        var ex = Assert.Throws<McpException>(() => McpGuard.Run(notFound));

        // The message is the only thing the calling agent gets to act on, so it
        // must survive the translation rather than be replaced by a generic one.
        Assert.Contains("abc123", ex.Message);
    }

    [Theory]
    [InlineData(typeof(ProfileNotFoundException))]
    [InlineData(typeof(ScopeGateException))]
    public void Every_JaraviException_subtype_is_covered_by_the_base_class_catch(Type exceptionType)
    {
        var thrown = (JaraviException)Activator.CreateInstance(exceptionType, "x")!;
        Func<string> boom = () => throw thrown;

        Assert.Throws<McpException>(() => McpGuard.Run(boom));
    }

    [Fact]
    public void Unexpected_exceptions_are_deliberately_not_swallowed()
    {
        // An InvalidOperationException is a bug in Jaravi, not a condition the
        // calling agent can do anything about. Dressing it up as a tidy protocol
        // error would hide real defects behind a message that reads like user error.
        Func<string> bug = () => throw new InvalidOperationException("boom");

        Assert.Throws<InvalidOperationException>(() => McpGuard.Run(bug));
    }

    [Fact]
    public async Task The_async_overload_maps_the_same_way()
    {
        Func<Task<string>> notFound = () => throw new SessionNotFoundException("async-id");

        var ex = await Assert.ThrowsAsync<McpException>(() => McpGuard.Run(notFound));

        Assert.Contains("async-id", ex.Message);
    }

    [Fact]
    public async Task Faulted_tasks_are_mapped_too_not_just_synchronous_throws()
    {
        // The engine's async methods usually fail by returning a faulted task
        // rather than throwing before the await — both paths must map identically.
        Func<Task<string>> faulted = () => Task.FromException<string>(new SessionNotFoundException("faulted-id"));

        var ex = await Assert.ThrowsAsync<McpException>(() => McpGuard.Run(faulted));

        Assert.Contains("faulted-id", ex.Message);
    }

    [Fact]
    public async Task Successful_calls_pass_their_value_through_untouched()
    {
        Func<string> sync = () => "ok";
        Func<Task<string>> async = () => Task.FromResult("ok-async");

        Assert.Equal("ok", McpGuard.Run(sync));
        Assert.Equal("ok-async", await McpGuard.Run(async));
    }
}
