using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;

namespace STS2_MCP;

// Local addition (Spire Ledger testing): run a dev-console command, e.g. "fight JAW_WORM", "godmode",
// "heal 999". Debug commands are available because the game enables them when running modded.
public static partial class McpMod
{
    private static readonly FieldInfo? DevConsoleField =
        typeof(NDevConsole).GetField("_devConsole", BindingFlags.Instance | BindingFlags.NonPublic);

    // Saves the game viewport as PNG under %TEMP%\sts2shots\<name>.png (name sanitized; no other paths).
    private static Dictionary<string, object?> ExecuteScreenshot(string name)
    {
        var safe = System.Text.RegularExpressions.Regex.Replace(name ?? "", "[^A-Za-z0-9_-]", "");
        if (safe.Length == 0) safe = "shot";
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sts2shots");
        System.IO.Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, safe + ".png");
        var tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
        var image = tree.Root.GetViewport().GetTexture().GetImage();
        var err = image.SavePng(path);
        return new Dictionary<string, object?> { ["status"] = err == Godot.Error.Ok ? "ok" : "error", ["path"] = path };
    }

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

    // Starts a custom-mode run with a fixed seed from the open "Mode personnalise" screen, so test runs
    // are reproducible. Character: whatever is selected (Ironclad by default).
    private static Dictionary<string, object?> ExecuteCustomEmbark(string seed, string character = "")
    {
        var tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
        var screen = FindFirst<NCustomRunScreen>(tree.Root);
        if (screen == null || screen.Lobby == null) return Error("Custom run screen is not open");
        if (!string.IsNullOrWhiteSpace(character))
        {
            var model = System.Linq.Enumerable.FirstOrDefault(MegaCrit.Sts2.Core.Models.ModelDb.AllCharacters,
                c => string.Equals(c.Id.Entry, character.Trim(), System.StringComparison.OrdinalIgnoreCase));
            if (model == null) return Error($"Unknown character '{character}'");
            screen.Lobby.SetLocalCharacter(model);
        }
        if (!string.IsNullOrWhiteSpace(seed)) screen.Lobby.SetSeed(seed.Trim().ToUpperInvariant()); // host only
        var embark = typeof(NCustomRunScreen).GetMethod("OnEmbarkPressed", BindingFlags.Instance | BindingFlags.NonPublic);
        if (embark == null) return Error("OnEmbarkPressed not found (game changed?)");
        embark.Invoke(screen, new object?[] { null });
        return new Dictionary<string, object?> { ["status"] = "ok", ["seed"] = screen.Lobby.Seed };
    }

    // Presses and releases a key (Godot key name, e.g. "C", "Escape"), for testing hotkeys.
    private static Dictionary<string, object?> ExecuteKey(string name)
    {
        var key = Godot.OS.FindKeycodeFromString(name ?? "");
        if (key == Godot.Key.None) return Error($"Unknown key '{name}'");
        Godot.Input.ParseInputEvent(new Godot.InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        var tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
        tree.CreateTimer(0.2).Timeout += () =>
            Godot.Input.ParseInputEvent(new Godot.InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
        return new Dictionary<string, object?> { ["status"] = "ok", ["key"] = key.ToString() };
    }

    // Clicks a node found by name anywhere in the scene tree (e.g. a modded button), for UI tests.
    private static Dictionary<string, object?> ExecuteClickNode(string name)
    {
        var tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
        var node = tree.Root.FindChild(name ?? "", true, false);
        if (node is not MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl clickable)
            return Error($"No clickable node named '{name}'");
        clickable.ForceClick();
        return new Dictionary<string, object?> { ["status"] = "ok", ["clicked"] = node.GetPath().ToString() };
    }

    // Left-clicks at a viewport position (x, y in game pixels): moves the cursor there, then presses and
    // releases the button a few frames apart, for UI that polls the mouse state.
    private static Dictionary<string, object?> ExecuteMouseClick(float x, float y)
    {
        var tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
        var vp = tree.Root.GetViewport();
        var pos = new Godot.Vector2(x, y);
        vp.WarpMouse(pos);
        Godot.Input.ParseInputEvent(new Godot.InputEventMouseButton { ButtonIndex = Godot.MouseButton.Left, Pressed = true, Position = pos, GlobalPosition = pos });
        tree.CreateTimer(0.15).Timeout += () =>
            Godot.Input.ParseInputEvent(new Godot.InputEventMouseButton { ButtonIndex = Godot.MouseButton.Left, Pressed = false, Position = pos, GlobalPosition = pos });
        return new Dictionary<string, object?> { ["status"] = "ok", ["x"] = x, ["y"] = y, ["viewport"] = vp.GetVisibleRect().Size.ToString() };
    }
}

// Local addition (screenshots): STS2MCP_PLAYER_NAMES="Name1,Name2" renames the Steam-less test players
// (net id 1 = host, 1000 = first client) wherever the game asks the platform for a player name.
[HarmonyLib.HarmonyPatch]
internal static class TestPlayerNames
{
    private static System.Reflection.MethodBase? TargetMethod()
        => HarmonyLib.AccessTools.Method(HarmonyLib.AccessTools.TypeByName("MegaCrit.Sts2.Core.Platform.PlatformUtil"), "GetPlayerNameRaw");

    private static void Postfix(ulong playerId, ref string __result)
    {
        var spec = System.Environment.GetEnvironmentVariable("STS2MCP_PLAYER_NAMES");
        if (string.IsNullOrWhiteSpace(spec)) return;
        var names = spec.Split(',');
        int i = playerId == 1 ? 0 : playerId == 1000 ? 1 : playerId == 1001 ? 2 : playerId == 1002 ? 3 : -1;
        if (i >= 0 && i < names.Length && names[i].Trim().Length > 0) __result = names[i].Trim();
    }
}
