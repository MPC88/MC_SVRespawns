using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace MC_SVRespawns
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class Main : BaseUnityPlugin
    {
        public const string pluginGuid = "mc.starvalor.respawns";
        public const string pluginName = "SV Respawns";
        public const string pluginVersion = "1.1.0";

        private const string modSaveFolder = "/MCSVSaveData/";  // /SaveData/ sub folder
        private const string modSaveFilePrefix = "Resapwns_"; // modSaveFlePrefixNN.dat

        public static ConfigEntry<int> cfgRavagerRespawnTime;
        public static ConfigEntry<int> cfgStationRespawnTime; // <-- SUNTIKKAN BARIS INI
        private static PersistentData data;

        public void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Main));

            cfgRavagerRespawnTime = Config.Bind<int>(
                "Config",
                "Ravager respawn time",
                30,
                "Ravager respawn time in minutes.");
            cfgStationRespawnTime = Config.Bind<int>(
                "Config",
                "Station respawn time",
                45,
        "Station respawn time in minutes. Player stations from DLC are safe from this calculation.");
        }

        [HarmonyPatch(typeof(AIMarauder), nameof(AIMarauder.Die))]
        [HarmonyPostfix]
        private static void AIMarauderDie_Post(AIMarauder __instance)
        {
            if (__instance.Char.AIType == 4)
            {
                if (data == null)
                    data = new PersistentData();

                data.desroyedRavagers.Add(GameData.data.currentSectorIndex, GameData.timePlayed);
            }
        }
        [HarmonyPatch(typeof(Station), nameof(Station.SetAsDestroyed))]
        [HarmonyPostfix]
        private static void StationSetAsDestroyed_Post(Station __instance, bool __result)
        {
            // Jika stasiun terbukti sukses dihancurkan (bukan stasiun yang memang sudah hancur dari awal)
            if (__result && data != null)
            {
                int currentSector = GameData.data.currentSectorIndex;

                // "Sesetan" taktis: Catat waktu kehancuran stasiun berdasarkan sektornya
                if (!data.destroyedStations.ContainsKey(currentSector))
                {
                    data.destroyedStations.Add(currentSector, GameData.timePlayed);
                    Debug.Log("[SV Respawns] Taktis! Stasiun di Sektor " + currentSector + " hancur, waktu dicatat.");
                }
                else
                {
                    // Jika entah bagaimana kuncinya sudah ada, timpa dengan waktu terbaru
                    data.destroyedStations[currentSector] = GameData.timePlayed;
                }
            }
        }
        [HarmonyPatch(typeof(GameData), nameof(GameData.MovePlayerToStation))]
        [HarmonyPrefix]
        private static void GameDataMovePlayerToStation_Pre(Station station)
        {
            if (data == null)
                return;

            RespawnRavagers();
            RespawnStations();
        }

        [HarmonyPatch(typeof(GameData), nameof(GameData.GoToSector))]
        [HarmonyPrefix]
        private static void GameDataGoToSector_Pre(int X, int Y)
        {
            if (data == null)
                return;

            int sectorIndex = GameData.data.GetSectorIndex(X, Y, -1, false);
            RespawnRavagers();
            RespawnStations();
        }

        private static void RespawnRavagers()
        {
            List<int> remove = new List<int>();

            foreach(int sectorIndex in data.desroyedRavagers.Keys)
            {
                if (sectorIndex < 0 || sectorIndex >= GameData.data.sectors.Count)
                    continue;

                if (GameData.data.sectors[sectorIndex].boss != null &&
                !GameData.data.sectors[sectorIndex].boss.alive &&
                data.desroyedRavagers.TryGetValue(sectorIndex, out float timeDestroyed) &&
                timeDestroyed + (cfgRavagerRespawnTime.Value * 60) <= GameData.timePlayed)
                {
                    TSector sector = GameData.data.sectors[sectorIndex];

                    // Infiltrasi Taktis: Amankan jumlah spawn maksimum 1
                    int totalAmbush = 1;
                    for (int i = 0; i < totalAmbush; i++)
                    {
                        sector.boss.CreateBossShip(sector, sector.GetCoordsForTempObjects(), 0);
                    }

                    sector.boss.alive = true;
                    remove.Add(sectorIndex);
                }

                if (GameData.data.sectors[sectorIndex].boss != null &&
                    GameData.data.sectors[sectorIndex].boss.alive &&
                    data.desroyedRavagers.ContainsKey(sectorIndex))
                    remove.Add(sectorIndex);
            }

            remove.ForEach(x => data.desroyedRavagers.Remove(x));
        }
        private static void RespawnStations()
        {
            if (data == null || data.destroyedStations == null || data.destroyedStations.Count == 0)
                return;

            List<int> remove = new List<int>();

            foreach (int sectorIndex in data.destroyedStations.Keys)
            {
                if (sectorIndex < 0 || sectorIndex >= GameData.data.sectors.Count)
                    continue;

                // Hitung mundur: Jika waktu tunggu di .cfg sudah terpenuhi
                if (data.destroyedStations.TryGetValue(sectorIndex, out float timeDestroyed) &&
                    timeDestroyed + (cfgStationRespawnTime.Value * 60) <= GameData.timePlayed)
                {
                    TSector sector = GameData.data.sectors[sectorIndex];

                    // RITUAL BARU: Pindai daftar ID stasiun di sektor tersebut
                    if (sector.stationIDs != null)
                    {
                        foreach (int stationID in sector.stationIDs)
                        {
                            // Panggil wujud stasiunnya menggunakan mantra GameData
                            Station station = GameData.GetStation(stationID);

                            // Temukan stasiun yang sedang mati/menjadi puing
                            if (station != null && station.Destroyed)
                            {
                                station.Destroyed = false;
                                station.Wrecked = false;

                                // Panggil fungsi bawaan game untuk isi ulang HP
                                station.UpdateHP();

                                Debug.Log("[SV Respawns] Sukses! Stasiun utama faksi di Sektor " + sectorIndex + " (ID: " + stationID + ") berhasil dibangun kembali dari puing-puing.");
                            }
                        }
                    }
                    // Masukkan ke daftar antrean untuk dihapus dari memori "buku kematian"
                    remove.Add(sectorIndex);
                }
            }

            // Bersihkan daftar agar tidak terjadi looping respawn tanpa akhir
            remove.ForEach(x => data.destroyedStations.Remove(x));
        }
        [HarmonyPatch(typeof(MenuControl), nameof(MenuControl.LoadGame))]
        [HarmonyPostfix]
        private static void MenuControlLoadGame_Post()
        {
            LoadData(GameData.gameFileIndex.ToString("00"));
            if (data != null && data.destroyedStations.Count > 0)
                data.destroyedStations.Clear();
        }

        internal static void LoadData(string saveIndex)
        {
            string modData = Application.dataPath + GameData.saveFolderName + modSaveFolder + modSaveFilePrefix + saveIndex + ".dat";
            try
            {
                if (!saveIndex.IsNullOrWhiteSpace() && File.Exists(modData))
                {
                    BinaryFormatter binaryFormatter = new BinaryFormatter();
                    FileStream fileStream = File.Open(modData, FileMode.Open);
                    PersistentData loadData = (PersistentData)binaryFormatter.Deserialize(fileStream);
                    fileStream.Close();

                    if (loadData == null)
                        data = new PersistentData();
                    else
                        data = loadData;
                }
                else
                    data = new PersistentData();
            }
            catch
            {
                SideInfo.AddMsg("<color=red>Respanws mod load failed.</color>");
            }
        }

        [HarmonyPatch(typeof(GameData), nameof(GameData.SaveGame))]
        [HarmonyPrefix]
        private static void GameDataSaveGame_Pre()
        {
            SaveData();
        }

        private static void SaveData()
        {
            if (data == null)
                return;

            string tempPath = Application.dataPath + GameData.saveFolderName + modSaveFolder + "RSTemp.dat";

            if (!Directory.Exists(Path.GetDirectoryName(tempPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath));

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            BinaryFormatter binaryFormatter = new BinaryFormatter();
            FileStream fileStream = File.Create(tempPath);
            binaryFormatter.Serialize(fileStream, data);
            fileStream.Close();

            File.Copy(tempPath, Application.dataPath + GameData.saveFolderName + modSaveFolder + modSaveFilePrefix + GameData.gameFileIndex.ToString("00") + ".dat", true);
            File.Delete(tempPath);
        }

        [HarmonyPatch(typeof(MenuControl), nameof(MenuControl.DeleteSaveGame))]
        [HarmonyPrefix]
        private static void DeleteSave_Pre()
        {            
            if (GameData.ExistsAnySaveFile(GameData.gameFileIndex) &&
                File.Exists(Application.dataPath + GameData.saveFolderName + modSaveFolder + modSaveFilePrefix + GameData.gameFileIndex.ToString("00") + ".dat"))
            {
                File.Delete(Application.dataPath + GameData.saveFolderName + modSaveFolder + modSaveFilePrefix + GameData.gameFileIndex.ToString("00") + ".dat");
            }
        }
    }

    [Serializable]
    public class PersistentData
    {
        public Dictionary<int, float> destroyedStations;
        public Dictionary<int, float> desroyedRavagers;

        public PersistentData()
        {
            destroyedStations = new Dictionary<int, float>();
            desroyedRavagers = new Dictionary<int, float>();
        }
    }
}
