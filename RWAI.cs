//#define USERCONTROL

using System;

[assembly: System.Security.Permissions.SecurityPermission(System.Security.Permissions.SecurityAction.RequestMinimum, SkipVerification = true)]

namespace RWAI {
	[BepInEx.BepInPlugin("com.isaacelenbaas.rwai", "RWAI", "1.0.0")]

	// TODO: check out Room.PlaySound (or SoundLoader?) to see if audio is at all possible
	// TODO: see world.singleRoomWorld (maybe is just arena?), may be helpful for training jumps
	// TODO: see Custom.BetweenRoomsDistance
	public partial class RWAI : BepInEx.BaseUnityPlugin {
		const int creatureVision = 10; // centered on slugcat
		const int     foodVision = 10; // centered on slugcat
		const int     itemVision = 10; // centered on slugcat

		int deathCooldown = 0;
		int runCooldown = 60*40;
		int teleport = 0;
		bool justSucceeded = false;
		bool pauseData = false;
		System.Diagnostics.Stopwatch running = null;
		float recorded = 0;

/*{{{ init*/
		public void Awake() { On.RainWorld.OnModsInit += OnModsInit; }
		public void OnEnable() { On.RainWorld.OnModsInit += OnModsInit; }
		static bool initialized = false;
		private void OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self) {
			orig(self);
			if(initialized) return; initialized = true;
			if(creatureVision/2 != creatureVision/2.0) throw new ArgumentException("RWAI: creatureVision must be divisible by two");
			if(    foodVision/2 !=     foodVision/2.0) throw new ArgumentException("RWAI: foodVision must be divisible by two");
			if(    itemVision/2 !=     itemVision/2.0) throw new ArgumentException("RWAI: itemVision must be divisible by two");
			try {
				System.Net.Sockets.TcpClient ipcClient = new System.Net.Sockets.TcpClient("127.0.0.1", 8319);
				ipc = ipcClient.GetStream();
#if !USERCONTROL
				UnityEngine.Application.runInBackground = true;
				UnityEngine.QualitySettings.vSyncCount = 0;
				UnityEngine.Application.targetFrameRate = -1;
#endif
				try {
					System.Net.Sockets.TcpClient ipcRecordClient = new System.Net.Sockets.TcpClient("127.0.0.1", 8325);
					ipcRecord = ipcRecordClient.GetStream();
					running = System.Diagnostics.Stopwatch.StartNew();
				}
				catch { ipcRecord = null; }
				On.RainWorldGame.ctor += RainWorldGame_ctor;
				On.ProcessManager.Update += ProcessManager_Update;
				On.RoomCamera.DrawUpdate += RoomCamera_DrawUpdate;
				On.RoomCamera.ApplyPositionChange += RoomCamera_ApplyPositionChange;
				On.RWInput.PlayerInput += RWInput_PlayerInput;
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
			catch(System.Net.Sockets.SocketException e) {}
		}
/*}}}*/

/*{{{ RoomCamera.ApplyPositionChange*/
		// last (current, last call) room and goal room indices
		int last = -1;
		int goal = -1;
		private void RoomCamera_ApplyPositionChange(On.RoomCamera.orig_ApplyPositionChange orig, RoomCamera self) {
			orig(self);
			// ignore screen changes within room
			if(self.room.abstractRoom.index == last) return;
			last = self.room.abstractRoom.index;
			if(teleport == 1) {
				teleport = 2;
				runCooldown = 60*40;
				// also sets goal
				RoomData(self.room);
			}
			else {
				if(self.room.abstractRoom.index == goal) justSucceeded = true;
				deathCooldown = 0;
				// JustDied would just return here, in pipe
				runCooldown = 0;
			}
		}
/*}}}*/

/*{{{ RoomCamera.DrawUpdate*/
		bool readInputsThis = false;
		bool sendDataThis = true;
		private void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed) {
			if(deathCooldown > 0) deathCooldown--;
			if(  runCooldown > 0)   runCooldown--;
			else {
				JustDied(self.room.game);
				return;
			}
			if(teleport == 2) {
				teleport = 0;
				pauseData = false;
				TeleportInRoom(self.room);
			}
			if(pauseData) return;
			if(sendDataThis) {
				sendDataThis = false;
				// TODO: prevents error when loading warehouse but doesn't make player
				//       if going to do training there then will need to do so
				if(self.game.Players.Count < 1) return;
				Player player = self.game.Players[0].realizedCreature as Player;
				if(player == null) return;
				if(player.bodyMode == Player.BodyModeIndex.Dead ||
				   player.animation == Player.AnimationIndex.Dead) {
					throw new ArgumentException("RWAI: death was not caught");
				}
				if(player.dangerGrasp != null) {
					JustDied(self.room.game);
					return;
				}
#if !USERCONTROL
				readInputsThis = true;
				MainData(self.game.world, true);
#else
				MainData(self.game.world, false);
#endif
			}
			if(!record) return;
			orig(self, timeStacker, timeSpeed);
			// after orig because recording is threaded
			if(ipcRecord != null) recordThis = true;
		}
/*}}}*/

/*{{{ void JustDied(RainWorldGame game)*/
		void JustDied(RainWorldGame game) {
			if(game.Players[0].realizedCreature == null ||
			   game.Players[0].realizedCreature.inShortcut
			) return;
			Player player = game.Players[0].realizedCreature as Player;
			// TODO: need to see if this will make things come back out of dens
			//       either way good enough for initial training, going to hard cut off long before that to reward/punish
			// TODO: can cause NullReferenceException at start, either don't justDied at start or do the first warp more intentionally
			game.globalRain.ResetRain();
			ResetDeath(player);
			if(deathCooldown > 0) return;
			else deathCooldown = 40;
			pauseData = true;
			if(ipcRecord != null) {
				if(record) recorded += (float)((60*40-runCooldown)/(60.0*40));
				record = running.Elapsed.Minutes+1 > recorded;
			}
#if !USERCONTROL
			// TODO
			//else record = false;
#endif
			if(justSucceeded)   WriteIPC("-S\n");
			else if(goal != -1) WriteIPC("-X\n");
			justSucceeded = false;
			TeleportNewRoom(game.world);
			teleport = 1;
			// called again to lose items if was killed by pole plant/worm grass
			ResetDeath(player);
		}
/*}}}*/

/*{{{ RWInput.PlayerInput*/
		private Player.InputPackage RWInput_PlayerInput(On.RWInput.orig_PlayerInput orig, int playerNumber, RainWorld rainWorld) {
			sendDataThis = true;
#if USERCONTROL
			Player.InputPackage actual = orig(playerNumber, rainWorld);
			//if(actual.thrw) JustDied(rainWorld.processManager.currentMainLoop as RainWorldGame);
			return actual;
#endif
			Player.InputPackage inputs = new Player.InputPackage(
				false, // gamePad
				null, // controllerType, only used by orig of this
				0, // x, direction to move in
				0, // y, direction to move in
				false, // jmp
				false, // thrw
				false, // pckp
				false, // mp
				false  // crouchToggle, unused
			);
			float x = 0;
			float y = 0;
			if(readInputsThis) {
				readInputsThis = false;
				byte[] data = new byte[1024];
				int length = ipc.Read(data, 0, data.Length);
				// TODO: maybe want to get other messages
				// get two inputs sometimes - from end of round and first (blank) state of next
				string text = System.Text.Encoding.ASCII.GetString(data, 0, length).Split('\n')[0];
				int rawInputs = int.Parse(text);
				Func<int, bool> check = i => (rawInputs & (1 << i)) != 0;
				x = ((check(0)) ? 1 : 0)-((check(1)) ? 1 : 0);
				y = ((check(2)) ? 1 : 0)-((check(3)) ? 1 : 0);
				inputs.jmp = check(4);
				inputs.thrw = check(5);
				inputs.pckp = check(6);
				inputs.x = (int)Math.Round(x);
				inputs.y = (int)Math.Round(y);
			}
			// unfortunately only have 0 or 1 outputs so saying gamepad would be a bit useless
			//inputs.analogueDir = new UnityEngine.Vector2(x, y);
			inputs.downDiagonal =
				(y < -0.05)
					? (Math.Abs(x) > 0.05)
						? Math.Sign(x)
						: 0
					: 0
			;
			return inputs;
		}
/*}}}*/

/*{{{ framerate locking*/
		private void RainWorldGame_ctor(On.RainWorldGame.orig_ctor orig, RainWorldGame self, ProcessManager manager) {
			orig(self, manager);
#if !USERCONTROL
			// makes Process_Manager_Update need /40 instead of /20 and GrafUpdate run once per overall Update
			self.framesPerSecond = 40;
#endif
		}
		private void ProcessManager_Update(On.ProcessManager.orig_Update orig, ProcessManager self, float deltaTime) {
			// I would like to change this to 1, but it is set in a couple of places
			// I don't care *that* much about one dropped/extra frame every once in a while
#if !USERCONTROL
			orig(self, 1f/40/fpsMult);
#else
			orig(self, deltaTime);
#endif
		}
/*}}}*/

	}
}
