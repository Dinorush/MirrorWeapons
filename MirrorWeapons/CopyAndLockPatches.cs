using GameData;
using Gear;
using HarmonyLib;
using Player;
using System.Collections.Generic;

namespace MirrorWeapons
{
    [HarmonyPatch]
    internal static class CopyAndLockPatches
    {
        static class CopyFixer<T> where T : GameDataBlockBase<T>
        {
            private static int s_counter = 0;

            public static T CreateNewCopy(T block)
            {
                // Force unique names to prevent duplicate name errors in CreateNewCopy.
                var oldName = block.name;
                block.name = oldName + (++s_counter).ToString();
                var copy = GameDataBlockBase<T>.CreateNewCopy(block);
                block.name = oldName;
                return copy;
            }
        }

        private static readonly Dictionary<uint, uint> _offlineCopies = new();
        private static readonly Dictionary<InventorySlot, (int pos, GearIDRange range)> _blockedGearRanges = new();


        [HarmonyPatch(typeof(GameDataInit), nameof(GameDataInit.Initialize))]
        [HarmonyWrapSafe]
        [HarmonyPostfix]
        private static void Post_Initialize()
        {
            _blockedGearRanges.Clear();
            _offlineCopies.Clear();
            Dictionary<uint, uint> itemToCopy = new();
            Dictionary<uint, uint> cateToCopy = new();
            foreach (var offlineBlock in PlayerOfflineGearDataBlock.GetAllBlocks())
            {
                if (!TryGetComp(offlineBlock.GearJSON, eGearComponent.BaseItem, out ItemDataBlock itemBlock)) continue;
                if (!TryGetComp(offlineBlock.GearJSON, eGearComponent.Category, out GearCategoryDataBlock cateBlock)) continue;

                uint itemBlockID = itemBlock.persistentID;
                uint cateBlockID = cateBlock.persistentID;

                InventorySlot newSlot;
                switch (itemBlock.inventorySlot)
                {
                    case InventorySlot.GearStandard:
                        newSlot = InventorySlot.GearSpecial;
                        break;
                    case InventorySlot.GearSpecial:
                        newSlot = InventorySlot.GearStandard;
                        break;
                    default:
                        continue;
                }

                if (!itemToCopy.TryGetValue(itemBlockID, out var copyItemID))
                {
                    var copyItem = CopyFixer<ItemDataBlock>.CreateNewCopy(itemBlock);
                    copyItem.inventorySlot = newSlot;
                    TrySetIDOffset(copyItem, itemBlockID);
                    itemToCopy.Add(itemBlockID, copyItemID = copyItem.persistentID);
                }

                if (!cateToCopy.TryGetValue(cateBlockID, out var copyCateID))
                {
                    var copyCate = CopyFixer<GearCategoryDataBlock>.CreateNewCopy(cateBlock);
                    copyCate.BaseItem = copyItemID;
                    TrySetIDOffset(copyCate, cateBlockID);
                    cateToCopy.Add(cateBlockID, copyCateID = copyCate.persistentID);
                }

                var copyOffline = CopyFixer<PlayerOfflineGearDataBlock>.CreateNewCopy(offlineBlock);
                string baseItemComp = GetCompString(eGearComponent.BaseItem);
                string baseCateComp = GetCompString(eGearComponent.Category);
                copyOffline.GearJSON = copyOffline.GearJSON.Replace(baseItemComp + itemBlockID, baseItemComp + copyItemID);
                copyOffline.GearJSON = copyOffline.GearJSON.Replace(baseCateComp + cateBlockID, baseCateComp + copyCateID);
                TrySetIDOffset(copyOffline, offlineBlock.persistentID);

                _offlineCopies.Add(offlineBlock.persistentID, copyOffline.persistentID);
                _offlineCopies.Add(copyOffline.persistentID, offlineBlock.persistentID);
            }
        }

        private static bool TryGetComp<T>(string gearJSON, eGearComponent comp, out T block) where T : GameDataBlockBase<T>
        {
            var compString = GetCompString(comp);
            int start = gearJSON.IndexOf(compString) + compString.Length;
            if (start >= 0)
            {
                int end = gearJSON.IndexOf('}', start);
                if (uint.TryParse(gearJSON[start..end], out var id) && GameDataBlockBase<T>.HasBlock(id))
                {
                    block = GameDataBlockBase<T>.GetBlock(id);
                    return true;
                }
            }
            block = null!;
            return false;
        }

        private static string GetCompString(eGearComponent comp) => $"\"c\":{(int)comp},\"v\":";

        private static void TrySetIDOffset<T>(T block, uint origID) where T : GameDataBlockBase<T>
        {
            if (Configuration.Offset == 0)
                return;

            var newID = origID + Configuration.Offset;
            if (GameDataBlockBase<T>.HasBlock(newID))
            {
                DinoLogger.Warning($"Failed to give ID {newID} in {GameDataBlockBase<T>.m_fileNameNoExt}! Using {block.persistentID} instead.");
                return;
            }

            // Undo AddBlock logic and set to new ID
            GameDataBlockBase<T>.s_dirtyBlocks.RemoveAt(GameDataBlockBase<T>.s_dirtyBlocks.Count - 1);
            GameDataBlockBase<T>.s_dirtyBlocks.Add(newID);
            GameDataBlockBase<T>.s_blockIDByName[block.name] = newID;
            GameDataBlockBase<T>.s_blockByID.Remove(block.persistentID);
            GameDataBlockBase<T>.s_blockByID.Add(newID, block);
            block.persistentID = newID;
        }

        [HarmonyPatch(typeof(PlayerBackpackManager), nameof(PlayerBackpackManager.EquipLocalGear))]
        [HarmonyWrapSafe]
        [HarmonyPostfix]
        private static void Post_Equip(GearIDRange gearSetup)
        {
            if (!Configuration.LockDuplicate)
                return;

            var itemBlock = ItemDataBlock.GetBlock(gearSetup.GetCompID(eGearComponent.BaseItem));
            if (itemBlock == null)
                return;

            InventorySlot slot;
            switch (itemBlock.inventorySlot)
            {
                case InventorySlot.GearSpecial:
                    slot = InventorySlot.GearStandard;
                    break;
                case InventorySlot.GearStandard:
                    slot = InventorySlot.GearSpecial;
                    break;
                default:
                    return;
            }

            if (!TryGetOfflineID(gearSetup, out var offlineID))
                return;

            bool hasID = _offlineCopies.TryGetValue(offlineID, out var copyID);

            if (_blockedGearRanges.TryGetValue(slot, out (int pos, GearIDRange range) blockPair))
            {
                if (hasID && TryGetOfflineID(blockPair.range, out var blockedID) && blockedID == copyID)
                    return;
                GearManager.Current.m_gearPerSlot[(int)slot].Insert(blockPair.pos, blockPair.range);
                _blockedGearRanges.Remove(slot);
            }

            if (!hasID)
                return;

            var list = GearManager.Current.m_gearPerSlot[(int)slot];
            for (int i = 0; i < list.Count; i++)
            {
                var gearRange = list[i];
                if (!TryGetOfflineID(gearRange, out var gearID))
                    continue;

                if (gearID == copyID)
                {
                    _blockedGearRanges[slot] = (i, gearRange);
                    list.RemoveAt(i);
                    return;
                }
            }
        }

        private static bool TryGetOfflineID(GearIDRange range, out uint offlineID)
        {
            if (!uint.TryParse(range.PlayfabItemInstanceId["OfflineGear_ID_".Length..], out offlineID))
                return false;
            return true;
        }
    }
}
