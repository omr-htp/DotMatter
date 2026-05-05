using DotMatter.Core.Sessions;

namespace DotMatter.Core.InteractionModel;

internal static partial class InteractionManager
{
    internal static async Task<IReadOnlyList<ushort>> DiscoverEndpointsAsync(
        ISession session,
        CancellationToken ct = default)
    {
        var result = await ReadAttributeAsync(session, 0, 0x001D, 0x0003, ct);
        var endpoints = new List<ushort> { 0 };
        if (result is IList<object> parts)
        {
            foreach (var part in parts)
            {
                switch (part)
                {
                    case ulong value:
                        endpoints.Add((ushort)value);
                        break;
                    case uint value:
                        endpoints.Add((ushort)value);
                        break;
                    case int value:
                        endpoints.Add((ushort)value);
                        break;
                    case long value:
                        endpoints.Add((ushort)value);
                        break;
                }
            }
        }

        return endpoints;
    }

    internal static async Task<IReadOnlyList<uint>> ReadServerListAsync(
        ISession session,
        ushort endpointId,
        CancellationToken ct = default)
    {
        var result = await ReadAttributeAsync(session, endpointId, 0x001D, 0x0001, ct);
        var clusters = new List<uint>();
        if (result is IList<object> parts)
        {
            foreach (var part in parts)
            {
                switch (part)
                {
                    case ulong value:
                        clusters.Add((uint)value);
                        break;
                    case uint value:
                        clusters.Add(value);
                        break;
                    case long value:
                        clusters.Add((uint)value);
                        break;
                    case int value:
                        clusters.Add((uint)value);
                        break;
                }
            }
        }

        return clusters;
    }
}
