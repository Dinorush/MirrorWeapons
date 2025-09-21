using BepInEx;
using BepInEx.Configuration;
using System.IO;

namespace MirrorWeapons
{
    internal static class Configuration
    {
        public static uint Offset { get; private set; } = 0u;
        public static bool LockDuplicate { get; private set; } = true;

        public static void Init()
        {
            ConfigFile configFile = new(Path.Combine(Paths.ConfigPath, EntryPoint.MODNAME + ".cfg"), saveOnInit: true);
            string section = "Developer Settings";
            Offset = configFile.Bind(section, "ID Offset", Offset, "Offset from the original ID to create copied blocks at.\nIf there is an ID conflict, it is given the next consecutive ID after the last block instead.").Value;
            LockDuplicate = configFile.Bind(section, "Lock Duplicate", LockDuplicate, "If a weapon is picked, its copy cannot be picked.\nDoesn't work properly for bots.").Value;
        }
    }
}
