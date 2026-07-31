using Jaravi.Core;
using ModelContextProtocol;

namespace Jaravi.McpServer;

/// <summary>Maps domain errors to clean MCP errors instead of opaque 500s — shared by tools and resources.</summary>
internal static class McpGuard
{
    public static T Run<T>(Func<T> action)
    {
        try { return action(); }
        catch (JaraviException ex) { throw new McpException(ex.Message); }
    }

    public static async Task<T> Run<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (JaraviException ex) { throw new McpException(ex.Message); }
    }
}
