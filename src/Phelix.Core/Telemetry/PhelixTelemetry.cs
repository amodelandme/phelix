using System.Diagnostics;

  namespace Phelix.Core.Telemetry;

  /// <summary>
  /// Owns the application's single <see cref="ActivitySource"/> and defines all span
  /// and tag name constants used across the codebase.
  /// </summary>
  /// <remarks>
  /// Consumers reference constants via the nested <see cref="Spans"/> and <see cref="Tags"/>
  /// classes so call sites are self-documenting without opening this file.
  /// Example: <c>PhelixTelemetry.Tags.Tool.Success</c> is unambiguous at a glance.
  /// </remarks>
  public static class PhelixTelemetry
  {
      /// <summary>The name used to register the <see cref="ActivitySource"/> with the OTel SDK.</summary>
      public const string SourceName = "Phelix";

      /// <summary>The single source for all Phelix-emitted spans.</summary>
      public static readonly ActivitySource Source = new(SourceName, "0.1");

      /// <summary>Span names.</summary>
      public static class Spans
      {
          public const string Turn     = "phelix.agent.turn";
          public const string ToolCall = "phelix.tool.call";
      }

      /// <summary>Tag (attribute) name constants, grouped by the span they appear on.</summary>
      public static class Tags
      {
          /// <summary>Tags set on <see cref="Spans.Turn"/>.</summary>
          public static class Turn
          {
              public const string ModelId      = "phelix.turn.model_id";
              public const string ToolTurns    = "phelix.turn.tool_turns";
              public const string InputTokens  = "gen_ai.usage.input_tokens";
              public const string OutputTokens = "gen_ai.usage.output_tokens";
          }

          /// <summary>Tags set on <see cref="Spans.ToolCall"/>.</summary>
          public static class Tool
          {
              public const string Name    = "phelix.tool.name";
              public const string Success = "phelix.tool.success";
              public const string Error   = "phelix.tool.error";
          }
      }
  }