using System;

namespace RWAI {
	public partial class RWAI : BepInEx.BaseUnityPlugin {
		// TODO: there's HitByWeapon and Violence triggers for most things, could hook Creature's maybe?
		//       also Rock.HitSomething and probably spear/Weapon equivalent

/*{{{ void RoomData(Room room)*/
		private void RoomData(Room room) {
			System.Text.StringBuilder data = new System.Text.StringBuilder();
			data.Append('R' + room.abstractRoom.name +
				'|' + room.Width + 'x' + room.Height +
				'|' + ((room.water) ? room.defaultWaterLevel.ToString() : "0") +
				// TODO: improve
				'|' + ((room.gravity == 0) ? '1' : '0')
			);
			data.Append('|');
			for(int j = 0; j < room.Height; j++) {
				for(int i = 0; i < room.Width; i++) {
					Room.Tile tile = room.Tiles[i, j];
					data.Append(
						  (tile.Terrain == Room.Tile.TerrainType.Solid && tile.shortCut == 0)            ? '2'
						: (tile.Terrain == Room.Tile.TerrainType.Floor ||
						   tile.Terrain == Room.Tile.TerrainType.Slope)                                  ? '1'
						: (tile.AnyBeam ||
						   tile.Terrain == Room.Tile.TerrainType.ShortcutEntrance || tile.shortCut != 0) ? '3'
						: '0'
					);
				}
			}
			data.Append('\n');
			// fix room.shortcuts being null on initial load, isn't when going to new regions
			while(!room.fullyLoaded) {
				while(!room.game.world.loadingRooms[0].done) room.game.world.loadingRooms[0].Update();
				room.game.world.loadingRooms.RemoveAt(0);
				System.Threading.Thread.Sleep(1);
			}
			ShortcutData shortcut = room.shortcuts[(new Random()).Next(room.shortcuts.Length)];
			RWCustom.IntVector2 vesselPos = shortcut.StartTile;
			bool goodGoal = false;
			// prevent infinite loop in rooms with only one pipe that leaves the room
			bool seenPlayer = false;
			while(!goodGoal) {
				goodGoal = true;
				while(shortcut.shortCutType != ShortcutData.Type.RoomExit) {
					goodGoal = false;
					shortcut = room.shortcuts[(Array.IndexOf(room.shortcuts, shortcut)+1)%room.shortcuts.Length];
				}
				if(room.world.game.Players[0].realizedCreature.inShortcut) {
					System.Collections.Generic.List<ShortcutHandler.ShortCutVessel> transportVessels = room.world.game.shortcuts.transportVessels;
					for(int i = transportVessels.Count-1; i >= 0; i--) {
						if(transportVessels[i].creature is Player) {
							// make sure we have a copy
							vesselPos = new RWCustom.IntVector2(transportVessels[i].pos.x, transportVessels[i].pos.y);
							RWCustom.IntVector2 lastVesselPos = vesselPos;
							RWCustom.IntVector2 lastVesselPos2;
							while(room.GetTile(vesselPos).Terrain != Room.Tile.TerrainType.ShortcutEntrance) {
								lastVesselPos2 = vesselPos;
								vesselPos = ShortcutHandler.NextShortcutPosition(vesselPos, lastVesselPos, room);
								lastVesselPos = lastVesselPos2;
							}
							if(vesselPos.x == shortcut.StartTile.x && vesselPos.y == shortcut.StartTile.y && !seenPlayer) {
								goodGoal = false;
								seenPlayer = true;
								shortcut = room.shortcuts[(Array.IndexOf(room.shortcuts, shortcut)+1)%room.shortcuts.Length];
							}
							break;
						}
					}
				}
			}
			goal = shortcut.connection.destinationCoord.room;
			data.Append('S' + vesselPos.x.ToString() + ',' + vesselPos.y.ToString() + '|' + shortcut.StartTile.x.ToString() + ',' + shortcut.StartTile.y.ToString() + '\n');
			WriteIPC(data.ToString());
		}
/*}}}*/

/*{{{ void MainData(World world, bool userInput)*/
		private void MainData(World world, bool userInput) {
			Player player = world.game.Players[0].realizedCreature as Player;
			System.Text.StringBuilder data = new System.Text.StringBuilder();
			data.Append('T' + world.rainCycle.cycleLength.ToString() +
			            ',' + world.rainCycle.timer.ToString() +
			'\n');

			// TODO: ShortCutVessel looks like it would be near what I need to give it time until comes out of pipe to learn to boost
			//       see ShortcutHandler.Update CheckJumpButton
			//       maybe current Vessel distance from dest
			// TODO: give time in room, 0 to 1 with 1=60s or smth
			//       can calculate in C by time since last room message
			//       OOH, make that time pressure to get out! How long before killed and punished
			//       then that can be the "explore or get moving" setting when making a run
	/*{{{ player*/
			// walking is 4.2, crawling is 2.5
			// boosting out of a pipe is 14
			// eihop is 26 then a lot of slowly decreasing from 16
			// eslide is a lot of 20 then a lot of slowly decreasing from 13
			// slide is a good amount of 18s
			// y is 1.5 at rest (negative gravity acceleration applied before velocity?)
			// jumping is 10.18 on first frame
			// jump stored QCTJ is still only 14, just stays high for long
			// TODO: TL;DR will have to give bonus points for movement by cumulative over the last three seconds or smth
			//       give for both displacement and average speed over that time
			// blocks - 0,0 is bottom left, set to -1,-1 when in pipe
			data.Append('P' + player.coord.x.ToString() + ',' + player.coord.y.ToString() +
			// pos and vel are in pixels - 0,0 is bottom left
			// TODO: position on block might not be working - switches at 0.85 in y
			//       alternatively, may be average position of body chunks as x seemed to not work when crawling
			            '|' + ((player.mainBodyChunk.pos.x%20)/20).ToString("F3") + ',' +
			                  ((player.mainBodyChunk.pos.y%20)/20).ToString("F3") +
			            '|' + (player.mainBodyChunk.vel.x/20).ToString("F3") + ',' +
			                  (player.mainBodyChunk.vel.y/20).ToString("F3")
			);

		/*{{{ bodyMode and animation*/
			data.Append('|'); switch(player.bodyMode.value) {
				case "Default":           data.Append(0); break;
				case "Crawl":             data.Append(1); break;
				case "Stand":             data.Append(2); break;
				case "CorridorClimb":     data.Append(3); break;
				case "ClimbIntoShortCut": data.Append(4); break;
				case "WallClimb":         data.Append(5); break;
				case "ClimbingOnBeam":    data.Append(6); break;
				case "Swimming":          data.Append(7); break;
				case "ZeroG":             data.Append(8); break;
				case "Stunned":           data.Append(9); break;
				default: throw new ArgumentOutOfRangeException("RWAI: Unexpected bodyMode");
			}
			data.Append('|'); switch(player.animation.value) {
				case "None":                  data.Append( 0); break;
				case "CrawlTurn":             data.Append( 1); break;
				case "StandUp":               data.Append( 2); break;
				case "DownOnFours":           data.Append( 3); break;
				case "LedgeCrawl":            data.Append( 4); break;
				case "LedgeGrab":             data.Append( 5); break;
				case "HangFromBeam":          data.Append( 6); break;
				case "GetUpOnBeam":           data.Append( 7); break;
				case "StandOnBeam":           data.Append( 8); break;
				case "ClimbOnBeam":           data.Append( 9); break;
				case "GetUpToBeamTip":        data.Append(10); break;
				case "HangUnderVerticalBeam": data.Append(11); break;
				case "BeamTip":               data.Append(12); break;
				case "CorridorTurn":          data.Append(13); break;
				case "SurfaceSwim":           data.Append(14); break;
				case "DeepSwim":              data.Append(15); break;
				case "Roll":                  data.Append(16); break;
				case "Flip":                  data.Append(17); break;
				case "RocketJump":            data.Append(18); break;
				case "BellySlide":            data.Append(19); break;
				case "AntlerClimb":           data.Append(20); break;
				case "GrapplingSwing":        data.Append(21); break;
				case "ZeroGSwim":             data.Append(22); break;
				case "ZeroGPoleGrab":         data.Append(23); break;
				case "VineGrab":              data.Append(24); break;
				default: throw new ArgumentOutOfRangeException("RWAI: Unexpected animation");
			}
		/*}}}*/

			data.Append('|' + player.airInLungs.ToString("F3"));

		/*{{{ stomach and grasped items*/
			data.Append('|' + ((player.objectInStomach == null) ? 0 : 1).ToString()); // don't want to add the chars
			for(int i = 0; i < 2; i++) {
				if(player.grasps[0] != null &&
				   player.Grabability(player.grasps[0].grabbed) != Player.ObjectGrabability.OneHand &&
				   player.Grabability(player.grasps[0].grabbed) != Player.ObjectGrabability.BigOneHand
				) data.Append("|1,0,0");
				else if(player.grasps[i] != null) {
					data.Append("|1," + (
						  ((
								player.grasps[i].grabbed is IPlayerEdible &&
								(player.grasps[i].grabbed as IPlayerEdible).FoodPoints > 0
							) || (player.grasps[i].grabbed is Creature && (
								(player.grasps[i].grabbed as Creature).Template.type == CreatureTemplate.Type.Fly ||
								(player.CanEatMeat(player.grasps[i].grabbed as Creature) && (player.grasps[i].grabbed as Creature).Template.meatPoints > 0)
						)))                                            ?       8
						: (player.grasps[i].grabbed is ExplosiveSpear) ? 1|2|4
						: (player.grasps[i].grabbed is Spear)          ? 1|2
						: (player.grasps[i].grabbed is ScavengerBomb)  ? 1  |4
						: (
							player.grasps[i].grabbed is Weapon &&
							(player.grasps[i].grabbed as Weapon).HeavyWeapon
						)                                              ? 1
						: 0
					) + ',' + (player.CanBeSwallowed(player.grasps[i].grabbed) ? 1 : 0));
				}
				else data.Append("|0,0,0");
			}
			if(player.pickUpCandidate == null) data.Append("|0,0");
			else data.Append("|1," + (
				  ((
						player.pickUpCandidate is IPlayerEdible &&
						(player.pickUpCandidate as IPlayerEdible).FoodPoints > 0
					) || (player.pickUpCandidate is Creature && (
						(player.pickUpCandidate as Creature).Template.type == CreatureTemplate.Type.Fly ||
						(player.CanEatMeat(player.pickUpCandidate as Creature) && (player.pickUpCandidate as Creature).Template.meatPoints > 0)
				)))                                            ?       8
				: (player.pickUpCandidate is ExplosiveSpear) ? 1|2|4
				: (player.pickUpCandidate is Spear)          ? 1|2
				: (player.pickUpCandidate is ScavengerBomb)  ? 1  |4
				: (
					player.pickUpCandidate is Weapon &&
					(player.pickUpCandidate as Weapon).HeavyWeapon
				)                                              ? 1
				: 0
			));
		/*}}}*/

			data.Append('|' + (player.MaxFoodInStomach-player.FoodInStomach).ToString());
			data.Append('\n');
	/*}}}*/

	/*{{{ enemy, food, item vision*/
			if(player.room != null) {
				char[] creatureVisionOut = (new string('0', creatureVision*creatureVision)).ToCharArray();
				char[]     foodVisionOut = (new string('0',        foodVision* foodVision)).ToCharArray();
				char[]     itemVisionOut = (new string('0',        itemVision* itemVision)).ToCharArray();

		/*{{{ FillBodyChunk(BodyChunk b, char id, int vision, char[] dest)*/
				Action<BodyChunk, char, int, char[]> FillBodyChunk = (b, id, vision, dest) => {
					// ensure that BodyChunks cannot fit between points on checked grid
					// <= is used, but opposite Ceiling/Floor makes this still necessary
					float rad = System.Math.Max(20/2, b.rad);
					UnityEngine.Vector2 dist = b.pos-player.mainBodyChunk.pos;
					if(dist.magnitude-rad > 20*vision) return;
					for(int y = (int)System.Math.Ceiling((dist.y-rad)/20); y <= (int)System.Math.Floor((dist.y+rad)/20); y++) {
						// width of BodyChunk's circle at elevation y
						float width = (float)System.Math.Sqrt(rad*rad+System.Math.Pow(20*y-dist.y, 2));
						for(int x = (int)System.Math.Ceiling((dist.x-width)/20); x <= (int)System.Math.Floor((dist.x+width)/20); x++) {
							if(System.Math.Min(x, y) < -vision/2 || System.Math.Max(x, y) >= vision/2) continue;
							dest[(y+vision/2)*vision+(x+vision/2)] = id;
						}
					}
				};
		/*}}}*/

				foreach(AbstractCreature c in player.room.abstractRoom.creatures) {
					if(c == player.abstractCreature ||
					   c.realizedCreature == null ||
					   c.realizedCreature.Template.type == CreatureTemplate.Type.Overseer
					) continue;
					bool dangerous = c.state.alive && CreatureDangerous(c);
					float mass = 0;
					foreach(BodyChunk b in c.realizedCreature.bodyChunks) {
						mass += b.mass;
						if(mass >= 0.35) break;
					}
					if(dangerous || mass >= 0.35) {
						foreach(BodyChunk b in c.realizedCreature.bodyChunks) {
							FillBodyChunk(b, (!dangerous) ? '1' : '2', creatureVision, creatureVisionOut);
						}
					}
					if(c.realizedCreature.Template.type == CreatureTemplate.Type.Fly ||
					   (player.CanEatMeat(c.realizedCreature) && c.realizedCreature.Template.meatPoints > 0)
					) {
						foreach(BodyChunk b in c.realizedCreature.bodyChunks) {
							FillBodyChunk(b, '1',  foodVision,  foodVisionOut);
						}
					}
				}
				foreach(AbstractWorldEntity e in player.room.abstractRoom.entities) {
					if(!(e is AbstractPhysicalObject) || (e as AbstractPhysicalObject).realizedObject == null) continue;
					if(e is AbstractConsumable &&
					   (e as AbstractConsumable).realizedObject is IPlayerEdible &&
					   ((e as AbstractConsumable).realizedObject as IPlayerEdible).FoodPoints > 0
					) {
						foreach(BodyChunk b in (e as AbstractConsumable).realizedObject.bodyChunks) {
							FillBodyChunk(b, '1', foodVision, foodVisionOut);
						}
					}
					else if((e as AbstractPhysicalObject).realizedObject is Weapon &&
					        (player.grasps[0] == null || (e as AbstractPhysicalObject).realizedObject != player.grasps[0].grabbed) &&
					        (player.grasps[1] == null || (e as AbstractPhysicalObject).realizedObject != player.grasps[1].grabbed)
					) {
						foreach(BodyChunk b in (e as AbstractPhysicalObject).realizedObject.bodyChunks) {
							FillBodyChunk(b, (
								  ((e as AbstractPhysicalObject).realizedObject is ExplosiveSpear)     ? '2'
								: ((e as AbstractPhysicalObject).realizedObject is Spear)              ? '2'
								: ((e as AbstractPhysicalObject).realizedObject is ScavengerBomb)      ? '2'
								: ((e as AbstractPhysicalObject).realizedObject as Weapon).HeavyWeapon ? '1'
								: '0'
							), itemVision, itemVisionOut);
						}
					}
				}
				/*for(int i = itemVision-1; i >= 0; i--) {
					data.Append((new string(itemVisionOut)).Substring(i*itemVision, itemVision) + '\n');
				}//*/
				data.Append("C|" + new string(creatureVisionOut) + '\n');
				data.Append("F|" + new string(    foodVisionOut) + '\n');
				data.Append("I|" + new string(    itemVisionOut) + '\n');
			}
			else data.Append("C-\nF-\nI-\n");
	/*}}}*/

			WriteIPC(data.ToString() + ((!userInput) ? "--" : "-|") + "\n");
		}
/*}}}*/

	}
}
