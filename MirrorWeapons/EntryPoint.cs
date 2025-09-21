using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace MirrorWeapons
{
    [BepInPlugin("Dinorush." + MODNAME, MODNAME, "1.0.0")]
    internal sealed class EntryPoint : BasePlugin
    {
        public const string MODNAME = "MirrorWeapons";

        public override void Load()
        {
            Configuration.Init();
            new Harmony(MODNAME).PatchAll();
            Log.LogMessage("Loaded " + MODNAME);
        }
    }
}