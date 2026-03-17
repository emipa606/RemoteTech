using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;

// just a little shim
namespace HugsLib.Utils;

public static class HugsLibUtility
{
    public static readonly BindingFlags AllBindingFlags =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool ShiftIsHeld => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    public static bool AltIsHeld => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

    public static bool ControlIsHeld => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                                        Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);


    extension(Thing thing)
    {
        public bool HasDesignation(DesignationDef def)
        {
            if (thing.Map == null || thing.Map.designationManager == null)
            {
                return false;
            }

            return thing.Map.designationManager.DesignationOn(thing, def) != null;
        }

        public void ToggleDesignation(DesignationDef def, bool enable)
        {
            if (thing.Map == null || thing.Map.designationManager == null)
            {
                throw new Exception("Thing must belong to a map to toggle designations on it");
            }

            var designation = thing.Map.designationManager.DesignationOn(thing, def);
            switch (enable)
            {
                case true when designation == null:
                    thing.Map.designationManager.AddDesignation(new Designation(thing, def));
                    break;
                case false when designation != null:
                    thing.Map.designationManager.RemoveDesignation(designation);
                    break;
            }
        }
    }


    extension(IEnumerable list)
    {
        public string ListElements()
        {
            return list.Join(", ", true);
        }

        public string Join(string separator, bool explicitNullValues = false)
        {
            if (list == null)
            {
                return "";
            }

            var stringBuilder = new StringBuilder();
            var seenLine = false;
            foreach (var item in list)
            {
                if (seenLine)
                {
                    stringBuilder.Append(separator);
                }

                seenLine = true;
                if (item != null || explicitNullValues)
                {
                    stringBuilder.Append(item != null ? item.ToString() : "[null]");
                }
            }

            return stringBuilder.ToString();
        }
    }


    extension(IntVec3 pos)
    {
        public void ToggleDesignation(DesignationDef def, bool enable, Map map = null)
        {
            map ??= Find.CurrentMap;

            if (map == null || map.designationManager == null)
            {
                throw new Exception("ToggleDesignation requires a map argument or VisibleMap must be set");
            }

            var designation = map.designationManager.DesignationAt(pos, def);
            if (enable && designation == null)
            {
                map.designationManager.AddDesignation(new Designation(pos, def));
            }
            else if (!enable && designation != null)
            {
                map.designationManager.RemoveDesignation(designation);
            }
        }

        public bool HasDesignation(DesignationDef def, Map map = null)
        {
            map ??= Find.CurrentMap;

            if (map == null || map.designationManager == null)
            {
                return false;
            }

            return map.designationManager.DesignationAt(pos, def) != null;
        }
    }

    public static class InjectedDefHasher
    {
        private static GiveShortHash giveShortHashDelegate;

        internal static void PrepareReflection()
        {
            try
            {
                if (typeof(ShortHashGiver)
                        .GetField("takenHashesPerDeftype", BindingFlags.Static | BindingFlags.NonPublic)
                        ?.GetValue(null) is not Dictionary<Type, HashSet<ushort>> takenHashesDictionary)
                {
                    throw new Exception("taken hashes");
                }

                var method = typeof(ShortHashGiver).GetMethod("GiveShortHash",
                    BindingFlags.Static | BindingFlags.NonPublic, null, [
                        typeof(Def),
                        typeof(Type),
                        typeof(HashSet<ushort>)
                    ], null);
                if (method == null)
                {
                    throw new Exception("hashing method");
                }

                var hashDelegate =
                    (GiveShortHashTakenHashes)Delegate.CreateDelegate(typeof(GiveShortHashTakenHashes), method);
                giveShortHashDelegate = delegate(Def def, Type defType)
                {
                    var hashSet = takenHashesDictionary.TryGetValue(defType);
                    if (hashSet == null)
                    {
                        hashSet = [];
                        takenHashesDictionary.Add(defType, hashSet);
                    }

                    hashDelegate(def, defType, hashSet);
                };
            }
            catch (Exception ex)
            {
                Log.Error("Failed to reflect short hash dependencies: " + ex.Message);
            }
        }

        public static void GiveShortHashToDef(Def newDef, Type defType)
        {
            if (giveShortHashDelegate == null)
            {
                throw new Exception("Hasher not initialized");
            }

            giveShortHashDelegate(newDef, defType);
        }

        private delegate void GiveShortHashTakenHashes(Def def, Type defType, HashSet<ushort> takenHashes);

        private delegate void GiveShortHash(Def def, Type defType);
    }
}