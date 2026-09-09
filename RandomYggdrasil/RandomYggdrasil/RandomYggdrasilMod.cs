using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MushroomMods;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RandomYggdrasil
{
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    [BepInDependency(MushroomSync.MushroomSyncPlugin.PluginGuid)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    public class RandomYggdrasilMod : BaseUnityPlugin
    {
        internal const string PluginId = "gonfreecss.RandomYggdrasilMod";
        internal const string PluginName = "RandomYggdrasil";
        internal const string PluginVersion = "1.2.0";

        private const string RotationSection = "WorldRotations";
        private const string ConfigFileName = "RandomYggdrasil.cfg";
        private const int RotationNotGenerated = -1;

        // The scene object the entire mod hangs on. Confirmed present and active in
        // Valheim 1.0.7 under this exact name; see docs/yggdrasil-branch.md.
        private const string BranchObjectName = "YggdrasilBranch";
        private const string VanillaBranchPath = "_GameMain/_Environment/" + BranchObjectName;

        private readonly Harmony harmony = new Harmony(PluginId);

        private static ConfigFile modConfig;
        private static ConfigFile alternateConfig;
        private static bool loggedMissingBranch;
        private static readonly Dictionary<string, ConfigEntry<int>> rotationEntries = new Dictionary<string, ConfigEntry<int>>();

        void Awake()
        {
            InitializeConfigFiles();
            modConfig.Bind(
                "General",
                "LockConfiguration",
                true,
                "If on, the server owns world rotations and connected clients use the server's values.");

            RotationSync.Initialize(Logger);

            // Last, and per class: RotationSync above is what makes the mod work on a
            // client at all, and it used to sit behind an unguarded PatchAll.
            // See Shared/PatchIsolation.cs.
            int skipped = PatchIsolation.PatchAllIsolated(harmony, typeof(RandomYggdrasilMod).Assembly, Logger);
            if (skipped > 0)
            {
                Logger.LogWarning($"RandomYggdrasil: {skipped} patch class(es) skipped - see the errors above.");
            }

            Debug.Log($"RandomYggdrasil: Loaded ({(IsDedicatedServer() ? "dedicated server" : "client")}), config at '{modConfig.ConfigFilePath}'");
        }

        private static bool IsDedicatedServer()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                return true;
            }

            string processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            return processName.IndexOf("valheim_server", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Client: Valheim/BepInEx/config/RandomYggdrasil.cfg
        private static string GetClientConfigPath()
        {
            return Path.Combine(Paths.ConfigPath, ConfigFileName);
        }

        // Dedicated server: Valheim/config/bepinex/RandomYggdrasil.cfg
        // On lloesche Docker the persisted volume is /config, so this becomes /config/bepinex/.
        private static string GetServerConfigPath()
        {
            const string dockerConfigRoot = "/config";
            if (Directory.Exists(dockerConfigRoot))
            {
                return Path.Combine(dockerConfigRoot, "bepinex", ConfigFileName);
            }

            return Path.Combine(Path.GetDirectoryName(Paths.BepInExRootPath), "config", "bepinex", ConfigFileName);
        }

        private static string GetPrimaryConfigPath()
        {
            return IsDedicatedServer() ? GetServerConfigPath() : GetClientConfigPath();
        }

        private static string GetAlternateConfigPath()
        {
            return IsDedicatedServer() ? GetClientConfigPath() : GetServerConfigPath();
        }

        private static void EnsureConfigDirectoryExists(string configPath)
        {
            string directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static void InitializeConfigFiles()
        {
            string primaryPath = GetPrimaryConfigPath();
            string alternatePath = GetAlternateConfigPath();

            string configPath;
            if (File.Exists(primaryPath))
            {
                configPath = primaryPath;
            }
            else if (File.Exists(alternatePath))
            {
                configPath = alternatePath;
            }
            else
            {
                configPath = primaryPath;
            }

            EnsureConfigDirectoryExists(configPath);
            modConfig = new ConfigFile(configPath, true);

            alternateConfig = null;
            if (File.Exists(alternatePath) && !string.Equals(configPath, alternatePath, System.StringComparison.OrdinalIgnoreCase))
            {
                alternateConfig = new ConfigFile(alternatePath, true);
            }
        }

        private static bool TryGetRotationFromConfig(ConfigFile config, string worldIdentifier, out int degrees)
        {
            ConfigEntry<int> entry = config.Bind(
                RotationSection,
                worldIdentifier,
                RotationNotGenerated,
                "Y-axis rotation (in degrees, 0-359) of Yggdrasil for this world. Generated once per world on the server; delete this entry to re-roll.");

            if (entry.Value >= 0 && entry.Value < 360)
            {
                degrees = entry.Value;
                return true;
            }

            degrees = RotationNotGenerated;
            return false;
        }

        private static bool TryImportRotationFromAlternateConfig(string worldIdentifier, ConfigEntry<int> entry)
        {
            if (alternateConfig == null)
            {
                return false;
            }

            if (!TryGetRotationFromConfig(alternateConfig, worldIdentifier, out int degrees))
            {
                return false;
            }

            entry.Value = degrees;
            modConfig.Save();

            Debug.Log($"RandomYggdrasil: Imported rotation of {degrees} degrees for world '{worldIdentifier}' from alternate config");
            return true;
        }

        private static string GetWorldIdentifier()
        {
            ZNet znet = ZNet.instance;
            if (znet == null || znet.GetWorldName() == null)
            {
                return null;
            }

            string identifier = $"{znet.GetWorldName()}_{znet.GetWorldUID()}";

            foreach (char c in "=\n\t\\\"'[]")
            {
                identifier = identifier.Replace(c.ToString(), "");
            }

            return identifier;
        }

        private static ConfigEntry<int> GetRotationEntry(string worldIdentifier)
        {
            if (!rotationEntries.TryGetValue(worldIdentifier, out ConfigEntry<int> entry))
            {
                entry = modConfig.Bind(
                    RotationSection,
                    worldIdentifier,
                    RotationNotGenerated,
                    "Y-axis rotation (in degrees, 0-359) of Yggdrasil for this world. Generated once per world on the server; delete this entry to re-roll.");

                rotationEntries[worldIdentifier] = entry;
            }

            return entry;
        }

        private static int GetOrCreateRotation(string worldIdentifier)
        {
            if (!RotationSync.IsServerAuthority()
                && RotationSync.TryGetSyncedRotation(worldIdentifier, out int syncedDegrees))
            {
                Debug.Log($"RandomYggdrasil: Using server rotation of {syncedDegrees} degrees for world '{worldIdentifier}'");
                return syncedDegrees;
            }

            ConfigEntry<int> entry = GetRotationEntry(worldIdentifier);

            if (entry.Value >= 0 && entry.Value < 360)
            {
                Debug.Log($"RandomYggdrasil: Using rotation of {entry.Value} degrees for world '{worldIdentifier}'");
                return entry.Value;
            }

            if (TryImportRotationFromAlternateConfig(worldIdentifier, entry))
            {
                RotationSync.Broadcast();
                return entry.Value;
            }

            if (RotationSync.IsServerAuthority())
            {
                entry.Value = new System.Random().Next(360);
                modConfig.Save();
                RotationSync.Broadcast();

                Debug.Log($"RandomYggdrasil: Generated new rotation of {entry.Value} degrees for world '{worldIdentifier}'");
                return entry.Value;
            }

            if (!RotationSync.HasReceivedSync)
            {
                return RotationNotGenerated;
            }

            Debug.LogWarning($"RandomYggdrasil: No rotation received from server for world '{worldIdentifier}', leaving Yggdrasil at default orientation");
            return RotationNotGenerated;
        }

        internal static void EnsureCurrentWorldRotation()
        {
            if (!RotationSync.IsServerAuthority())
            {
                return;
            }

            string worldIdentifier = GetWorldIdentifier();
            if (worldIdentifier != null)
            {
                GetOrCreateRotation(worldIdentifier);
            }
        }

        internal static Dictionary<string, int> GetRotationSnapshot()
        {
            Dictionary<string, int> snapshot = new Dictionary<string, int>();
            foreach (KeyValuePair<string, ConfigEntry<int>> pair in rotationEntries)
            {
                int degrees = pair.Value.Value;
                if (degrees >= 0 && degrees < 360)
                {
                    snapshot[pair.Key] = degrees;
                }
            }

            return snapshot;
        }

        /// <summary>
        /// Locates the Yggdrasil branch, explaining itself once per process when it is
        /// not there. Vanilla keeps it active at <see cref="VanillaBranchPath"/>, so a
        /// null here means the object was renamed, moved out of the active scene, or
        /// deactivated - and the mod would otherwise do nothing and say nothing.
        /// </summary>
        private static GameObject FindYggdrasilBranch()
        {
            GameObject branch = GameObject.Find(BranchObjectName);
            if (branch == null)
            {
                LogMissingBranchOnce();
            }

            return branch;
        }

        private static void LogMissingBranchOnce()
        {
            if (loggedMissingBranch)
            {
                return;
            }

            loggedMissingBranch = true;

            Scene scene = SceneManager.GetActiveScene();
            string instanceKind = IsDedicatedServer() ? "dedicated server" : "client";

            Debug.LogWarning(
                $"RandomYggdrasil: no active GameObject named '{BranchObjectName}' in scene '{scene.name}' ({instanceKind}). "
                + $"Valheim 1.0.7 keeps it at '{VanillaBranchPath}'. Yggdrasil stays at its default orientation "
                + "until the name below is corrected in the source.");

            Debug.LogWarning($"RandomYggdrasil: scene roots: {string.Join(", ", GetRootObjectNames(scene))}");

            List<string> candidates = FindRenameCandidates(scene);
            Debug.LogWarning(candidates.Count == 0
                ? "RandomYggdrasil: no object in this scene has 'ygg' in its name, so the branch was removed rather than renamed."
                : $"RandomYggdrasil: scene objects with 'ygg' in the name - a rename shows up here: {string.Join(", ", candidates)}");
        }

        private static string[] GetRootObjectNames(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            string[] names = new string[roots.Length];
            for (int i = 0; i < roots.Length; i++)
            {
                names[i] = roots[i].name;
            }

            return names;
        }

        /// <summary>
        /// Scene objects whose name looks Yggdrasil-ish, inactive ones included - the
        /// point is to name the replacement, and <see cref="GameObject.Find"/> skips
        /// anything inactive. Restricted to the active scene so the several thousand
        /// loaded prefab assets stay out of the log.
        /// </summary>
        private static List<string> FindRenameCandidates(Scene scene)
        {
            const int limit = 20;
            List<string> found = new List<string>();

            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (found.Count >= limit)
                {
                    break;
                }

                if (!candidate.scene.IsValid() || candidate.scene != scene)
                {
                    continue;
                }

                if (candidate.name.IndexOf("ygg", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                found.Add(GetHierarchyPath(candidate) + (candidate.activeInHierarchy ? "" : " (inactive)"));
            }

            return found;
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            string path = gameObject.name;
            for (Transform parent = gameObject.transform.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return path;
        }

        internal static void TryApplyStoredRotation()
        {
            string worldIdentifier = GetWorldIdentifier();
            if (worldIdentifier == null)
            {
                return;
            }

            int degrees = GetOrCreateRotation(worldIdentifier);
            if (degrees < 0)
            {
                return;
            }

            GameObject gameObject = FindYggdrasilBranch();
            if (gameObject == null)
            {
                return;
            }

            gameObject.transform.rotation = Quaternion.Euler(0.0f, degrees, 0.0f);
            Debug.Log($"RandomYggdrasil: Applied rotation of {degrees} degrees after config sync");
        }

        [HarmonyPatch(typeof(ZNetScene))]
        class RandomizeYggdrasil_Patch
        {
            [HarmonyPatch("Awake")]
            [HarmonyPostfix]
            static void RandomizeYggdrasil()
            {
                Debug.Log("Starting location randomizer for Yggdrassil");

                string worldIdentifier = GetWorldIdentifier();
                int degrees = RotationNotGenerated;

                if (worldIdentifier != null)
                {
                    degrees = GetOrCreateRotation(worldIdentifier);
                }

                GameObject gameObject = FindYggdrasilBranch();
                if (gameObject == null)
                {
                    if (worldIdentifier != null && degrees >= 0)
                    {
                        Debug.Log($"RandomYggdrasil: Stored rotation {degrees} degrees for world '{worldIdentifier}' (no scene object on this instance)");
                    }

                    return;
                }

                if (degrees >= 0)
                {
                    gameObject.transform.rotation = Quaternion.Euler(0.0f, degrees, 0.0f);
                    return;
                }

                if (worldIdentifier == null)
                {
                    degrees = new System.Random().Next(360);
                    gameObject.transform.rotation = Quaternion.Euler(0.0f, degrees, 0.0f);
                    Debug.Log($"RandomYggdrasil: No world info available, using one-time random rotation of {degrees} degrees");
                    return;
                }

                Debug.Log("RandomYggdrasil: Waiting for server config sync before applying rotation");
            }
        }
    }
}
