using System.Threading.Channels;
using Phelix.Core.Agent;
using Phelix.Tui;

namespace Phelix.Cli;

internal abstract record HostMode
{
    internal sealed record Cli(SessionMode SessionMode) : HostMode;
    internal sealed record Tui(ChannelWriter<TuiEvent> EventWriter) : HostMode;
}
