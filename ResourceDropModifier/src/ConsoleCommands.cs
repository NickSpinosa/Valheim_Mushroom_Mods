using System;

namespace ResourceDropModifier
{
    /// <summary>
    /// <c>rdm_reload</c>, <c>rdm_rescan</c> and <c>rdm_show</c>. Registered from a
    /// postfix on <c>Terminal.InitTerminal</c>, inside a try/catch: the
    /// <c>ConsoleCommand</c> constructor has changed shape across updates and a DLL
    /// built against an older game throws <c>MissingMethodException</c> right here.
    /// Losing the commands is survivable; letting that escape the postfix chain takes
    /// every other mod's commands down with it. Same reasoning as Combat Adjustments.
    /// </summary>
    internal static class ConsoleCommands
    {
        private static bool _registered;

        internal static void Register()
        {
            if (_registered)
                return;
            _registered = true;

            try
            {
                _ = new Terminal.ConsoleCommand(
                    "rdm_reload",
                    "re-read the Resource Drop Modifier config from disk and apply it",
                    Reload,
                    isCheat: false);

                _ = new Terminal.ConsoleCommand(
                    "rdm_rescan",
                    "re-run the Resource Drop Modifier catalog walk and add any new items to the config",
                    Rescan,
                    isCheat: false);

                _ = new Terminal.ConsoleCommand(
                    "rdm_show",
                    "rdm_show <prefab> - print the drop multiplier in force for an item",
                    Show,
                    isCheat: false);

                Plugin.Log.LogInfo("Console commands registered: rdm_reload, rdm_rescan, rdm_show");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Failed to register Resource Drop Modifier console commands; continuing without them: " + ex.Message);
            }
        }

        private static void Reload(Terminal.ConsoleEventArgs args)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null)
                return;

            string result = plugin.ReloadFromDisk();
            args.Context?.AddString(result);
        }

        private static void Rescan(Terminal.ConsoleEventArgs args)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null)
                return;

            if (ZoneSystem.instance == null)
            {
                args.Context?.AddString("<color=red>No world loaded - join a world first.</color>");
                return;
            }

            plugin.OnWorldReady();
            args.Context?.AddString("Catalog rebuilt: " + DropConfig.Count + " item setting(s).");
        }

        private static void Show(Terminal.ConsoleEventArgs args)
        {
            if (args.Args == null || args.Args.Length < 2)
            {
                args.Context?.AddString("Usage: rdm_show <prefab>");
                return;
            }

            string prefab = args.Args[1];
            float multiplier;
            bool listed = Plugin.Table.TryGet(prefab, out multiplier);
            string source = Plugin.Sync != null && Plugin.Sync.ClientSyncActive ? "server" : "local";

            args.Context?.AddString(listed
                ? prefab + " = " + multiplier + " (" + source + ")"
                : prefab + " has no setting; vanilla amounts (" + source + ")");
        }
    }
}
