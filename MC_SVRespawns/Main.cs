using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using static System.Collections.Specialized.BitVector32;

namespace MC_SVRespawns
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class Main : BaseUnityPlugin
    {
        public const string pluginGuid = "mc.starvalor.respawns";
        public const string pluginName = "SV Respawns";
        public const string pluginVersion = "1.0.2";

        private const string modSaveFolder = "/MCSVSaveData/";  // /SaveData/ sub folder
        private const string modSaveFilePrefix = "Resapwns_"; // modSaveFlePrefixNN.dat

        public static ConfigEntry<int> cfgStationRespawnTime;
        public static ConfigEntry<int> cfgRavagerRespawnTime;

        private static PersistentData data;

        public void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Main));

            cfgStationRespawnTime = Config.Bind<int>(
                "Config",
                "Station respawn time",
                30,
                "Station respawn time in minutes.");

            cfgRavagerRespawnTime = Config.Bind<int>(
                "Config",
                "Ravager respawn time",
                30,
                "Ravager respawn time in minutes.");
        }

        [HarmonyPatch(typeof(StationSystem), nameof(StationSystem.DestroyStation))]
        [HarmonyPostfix]
        private static void StationSystemDestroyStation_Post(int id)
        {
            if (id < 0 || id >= GameData.data.stationList.Count)
                return;

            if (data == null)
                data = new PersistentData();

            data.destroyedStations.Add(id, GameData.timePlayed);
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

        [HarmonyPatch(typeof(GameData), nameof(GameData.MovePlayerToStation))]
        [HarmonyPrefix]
        private static void GameDataMovePlayerToStation_Pre(Station station)
        {
            if (data == null)
                return;

            RespawnStations();
            RespawnRavagers();
        }

        [HarmonyPatch(typeof(GameData), nameof(GameData.GoToSector))]
        [HarmonyPrefix]
        private static void GameDataGoToSector_Pre(int X, int Y)
        {
            if (data == null)
                return;

            RespawnStations();
            RespawnRavagers();
        }

        private static void RespawnStations()
        {
            List<int> respawned = new List<int>();
            List<int> affectedSectors = new List<int>();

            foreach(int stationID in data.destroyedStations.Keys)
            {
                if (data.destroyedStations[stationID] + (cfgStationRespawnTime.Value * 60) <= GameData.timePlayed)
                {
                    Station station = GameData.data.stationList[stationID];
                    TSector sector = GameData.data.sectors[station.sectorIndex];
                    affectedSectors.Add(station.sectorIndex);
                    Coordenates c = sector.GetMainCoords(true);
                    station.x = c.x;
                    station.y = c.y;
                    station.yRotation = (float)GameData.data.stationList[stationID].GenRand.Next(1, 360);
                    station.discovered = false;
                    station.destroyed = false;
                    respawned.Add(stationID);                    
                }

                if (!GameData.data.stationList[stationID].destroyed && data.destroyedStations.ContainsKey(stationID))
                    respawned.Add(stationID);
            }

            foreach (int id in respawned)
                data.destroyedStations.Remove(id);

            foreach(int id in affectedSectors)
            {
                TSector sector = GameData.data.sectors[id];
                List<int> stationIDs = sector.stationIDs;
                int[] factionPower = new int[6];
                int cur = 0;

                foreach (int stID in stationIDs)
                {
                    if(GameData.data.stationList[stID].factionIndex > -1 &&
                        GameData.data.stationList[stID].factionIndex < 7)
                    factionPower[GameData.data.stationList[stID].factionIndex]++;

                    if(factionPower[GameData.data.stationList[stID].factionIndex] > cur)
                    {
                        sector.factionControl = GameData.data.stationList[stID].factionIndex;
                        cur = factionPower[GameData.data.stationList[stID].factionIndex];
                    }
                }
            }
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
                    sector.boss.CreateBossShip(sector, sector.GetCoordsForTempObjects(), 0);
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

        [HarmonyPatch(typeof(MenuControl), nameof(MenuControl.LoadGame))]
        [HarmonyPostfix]
        private static void MenuControlLoadGame_Post()
        {
            LoadData(GameData.gameFileIndex.ToString("00"));
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
