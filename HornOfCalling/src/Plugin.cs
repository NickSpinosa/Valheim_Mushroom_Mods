using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HornOfCalling
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    public class Plugin : BaseUnityPlugin
    {
        internal const string Guid = "com.greg.hornofcalling";
        internal const string Name = "HornOfCalling";
        internal const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Log.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
