using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace VisitSettlements
{
    public class WorldComponent_SettlementData : WorldComponent
    {
        public Dictionary<int, MapParent> settlementMapParents = new Dictionary<int, MapParent>();
        public Dictionary<int, Map> settlementMaps = new Dictionary<int, Map>();
        public HashSet<ThingWithComps> settlementItems = new HashSet<ThingWithComps>();
        public List<Building> settlementStructures = new List<Building>();

        public WorldComponent_SettlementData(World world) : base(world) { }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref settlementMapParents, "settlementMapParents", LookMode.Value, LookMode.Reference, ref settlementMapParentKeys, ref settlementMapParentValues);
            Scribe_Collections.Look(ref settlementMaps, "settlementMaps", LookMode.Value, LookMode.Reference, ref settlementMapKeys, ref settlementMapValues);
            Scribe_Collections.Look(ref settlementItems, "settlementItems", LookMode.Reference);
            Scribe_Collections.Look(ref settlementStructures, "settlementStructures", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
                ValidateData();
			}
        }

        List<int> settlementMapKeys = new List<int>();
        List<Map> settlementMapValues = new List<Map>();

        List<int> settlementMapParentKeys = new List<int>();
        List<MapParent> settlementMapParentValues = new List<MapParent>();

        private void ValidateData()
        {
            if (settlementMapParents != null)
			{
                var nullParentKeys = settlementMapParents.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
                foreach (var key in nullParentKeys)
                {
                    Log.Warning($"[Visit Settlements] Removing null MapParent entry for tile {key}");
                    if (settlementMaps.TryGetValue(key, out var map))
				    {
                        Current.Game.DeinitAndRemoveMap(map, true);
                    }
                    settlementMapParents.Remove(key);
                }
			}

            if (settlementMaps != null)
			{
                var nullMapKeys = settlementMaps.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
                foreach (var key in nullMapKeys)
                {
                    Log.Warning($"[Visit Settlements] Removing null Map entry for tile {key}");
                    if (settlementMaps.TryGetValue(key, out var map))
                    {
                        Current.Game.DeinitAndRemoveMap(map, true);
                    }
                    settlementMaps.Remove(key);
                }
			}

            if (settlementItems != null)
			{
                settlementItems.RemoveWhere(item => item == null);
			}

            if (settlementStructures != null)
			{
                settlementStructures.RemoveAll(structure => structure == null);
			}
        }
    }
}
