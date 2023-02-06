//#define NOHOOKINPUT

using System;

[assembly: System.Security.Permissions.SecurityPermission(System.Security.Permissions.SecurityAction.RequestMinimum, SkipVerification = true)]

namespace RWAI {
	[BepInEx.BepInPlugin("com.isaacelenbaas.rwai", "RWAI", "1.0.0")]

	// TODO: check out Room.PlaySound (or SoundLoader?) to see if audio is at all possible
	public class RWAI : BepInEx.BaseUnityPlugin {
		const int frameBacklog = 20;
		const int frameProcessorThreads = 3;
		const float fpsMult = 1;
		const int enemyVision = 40; // centered on slugcat - 20 blocks in any direction
		const int  foodVision = 20; // centered on slugcat - 10 blocks in any direction
		const int  itemVision = 20; // centered on slugcat - 10 blocks in any direction

		// TODO: set to true so loads in random room?
		bool justDied = false;
		int deathCooldown = 0;
		bool sendData = true;

/*{{{ init*/
		public void Awake() { On.RainWorld.OnModsInit += OnModsInit; }
		public void OnEnable() { On.RainWorld.OnModsInit += OnModsInit; }
		static bool initialized = false;
		private void OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self) {
			orig(self);
			if(initialized) return; initialized = true;
			if(enemyVision/2 != enemyVision/2.0) throw new ArgumentException("RWAI: enemyVision must be divisible by two");
			try {
				System.Net.Sockets.TcpClient ipcClient = new System.Net.Sockets.TcpClient("127.0.0.1", 8319);
				ipc = ipcClient.GetStream();
				try {
					System.Net.Sockets.TcpClient ipcRecordClient = new System.Net.Sockets.TcpClient("127.0.0.1", 8325);
					ipcRecord = ipcRecordClient.GetStream();
					// training will still connect video, just never set record
					UnityEngine.QualitySettings.vSyncCount = 0;
					UnityEngine.Application.targetFrameRate = -1;
				}
				catch {
					ipcRecord = null;
				}
				On.ProcessManager.Update += ProcessManager_Update;
				On.RoomCamera.DrawUpdate += RoomCamera_DrawUpdate;
				On.RoomCamera.ApplyPositionChange += RoomCamera_ApplyPositionChange;
#if !NOHOOKINPUT
				On.RWInput.PlayerInput += RWInput_PlayerInput;
#endif
				On.Player.Die += Player_Die;
				On.RainWorldGame.GameOver += RainWorldGame_GameOver;

	/*{{{ death detection/prevention*/
				On.Player.Die += Player_Die;
				On.AbstractCreature.Die += AbstractCreature_Die;
				On.RainWorldGame.GameOver += RainWorldGame_GameOver;
				On.UpdatableAndDeletable.Destroy += UpdatableAndDeletable_Destroy;
				On.Room.AddObject += Room_AddObject;
				On.AbstractRoom.MoveEntityToDen += AbstractRoom_MoveEntityToDen;
				On.PhysicalObject.Update += PhysicalObject_Update;
	/*}}}*/

				On.ShortcutHandler.TeleportingCreatureArrivedInRealizedRoom += ShortcutHandler_TeleportingCreatureArrivedInRealizedRoom;
				self.StartCoroutine(CaptureFrames());
				ProcessFramesInit();
				for(int i = 0; i < frameProcessorThreads; i++) {
					(new System.Threading.Thread(ProcessFrames)).Start();
				}
			}
			catch {}
		}
/*}}}*/

/*{{{ WriteIPC(string text)*/
		private System.Net.Sockets.NetworkStream ipc;
		private System.Threading.Tasks.Task lastWrite = null;
		private async void WriteIPC(string text) {
			// TODO: same args for read
			Byte[] data = System.Text.Encoding.ASCII.GetBytes(text);
			if(lastWrite != null) await lastWrite;
			// WriteAsync does not start until awaited
			// TODO: I'm not actually certain this ensures they arrive in the correct order, at least without a newline
			lastWrite = System.Threading.Tasks.Task.Run(() => ipc.Write(data, 0, data.Length));
		}
/*}}}*/

