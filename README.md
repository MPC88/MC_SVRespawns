# MC_SVRespawns  
  
Backup your save before using any mods.  
  
Uninstall any mods and attempt to replicate issues before reporting any suspected base game bugs on official channels.  

Function 
=======  
Stations and ravagers respawn after configurable timer (default: 30 minutes).  
  
Stations will respawn with the same faction, but at new random x, y coordinates and undiscovered.  
  
Ravagers will respawn at new x, y coordinates and with a new randomly generated ship/loadout etc.  

NOTE: This is not retroactive.  Only things destroyed after install will respawn.  
  
Install  
=======  
1. Install BepInEx - https://docs.bepinex.dev/articles/user_guide/installation/index.html Stable version 5.4.21 x86.  
2. Run the game at least once to initialise BepInEx and quit.  
3. Download latest mod release.  
4. Place MC_SVRespawns.dll .\SteamLibrary\steamapps\common\Star Valor\BepInEx\plugins\  
  
Config  
=====  
Once you have run the game with the mod once, a new file mc.starvalor.respawns.cfg will be created in .\SteamLibrary\steamapps\common\Star Valor\BepInEx\config.  
  
Station Respawn Time = time in minutes for a station to respawn (default 30 minutes)
Ravager respawn timer = time in minutes for a ravager to respaw (default 30 minutes)
  
