using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Storm.ExternalEvent;
using Storm.StardewValley.Event;
using Storm.StardewValley.Wrapper;
using Microsoft.Xna.Framework;
using System.IO;
using Storm.StardewValley.Accessor;


namespace NoSoilDecay
{
    [Mod]
    public class NoSoilDecay : DiskResource
    {
        public JsonTerrainFeatures JsonTerrainFeatures { get; set; }
        public int TimeLastSaved { get; set; }
        public int TimeToSaveNext { get; set; }
        public bool HasDeserializedToday { get; set; }
        public string TileDecayJsonFilePath { get; set; }
        public ulong CurrentGameId { get; set; }

        public NoSoilDecay()
        {
            JsonTerrainFeatures = new JsonTerrainFeatures();
        }

        [Subscribe]
        public void Initialize(InitializeEvent @e)
        {
            HasDeserializedToday = false;
        }

        [Subscribe]
        public void DeserializeTerrainFeaturesAfterNewDay(PostNewDayEvent @e)
        {
            HasDeserializedToday = false;
        }

        [Subscribe]
        public void OnGameLoadedEvent(PostGameLoadedEvent @e)
        {
            CurrentGameId = @e.Root.UniqueIDForThisGame;
            TileDecayJsonFilePath = Path.Combine(ParentPathOnDisk + "\\NoSoilDecay\\SavedTiles\\", CurrentGameId.ToString() + ".json");
            CheckOrCreateJsonFile();
        }

        [Subscribe]
        public void HaveSoilDecaySlowerOnNewDay(PreUpdateEvent @e)
        {

            if (@e.Root.HasLoadedGame)
            {
                // Load the saved state the first time exiting the farmhouse of the day.
                if (!HasDeserializedToday && @e.Location.Name == "Farm")
                {
                    var locations = @e.Root.Locations;

                    for (int i = 0; i < @e.Root.Locations.Count; i++)
                    {
                        var loc = locations[i];

                        if (loc.Name == "Farm")
                        {
                            JsonTerrainFeatures.HoeDirtTile = DeserializeSavedTerrain();

                            if (JsonTerrainFeatures.HoeDirtTile != null)
                            {
                                var tFeats = @e.Location.TerrainFeatures;
                                var dirtToCreate = new List<Vector2>();

                                foreach (var j in JsonTerrainFeatures.HoeDirtTile)
                                {
                                    var location = j.Key;
                                    if (!tFeats.ContainsKey(location))
                                    {
                                        dirtToCreate.Add(location);
                                    }
                                }

                                foreach (var d in dirtToCreate)
                                {
                                    @e.Location.AddHoeDirtAt(d);
                                }

                                var tFeatsTest = @e.Location.TerrainFeatures;

                                foreach (var j in JsonTerrainFeatures.HoeDirtTile)
                                {
                                    foreach (var d in dirtToCreate)
                                    {
                                        if (j.Key == d)
                                        {
                                            tFeatsTest[d].As<HoeDirtAccessor, HoeDirt>().Fertilizer = j.Value;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    HasDeserializedToday = true;
                }
            }

            // Save state every 10 in-game minutes
            if (TimeLastSaved == 0 && @e.Root.HasLoadedGame)
            {
                TimeLastSaved = @e.Root.TimeOfDay;
                TimeToSaveNext = TimeLastSaved + 10;
            }

            if (@e.Root.TimeOfDay == 600 && @e.Root.HasLoadedGame)
            {
                TimeLastSaved = @e.Root.TimeOfDay;
                TimeToSaveNext = TimeLastSaved + 10;
            }

            if (@e.Root.TimeOfDay >= TimeToSaveNext && @e.Root.HasLoadedGame)
            {
                TimeLastSaved = @e.Root.TimeOfDay;
                TimeToSaveNext = TimeLastSaved + 10;

                //Perform the save.
                if (@e.Location.Name == "Farm")
                {
                    var tFeats = @e.Location.TerrainFeatures;
                    foreach (var k in tFeats.Keys)
                    {
                        if (tFeats[k].Is<HoeDirtAccessor>())
                        {
                            var temp = tFeats[k].As<HoeDirtAccessor, HoeDirt>();
                            if (temp.Crop == null)
                            {
                                // If HoeDirt doesnt contain a key that TerrainFeatures does, a HoeDirt has been created and needs to be added.
                                if (!JsonTerrainFeatures.HoeDirtTile.ContainsKey(k))
                                {
                                    JsonTerrainFeatures.HoeDirtTile.Add(k, tFeats[k].As<HoeDirtAccessor, HoeDirt>().Fertilizer);
                                }
                                else if (JsonTerrainFeatures.HoeDirtTile != null)
                                {
                                    // If the HoeDirt has a fertilizer, set its value here.
                                    JsonTerrainFeatures.HoeDirtTile[k] = tFeats[k].As<HoeDirtAccessor, HoeDirt>().Fertilizer;
                                }

                                // If HoeDirt contains key that TerrainFeatures doesn't, player has destroyed the tile. Remove from HoeDirt.
                                var dirtToRemove = new List<Vector2>();

                                if (JsonTerrainFeatures.HoeDirtTile != null)
                                {
                                    foreach (var h in JsonTerrainFeatures.HoeDirtTile)
                                    {
                                        if (tFeats[h.Key] == null)
                                        {
                                            dirtToRemove.Add(h.Key);
                                        }
                                    }
                                    foreach (var h in dirtToRemove)
                                    {
                                        if (JsonTerrainFeatures.HoeDirtTile.ContainsKey(h))
                                        {
                                            JsonTerrainFeatures.HoeDirtTile.Remove(h);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (JsonTerrainFeatures.HoeDirtTile != null)
                    {
                        SerializeSavedTerrain(JsonTerrainFeatures.HoeDirtTile);
                    }
                }
            }
        }

        private void SerializeSavedTerrain(Dictionary<Vector2, int> terrain)
        {
            var terrainLocation = Path.Combine(TileDecayJsonFilePath);
            File.WriteAllBytes(terrainLocation, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(terrain)));
        }

        private Dictionary<Vector2, int> DeserializeSavedTerrain()
        {
            var terrainLocation = Path.Combine(TileDecayJsonFilePath);
            var jsonTerrainFeatures = new JsonTerrainFeatures().HoeDirtTile;
            jsonTerrainFeatures = JsonConvert.DeserializeObject<Dictionary<Vector2, int>>(Encoding.UTF8.GetString(File.ReadAllBytes(terrainLocation)));

            if (jsonTerrainFeatures == null)
            {
                var newJTFeat = new JsonTerrainFeatures();
                return newJTFeat.HoeDirtTile;
            }
            return jsonTerrainFeatures;
        }

        private void CheckOrCreateJsonFile()
        {
            if (!Directory.Exists(ParentPathOnDisk + "\\NoSoilDecay\\SavedTiles\\"))
            {
                Directory.CreateDirectory(ParentPathOnDisk + "\\NoSoilDecay\\SavedTiles\\");
            }

            if (!File.Exists(Path.Combine(TileDecayJsonFilePath)))
            {
                File.Create(Path.Combine(TileDecayJsonFilePath));
            }
        }
    }
}
public class JsonTerrainFeatures
{
    public Dictionary<Vector2, int> HoeDirtTile;
    public JsonTerrainFeatures()
    {
        HoeDirtTile = new Dictionary<Vector2, int>();
    }
}



