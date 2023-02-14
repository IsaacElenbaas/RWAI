using System;

namespace RWAI {
	public partial class RWAI : BepInEx.BaseUnityPlugin {

/*{{{ void WriteIPC(string text)*/
		private System.Net.Sockets.NetworkStream ipc;
		private System.Threading.Tasks.Task lastWrite = null;
		private async void WriteIPC(string text) {
			Byte[] data = System.Text.Encoding.ASCII.GetBytes(text);
			if(lastWrite != null) await lastWrite;
			// TODO: this doesn't actually ensure they arrive in the correct order, even with newlines
			// WriteAsync does not start until awaited
			//lastWrite = System.Threading.Tasks.Task.Run(() => ipc.Write(data, 0, data.Length));
			ipc.Write(data, 0, data.Length);
		}
/*}}}*/

/*{{{ void ResetDeath(Player player)*/
		void ResetDeath(Player player) {
			foreach(Creature.Grasp g in new System.Collections.Generic.List<Creature.Grasp>(player.grabbedBy)) {
				g.grabber.LoseAllGrasps();
			}
			player.abstractCreature.LoseAllStuckObjects();
			player.airInLungs = 1;
			player.drown = 0;
			player.lungsExhausted = false;
			player.slowMovementStun = 0;
			player.rainDeath = 0;

			if(player.room != null && !player.room.abstractRoom.offScreenDen) {
				if(player.spearOnBack != null && player.spearOnBack.spear != null) player.spearOnBack.DropSpear();
				player.LoseAllGrasps();
				player.Regurgitate();
				if(player.spearOnBack != null) player.spearOnBack.SpearToHand(true);
				player.LoseAllGrasps();
			}
		}
/*}}}*/

/*{{{ void TeleportNewRoom(World world)*/
		int regionCooldown = 0;
		private void TeleportNewRoom(World world) {
			// try not to over-train on large regions (could be using number of rooms in new region)
			if(regionCooldown < 20) {
				regionCooldown++;
				WorldCoordinate origin;
				Player player = world.game.Players[0].realizedCreature as Player;
				Room playerRoom = player.room;
				if(playerRoom != null) {
					origin = (world.game.Players[0] as AbstractCreature).pos;
					playerRoom.RemoveObject(player);
					playerRoom.CleanOutObjectNotInThisRoom(player);
				}
				else origin = new WorldCoordinate();
				AbstractRoom room = world.abstractRooms[(new Random()).Next(world.abstractRooms.Length)];
				while((playerRoom != null && room == playerRoom.abstractRoom) || room.offScreenDen || room.connections.Length <= 0 || room.name.Contains("GATE") || System.Text.RegularExpressions.Regex.Match(room.name, @"_S[0-9]").Success) {
					room = world.abstractRooms[(world.abstractRooms.IndexOf(room)+1)%world.abstractRooms.Length];
				}
				if(room.realizedRoom == null) room.world.ActivateRoom(room);
				world.game.shortcuts.CreatureTeleportOutOfRoom(player, origin, new WorldCoordinate(room.index, -1, -1, 0));
			}
			else {
				regionCooldown = 0;
				Region[] regions = world.game.overWorld.regions;
				string target = regions[(regions.IndexOf(world.game.world.region)+1)%regions.Length].name;
				world.game.overWorld.worldLoader = new WorldLoader(null, world.game.overWorld.PlayerCharacterNumber, false, target, world.game.overWorld.GetRegion(target), world.game.setupValues, WorldLoader.LoadingContext.FASTTRAVEL);
				world.game.overWorld.worldLoader.NextActivity();
				while(!world.game.overWorld.worldLoader.Finished) {
					world.game.overWorld.worldLoader.Update();
					System.Threading.Thread.Sleep(1);
				}
				AbstractRoom[] rooms = world.game.overWorld.worldLoader.ReturnWorld().abstractRooms;
				AbstractRoom room = rooms[(new Random()).Next(rooms.Length)];
				while(room.offScreenDen || room.connections.Length <= 0 || room.name.Contains("GATE") || System.Text.RegularExpressions.Regex.Match(room.name, @"_S[0-9]").Success) {
					room = rooms[(rooms.IndexOf(room)+1)%rooms.Length];
				}
				world.game.overWorld.reportBackToGate = null;
				world.game.overWorld.specialWarpCallback = null;
				world.game.overWorld.currentSpecialWarp = OverWorld.SpecialWarpType.WARP_SINGLEROOM;
				world.game.overWorld.singleRoomWorldWarpGoal = room.name;
				world.game.overWorld.worldLoader = new WorldLoader(world.game, world.game.overWorld.PlayerCharacterNumber, false, target, world.game.overWorld.GetRegion(target), world.game.setupValues);
				world.game.overWorld.worldLoader.NextActivity();
				// not necessary but why waste resources rendering?
				while(!world.game.overWorld.worldLoader.Finished) {
					world.game.overWorld.worldLoader.Update();
					System.Threading.Thread.Sleep(1);
				}
			}
		}
/*}}}*/

/*{{{ void TeleportInRoom(Room room)*/
		private void TeleportInRoom(Room room) {
			int[] tiles = new int[room.Width*room.Height];
			for(int i = 0; i < tiles.Length; i++) { tiles[i] = i; }
			Random tileRand = new Random();
			for(int i = 0; i < tiles.Length; i++) {
				int j = tileRand.Next(0, tiles.Length);
				(tiles[i], tiles[j]) = (tiles[j], tiles[i]);
			}
			int teleportTile = -1;
			foreach(int tile in tiles) {
				Room.Tile tileTile = room.Tiles[tile%room.Width, tile/room.Width];
				if(tileTile.Terrain != Room.Tile.TerrainType.Solid &&
				   tileTile.Terrain != Room.Tile.TerrainType.Floor &&
				   tileTile.Terrain != Room.Tile.TerrainType.Slope
				) continue;
				bool good = true;
				for(int j = 1; j <= 3; j++) {
					if(tile/room.Width+j >= room.Height) {
						good = false; break;
					}
					for(int i = -1; i <= 1; i++) {
						if(tile%room.Width+i < 0 || tile%room.Width+i >= room.Width) {
							good = false; break;
						}
						Room.Tile tileTile2 = room.Tiles[tile%room.Width+i, tile/room.Width+j];
						if(tileTile2.Terrain == Room.Tile.TerrainType.Solid ||
						   tileTile2.Terrain == Room.Tile.TerrainType.Floor ||
						   tileTile2.Terrain == Room.Tile.TerrainType.Slope
						) {
							good = false; break;
						}
						// backup plan
						teleportTile = tile+i+j*room.Width;
					}
					if(!good) break;
				}
				if(good) {
					teleportTile = tile+2*room.Width;
					break;
				}
			}
			if(teleportTile == -1) throw new ArgumentException("RWAI: " + room.abstractRoom.name + " has no air");
			(room.game.Players[0].realizedCreature as Player).SuperHardSetPosition(room.MiddleOfTile(teleportTile%room.Width, teleportTile/room.Width));
		}
/*}}}*/

/*{{{ bool CreatureDangerous(AbstractCreatue creature)*/
		// see OverseerCommunicationModule.CreatureDangerScore
		private bool CreatureDangerous(AbstractCreature creature) {
			if(creature.creatureTemplate.smallCreature)
				return false;
			if(creature.creatureTemplate.dangerousToPlayer == 0 &&
			   creature.creatureTemplate.type != CreatureTemplate.Type.Scavenger
			) return false;
			return true;
		}
/*}}}*/

/*{{{ death detection/prevention*/
		private void Player_Die(On.Player.orig_Die orig, Player self) { JustDied(self.abstractCreature.world.game); }
		private void AbstractCreature_Die(On.AbstractCreature.orig_Die orig, AbstractCreature self) {
			if(self.realizedCreature is Player) JustDied(self.world.game);
			else orig(self);
		}
		private void RainWorldGame_GameOver(On.RainWorldGame.orig_GameOver orig, RainWorldGame self, Creature.Grasp dependentOnGrasp) { JustDied(self.world.game); }
		private void UpdatableAndDeletable_Destroy(On.UpdatableAndDeletable.orig_Destroy orig, UpdatableAndDeletable self) {
			if(self is Player) JustDied(self.room.world.game);
			else orig(self);
		}
		private void Room_AddObject(On.Room.orig_AddObject orig, Room self, UpdatableAndDeletable obj) {
			if(obj is Player && self.abstractRoom.offScreenDen) JustDied(self.world.game);
			else orig(self, obj);
		}
		private void AbstractRoom_MoveEntityToDen(On.AbstractRoom.orig_MoveEntityToDen orig, AbstractRoom self, AbstractWorldEntity ent) {
			if(ent is AbstractCreature && (ent as AbstractCreature).realizedCreature is Player) {
				// justDied->ResetDeath won't fix being killed by a pole plant/worm grass, need full teleport
				// janky hack mate - don't want to do full https://github.com/henpemaz/RemixMods/blob/master/MapWarp/MapWarp.cs#L389 and this seems fine?
				((ent as AbstractCreature).realizedCreature as Player).inShortcut = false;
				deathCooldown = 0;
				JustDied(self.world.game);
			}
			else orig(self, ent);
		}
		private void PhysicalObject_Update(On.PhysicalObject.orig_Update orig, PhysicalObject self, bool eu) {
			orig(self, eu);
			if(self is Player) {
				self.CollideWithObjects = true;
				typeof(PhysicalObject).GetProperty("CollideWithSlopes").GetSetMethod(nonPublic: true).Invoke(self, new object[1] { true });
				self.CollideWithTerrain = true;
			}
		}
/*}}}*/

/*{{{ ShortcutHandler_TeleportingCreatureArrivedInRealizedRoom*/
		// https://github.com/henpemaz/RemixMods/blob/master/MapWarp/MapWarp.cs
		// lets Player use CreatureTeleportOutOfRoom
		private void ShortcutHandler_TeleportingCreatureArrivedInRealizedRoom(On.ShortcutHandler.orig_TeleportingCreatureArrivedInRealizedRoom orig, ShortcutHandler self, ShortcutHandler.TeleportationVessel tVessel) {
			try { orig(self, tVessel); }
			catch(System.NullReferenceException) {
				if(!(tVessel.creature is ITeleportingCreature)) {
					WorldCoordinate arrival = tVessel.destination;
					if(!arrival.TileDefined) {
						arrival = tVessel.room.realizedRoom.LocalCoordinateOfNode(tVessel.entranceNode);
						arrival.abstractNode = tVessel.entranceNode;
					}
					tVessel.creature.abstractCreature.pos = arrival;
					tVessel.creature.SpitOutOfShortCut(arrival.Tile, tVessel.room.realizedRoom, true);
				}
			}
		}
/*}}}*/

	}
}
