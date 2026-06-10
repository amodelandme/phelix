using Phelix.Core.Agent;

namespace Phelix.Core.Session;

/// <summary>
/// The outcome of a single <see cref="PhelixSession"/> turn — either a completed turn or a failure.
/// </summary>
/// <remarks>
/// <see cref="PhelixSession.RunTurnAsync"/> always returns a <c>TurnResult</c> rather than
/// throwing. This lets entry points (CLI, TUI) decide how to display the error without wrapping
/// every call in a try/catch.
///
/// On failure, <see cref="PhelixSession"/> leaves conversation history unchanged.
/// The error is logged to the session store with <see cref="TurnExitReason.Error"/>.
/// </remarks>
public abstract record TurnResult
{
    /// <summary>The turn completed normally (or hit the turn limit).</summary>
    /// <param name="Turn">The completed turn produced by the agent loop.</param>
    public sealed record Success(Turn Turn) : TurnResult;

    /// <summary>The turn ended due to an unhandled exception.</summary>
    /// <param name="ErrorMessage">The exception message surfaced for display.</param>
    public sealed record Failure(string ErrorMessage) : TurnResult;
}
