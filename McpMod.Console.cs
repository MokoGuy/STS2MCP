using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Nodes.Debug;

namespace STS2_MCP;

// Local addition (Spire Ledger testing): run a dev-console command, e.g. "fight JAW_WORM", "godmode",
// "heal 999". Debug commands are available because the game enables them when running modded.
public static partial class McpMod
{
    private static readonly FieldInfo? DevConsoleField =
        typeof(NDevConsole).GetField("_devConsole", BindingFlags.Instance | BindingFlags.NonPublic);

    private static Dictionary<string, object?> ExecuteConsole(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return Error("Missing 'command'");
        if (DevConsoleField?.GetValue(NDevConsole.Instance) is not DevConsole console)
            return Error("Dev console not available");
        CmdResult result = console.ProcessCommand(command);
        return new Dictionary<string, object?>
        {
            ["status"] = result.success ? "ok" : "error",
            ["msg"] = result.msg
        };
    }
}