		private void RoomCamera_ApplyPositionChange(On.RoomCamera.orig_ApplyPositionChange orig, RoomCamera self) {
			orig(self);
			if(teleport == 1) teleport = 2;
			System.Text.StringBuilder data = new System.Text.StringBuilder();
			data.Append("R" + self.room.abstractRoom.name +
				"|" + self.room.Width + "x" + self.room.Height +
				"|" + ((self.room.water) ? self.room.defaultWaterLevel.ToString() : "0")
			);
			// positive x is right, positive y is up
			// default is -143, -52
			//data.Append("|" + self.pos.x + "," + self.pos.y);
			data.Append("|");
			for(int j = 0; j < self.room.Height; j++) {
				for(int i = 0; i < self.room.Width; i++) {
					Room.Tile tile = self.room.Tiles[i, j];
					data.Append(
						  (tile.Terrain == Room.Tile.TerrainType.Solid ||
						   tile.Terrain == Room.Tile.TerrainType.Floor) ? '1'
						: (tile.Terrain == Room.Tile.TerrainType.Slope) ? '2'
						: (tile.AnyBeam)                                ? '3'
						: '0'
					);
				}
			}
			data.Append("\n");
			WriteIPC(data.ToString());
		}

		int teleport = 0;
		private void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed) {
			if(sendData) {
				if(deathCooldown > 0) deathCooldown--;
				System.Text.StringBuilder data = new System.Text.StringBuilder();
				// TODO: make teleport spawn in random region as well
				// TODO: pathfinder, ML side

				// TODO: see DirectionFinder class
				// TODO: there's HitByWeapon and Violence triggers for most things, could hook Creature's maybe?
				//       also Rock.HitSomething and probably spear/Weapon equivalent
				// TODO: see world.singleRoomWorld (maybe is just arena?), may be helpful for training jumps

				// TODO: prevents error when loading warehouse but doesn't make player
				//       if going to do testing there then will need to do so
				if(self.game.Players.Count < 1) return;
				Player player = self.game.Players[0].realizedCreature as Player;
				if(player == null) return;
				if(player.bodyMode == Player.BodyModeIndex.Dead ||
				   player.animation == Player.AnimationIndex.Dead) {
					throw new ArgumentException("RWAI: death was not caught");
				}
				if(justDied || player.dangerGrasp != null) {
					justDied = false;
					foreach(Creature.Grasp g in new System.Collections.Generic.List<Creature.Grasp>(player.grabbedBy)) {
						g.grabber.LoseAllGrasps();
					}
					self.game.Players[0].LoseAllStuckObjects();
					player.airInLungs = 1;
					player.drown = 0;
					player.lungsExhausted = false;
					player.slowMovementStun = 0;
					player.rainDeath = 0;
					// TODO: doesn't look like this will make things come back out of dens, but good enough for initial training
					//       (also doesn't actually stop the rain if it has started)
					//       going to hard cut off long before that to reward/punish
					self.room.world.rainCycle.timer = 0;
					if(player.spearOnBack != null && player.spearOnBack.spear != null) player.spearOnBack.DropSpear();
					player.LoseAllGrasps();
					player.Regurgitate();
					if(player.spearOnBack != null) player.spearOnBack.SpearToHand(true);
					player.LoseAllGrasps();
					if(deathCooldown > 0) return;
					else deathCooldown = 40;
					WorldCoordinate origin;
					if(player.room != null) {
						origin = (self.game.Players[0] as AbstractCreature).pos;
						self.room.RemoveObject(player);
						self.room.CleanOutObjectNotInThisRoom(player);
					}
					else origin = new WorldCoordinate();
					AbstractRoom room = self.room.world.abstractRooms[(new Random()).Next(self.room.world.abstractRooms.Length)];
					while(room == self.room.abstractRoom || room.offScreenDen || room.connections.Length <= 0 || room.name.Contains("GATE")) {
						room = self.room.world.abstractRooms[(self.room.world.abstractRooms.IndexOf(room)+1)%self.room.world.abstractRooms.Length];
					}
					self.game.shortcuts.CreatureTeleportOutOfRoom(player, origin, new WorldCoordinate(room.index, -1, -1, 0));
					teleport = 1;
					return;
				}
				if(player.room != null && teleport == 2) {
					teleport = 0;

/*{{{ teleport to random coordinate in room*/
					int[] tiles = new int[self.room.Width*self.room.Height];
					for(int i = 0; i < tiles.Length; i++) { tiles[i] = i; }
					Random tileRand = new Random();
					for(int i = 0; i < tiles.Length; i++) {
						int j = tileRand.Next(0, tiles.Length);
						(tiles[i], tiles[j]) = (tiles[j], tiles[i]);
					}
					int teleportTile = -1;
					foreach(int tile in tiles) {
						Room.Tile tileTile = self.room.Tiles[tile%self.room.Width, tile/self.room.Width];
						if(tileTile.Terrain != Room.Tile.TerrainType.Solid &&
						   tileTile.Terrain != Room.Tile.TerrainType.Floor &&
						   tileTile.Terrain != Room.Tile.TerrainType.Slope
						) continue;
						bool good = true;
						for(int j = 1; j <= 3; j++) {
							if(tile/self.room.Width+j >= self.room.Height) {
								good = false; break;
							}
							for(int i = -1; i <= 1; i++) {
								if(tile%self.room.Width+i < 0 || tile%self.room.Width+i >= self.room.Width) {
									good = false; break;
								}
								Room.Tile tileTile2 = self.room.Tiles[tile%self.room.Width+i, tile/self.room.Width+j];
								if(tileTile2.Terrain == Room.Tile.TerrainType.Solid ||
								   tileTile2.Terrain == Room.Tile.TerrainType.Floor ||
								   tileTile2.Terrain == Room.Tile.TerrainType.Slope
								) {
									good = false; break;
								}
								// backup plan
								teleportTile = tile+i+j*self.room.Width;
							}
							if(!good) break;
						}
						if(good) {
							teleportTile = tile+2*self.room.Width;
							break;
						}
					}
					if(teleportTile == -1) throw new ArgumentException("RWAI: " + self.room.abstractRoom.name + " has no air");
					player.SuperHardSetPosition(self.room.MiddleOfTile(teleportTile%self.room.Width, teleportTile/self.room.Width));
/*}}}*/

				}
				data.Append("T" + self.room.world.rainCycle.cycleLength +
				            "," + self.room.world.rainCycle.timer +
				"\n");

				// TODO: give time in room so can boost out of pipes, 0 to 1 with 1=60s or smth
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
				data.Append("P" + player.coord.x + "," + player.coord.y +
				// pos and vel are in pixels - 0,0 is bottom left
				// TODO: position on block might not be working - switches at 0.85 in y
				//       alternatively, may be average position of body chunks as x seemed to not work when crawling
				            "|" + ((player.mainBodyChunk.pos.x%20)/20).ToString("F3") + "," +
				                  ((player.mainBodyChunk.pos.y%20)/20).ToString("F3") +
				            "|" + (player.mainBodyChunk.vel.x/20).ToString("F3") + "," +
				                  (player.mainBodyChunk.vel.y/20).ToString("F3")
				);

	/*{{{ bodyMode and animation*/
				data.Append("|"); switch(player.bodyMode.value) {
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
				data.Append("|"); switch(player.animation.value) {
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

				data.Append("|" + player.airInLungs);

	/*{{{ stomach and grasped items*/
				data.Append("|" + ((player.objectInStomach == null) ? 0 : 1));
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
						) + "," + (player.CanBeSwallowed(player.grasps[i].grabbed) ? 1 : 0));
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

				data.Append("|" + (player.MaxFoodInStomach-player.FoodInStomach));
				data.Append("\n");
/*}}}*/

/*{{{ enemy, food, item vision*/
				if(player.room != null) {
					char[] enemyVisionOut = (new string('0', enemyVision*enemyVision)).ToCharArray();
					char[]  foodVisionOut = (new string('0',  foodVision* foodVision)).ToCharArray();
					char[]  itemVisionOut = (new string('0',  itemVision* itemVision)).ToCharArray();

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

					foreach(AbstractCreature c in self.room.abstractRoom.creatures) {
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
								FillBodyChunk(b, (!dangerous) ? '1' : '2', enemyVision, enemyVisionOut);
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
					foreach(AbstractWorldEntity e in self.room.abstractRoom.entities) {
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
						data.Append((new string(itemVisionOut)).Substring(i*itemVision, itemVision) + "\n");
					}//*/
					data.Append("C|" + new string(enemyVisionOut) + "\n");
					data.Append("F|" + new string( foodVisionOut) + "\n");
					data.Append("I|" + new string( itemVisionOut) + "\n");
				}
				else data.Append("C-\nF-\nI-\n");
/*}}}*/

				WriteIPC(data.ToString());
#if !NOHOOKINPUT
				sendData = false;
#endif
			}
			// not putting the above in another function because this would be four lines
			// cope
			if(!record && ipcRecord != null) return;
			orig(self, timeStacker, timeSpeed);
			if(ipcRecord != null) recordThis = true;
		}

/*{{{ recording*/
		private bool record = false;
		// just in case
		private bool recordThis = false;
		private System.Net.Sockets.NetworkStream ipcRecord;

		private void ProcessManager_Update(On.ProcessManager.orig_Update orig, ProcessManager self, float deltaTime) {
			// I would like to change this to 1 along with targetFrameRate, but it is set in a couple of places
			// I don't care *that* much about one dropped/extra frame every once in a while
			orig(self, (ipcRecord != null) ? 1f/20/fpsMult : deltaTime);
		}

	/*{{{ variables*/
		bool initFrame = true;
		UnityEngine.Experimental.Rendering.GraphicsFormat frameFormat;
		int frameWidth;
		int frameHeight;
		Unity.Collections.NativeArray<byte>[] availableFrames = new Unity.Collections.NativeArray<byte>[frameBacklog];
		UnityEngine.RenderTexture[] availableFrameBuffers = new UnityEngine.RenderTexture[frameBacklog];
		System.Collections.Concurrent.ConcurrentQueue<Unity.Collections.NativeArray<byte>> queuedFrames = new System.Collections.Concurrent.ConcurrentQueue<Unity.Collections.NativeArray<byte>>();
		System.Collections.Concurrent.ConcurrentQueue<UnityEngine.Rendering.AsyncGPUReadbackRequest> queuedFrameRequests = new System.Collections.Concurrent.ConcurrentQueue<UnityEngine.Rendering.AsyncGPUReadbackRequest>();
		System.Threading.Semaphore availableFramesSem = new System.Threading.Semaphore(0, frameBacklog);
		System.Threading.Semaphore    queuedFramesSem = new System.Threading.Semaphore(0, frameBacklog);
		int queuedSemCount = 0;
		int skipQueuedSemRelease = 0;
		System.Threading.Mutex frameMut = new System.Threading.Mutex();
	/*}}}*/

	/*{{{ IEnumerator CaptureFrames()*/
		System.Collections.IEnumerator CaptureFrames() {
			UnityEngine.RenderTexture tempFrameBuffer = new UnityEngine.RenderTexture(UnityEngine.Screen.currentResolution.width, UnityEngine.Screen.currentResolution.height, 0);
			UnityEngine.Vector2 scale  = new UnityEngine.Vector2(1, -1);
			UnityEngine.Vector2 offset = new UnityEngine.Vector2(0, 1);
			for(int i = 0; i < frameBacklog; i++) {
				availableFrames[i] = new Unity.Collections.NativeArray<byte>(UnityEngine.Screen.currentResolution.width*UnityEngine.Screen.currentResolution.height*4, Unity.Collections.Allocator.Persistent, Unity.Collections.NativeArrayOptions.UninitializedMemory);
				availableFrameBuffers[i] = new UnityEngine.RenderTexture(UnityEngine.Screen.currentResolution.width, UnityEngine.Screen.currentResolution.height, 0);
				availableFramesSem.Release();
			}
			for(int i = 0; true; i = (i+1)%frameBacklog) {
				yield return new UnityEngine.WaitForEndOfFrame();
				if(!recordThis) continue;
				recordThis = false;
				// using mutex here because technically all frames could be taken for processing between "at limit" check and peek
				// not using it to make skipQueuedSemRelease skip checks happen after it is incremented for this run or to check-and-set skipQueuedSemRelease because FrameAvailable is called by Unity in main thread at some start/end of frame (which is the problem this tackles in the first place)
				// lil' performance boost, don't fight for mutex when unnecessary
				// Releasing queuedFramesSem isn't enough to decrement from queuedFrames.count, need to not have FrameAvailables trigger then this, pushing queuedFramesSem over max, hence queuedSemCount
				if(queuedFrameRequests.Count == frameBacklog && queuedSemCount != frameBacklog) {
					frameMut.WaitOne();
					if(queuedFrameRequests.Count == frameBacklog) {
						UnityEngine.Rendering.AsyncGPUReadbackRequest request;
						queuedFrameRequests.TryPeek(out request);
						frameMut.ReleaseMutex();
						request.WaitForCompletion();
						// the reason for this weirdness - where this normally happens won't if this thread is waiting on availableFramesSem
						queuedFramesSem.Release();
						System.Threading.Interlocked.Increment(ref queuedSemCount);
						skipQueuedSemRelease++;
					}
					else frameMut.ReleaseMutex();
				}
				UnityEngine.ScreenCapture.CaptureScreenshotIntoRenderTexture(tempFrameBuffer);
				availableFramesSem.WaitOne();
				Unity.Collections.NativeArray<byte> frame = availableFrames[i];
				UnityEngine.RenderTexture frameBuffer = availableFrameBuffers[i];
				if(initFrame) {
					initFrame = false;
					frameFormat = frameBuffer.graphicsFormat;
					frameWidth = frameBuffer.width; frameHeight = frameBuffer.height;
				}
				UnityEngine.Graphics.Blit(tempFrameBuffer, frameBuffer, scale, offset);
				queuedFrames.Enqueue(frame);
				queuedFrameRequests.Enqueue(UnityEngine.Rendering.AsyncGPUReadback.RequestIntoNativeArray(ref frame, frameBuffer, 0, FrameAvailable));
			}
		}
		private void FrameAvailable(UnityEngine.Rendering.AsyncGPUReadbackRequest request) {
			if(skipQueuedSemRelease == 0) {
				queuedFramesSem.Release();
				System.Threading.Interlocked.Increment(ref queuedSemCount);
			}
			else skipQueuedSemRelease--;
		}
	/*}}}*/

	/*{{{ void ProcessFrames()*/
		System.Threading.Mutex threadMut = new System.Threading.Mutex();
		System.Threading.Semaphore[] writeFrameSems = new System.Threading.Semaphore[frameProcessorThreads];
		private void ProcessFramesInit() {
			writeFrameSems[0] = new System.Threading.Semaphore(1, 1);
			for(int i = 1; i < frameProcessorThreads; i++) {
				writeFrameSems[i] = new System.Threading.Semaphore(0, 1);
			}
		}
		int writeFrameSemsIndex = 0;
		private void ProcessFrames() {
			while(true) {
				queuedFramesSem.WaitOne();
				System.Threading.Interlocked.Decrement(ref queuedSemCount);
				UnityEngine.Rendering.AsyncGPUReadbackRequest request;
				threadMut.WaitOne();
				if(queuedFrameRequests.TryPeek(out request) && request.done) {
					Unity.Collections.NativeArray<byte> frame;
					frameMut.WaitOne();
					queuedFrames.TryDequeue(out frame);
					queuedFrameRequests.TryDequeue(out request);
					frameMut.ReleaseMutex();
					int writeFrameSemIndex = writeFrameSemsIndex;
					writeFrameSemsIndex = (writeFrameSemsIndex+1)%frameProcessorThreads;
					threadMut.ReleaseMutex();
					// hasError is toggled after one frame, see AsyncGPUReadbackRequest docs
					// data should be Disposed of at that point but I have set as persistent
					// a try catch was necessary when using GetData (just dropped frames) but seems good with queuedFrames
					//WriteIPC(request.hasError ? "BAD\n" : "GOOD\n");
					Unity.Collections.NativeArray<byte> nativeData = UnityEngine.ImageConversion.EncodeNativeArrayToJPG(frame, frameFormat, (uint)frameWidth, (uint)frameHeight, quality:100);
					byte[] data = nativeData.ToArray();
					availableFramesSem.Release();
					writeFrameSems[writeFrameSemIndex].WaitOne();
					writeFrameSems[(writeFrameSemIndex+1)%frameProcessorThreads].Release();
					ipcRecord.Write(data, 0, data.Length);
					nativeData.Dispose();
				}
				else threadMut.ReleaseMutex();
			}
		}
	/*}}}*/
/*}}}*/

/*{{{ PlayerInput*/
		private Player.InputPackage RWInput_PlayerInput(On.RWInput.orig_PlayerInput orig, int playerNumber, RainWorld rainWorld) {
			Player.InputPackage actual = orig(playerNumber, rainWorld);
			if(actual.thrw) justDied = true;
			return actual;
			float x = 1;
			float y = 0;
			Player.InputPackage inputs = new Player.InputPackage(
				true, // gamePad
				null, // controllerType, only used by orig of this
				(int)Math.Round(x), // x, direction to move in
				(int)Math.Round(y), // y, direction to move in
				false, // jmp
				false, // thrw
				false, // pckp
				false, // mp
				false  // crouchToggle, unused
			);
			inputs.analogueDir = new UnityEngine.Vector2(x, y);
			inputs.downDiagonal =
				(y < -0.05)
					? (Math.Abs(x) > 0.05)
						? Math.Sign(x)
						: 0
					: 0
			;
			sendData = true;
			return inputs;
		}
/*}}}*/

/*{{{ death detection/prevention*/
		private void Player_Die(On.Player.orig_Die orig, Player self) { justDied = true; }
		private void AbstractCreature_Die(On.AbstractCreature.orig_Die orig, AbstractCreature self) {
			if(self.realizedCreature is Player) justDied = true;
			else orig(self);
		}
		private void RainWorldGame_GameOver(On.RainWorldGame.orig_GameOver orig, RainWorldGame self, Creature.Grasp dependentOnGrasp) { justDied = true; }
		private void UpdatableAndDeletable_Destroy(On.UpdatableAndDeletable.orig_Destroy orig, UpdatableAndDeletable self) {
			if(self is Player) justDied = true;
			else orig(self);
		}
		private void Room_AddObject(On.Room.orig_AddObject orig, Room self, UpdatableAndDeletable obj) {
			if(obj is Player && self.abstractRoom.offScreenDen) justDied = true;
			else orig(self, obj);
		}
		private void AbstractRoom_MoveEntityToDen(On.AbstractRoom.orig_MoveEntityToDen orig, AbstractRoom self, AbstractWorldEntity ent) {
			if(ent is AbstractCreature && (ent as AbstractCreature).realizedCreature is Player) justDied = true;
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

		// https://github.com/henpemaz/RemixMods/blob/master/MapWarp/MapWarp.cs
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

		// see OverseerCommunicationModule.CreatureDangerScore
		private bool CreatureDangerous(AbstractCreature creature) {
			if(creature.creatureTemplate.smallCreature)
				return false;
			if(creature.creatureTemplate.dangerousToPlayer == 0 &&
			   creature.creatureTemplate.type != CreatureTemplate.Type.Scavenger
			) return false;
			return true;
		}
	}
}
