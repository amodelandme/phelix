using System.Text;

namespace Phelix.Core.Agent;

/// <summary>
/// Replaces terminal control characters with visible literal representations.
/// </summary>
/// <remarks>
/// Used by <see cref="InteractiveApprovalGate"/> before printing model-controlled
/// strings (tool name, call summary) to the terminal. Without sanitization, a model
/// could embed ANSI escape sequences or bare CR/backspace characters that reposition
/// the cursor and overwrite previously rendered approval prompt lines, tricking the
/// user into approving a command they did not see.
///
/// Every C0 control character (U+0000–U+001F) except <c>\t</c> and <c>\n</c> is
/// replaced with an angle-bracketed name. The C1 range (U+007F–U+009F) and the
/// Unicode line/paragraph separators (U+2028, U+2029) are also replaced. Tab and
/// newline are left intact because their terminal behaviour is unambiguous.
/// </remarks>
internal static class ControlCharSanitizer
{
    /// <summary>
    /// Returns a copy of <paramref name="value"/> with all control characters replaced
    /// by their visible representations. Printable characters pass through unchanged.
    /// Returns the original reference when no replacements are needed.
    /// </summary>
    internal static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        StringBuilder? builder = null;
        int segmentStart = 0;

        for (int i = 0; i < value.Length; i++)
        {
            string? replacement = GetReplacement(value[i]);

            if (replacement is null)
                continue;

            builder ??= new StringBuilder(value.Length + 16);
            builder.Append(value, segmentStart, i - segmentStart);
            builder.Append(replacement);
            segmentStart = i + 1;
        }

        if (builder is null)
            return value;

        builder.Append(value, segmentStart, value.Length - segmentStart);
        return builder.ToString();
    }

    static string? GetReplacement(char c) => c switch
    {
        '\t' or '\n'            => null,
        '\x00'                  => "<NUL>",
        '\x01'                  => "<SOH>",
        '\x02'                  => "<STX>",
        '\x03'                  => "<ETX>",
        '\x04'                  => "<EOT>",
        '\x05'                  => "<ENQ>",
        '\x06'                  => "<ACK>",
        '\a'                    => "<BEL>",
        '\b'                    => "<BS>",
        '\x0B'                  => "<VT>",
        '\f'                    => "<FF>",
        '\r'                    => "<CR>",
        '\x0E'                  => "<SO>",
        '\x0F'                  => "<SI>",
        '\x10'                  => "<DLE>",
        '\x11'                  => "<DC1>",
        '\x12'                  => "<DC2>",
        '\x13'                  => "<DC3>",
        '\x14'                  => "<DC4>",
        '\x15'                  => "<NAK>",
        '\x16'                  => "<SYN>",
        '\x17'                  => "<ETB>",
        '\x18'                  => "<CAN>",
        '\x19'                  => "<EM>",
        '\x1A'                  => "<SUB>",
        '\x1B'                  => "<ESC>",
        '\x1C'                  => "<FS>",
        '\x1D'                  => "<GS>",
        '\x1E'                  => "<RS>",
        '\x1F'                  => "<US>",
        '\x7F'                  => "<DEL>",
        >= '\x80' and <= '\x9F' => $"<U+{(int)c:X4}>",
        (char)0x2028            => "<LS>",
        (char)0x2029            => "<PS>",
        _                       => null
    };
}
