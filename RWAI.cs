using System;

[module: System.Security.UnverifiableCode]
[assembly: System.Security.Permissions.SecurityPermission(System.Security.Permissions.SecurityAction.RequestMinimum, SkipVerification = true)]

namespace RWAI {
	[BepInEx.BepInPlugin("isaacelenbaas.rwai", "RWAI", "1.0.0")]

	public class RWAI : BepInEx.BaseUnityPlugin {
		// TODO
		const int frameBacklog = 1;

		private bool record = true;
		// just in case
		private bool recordThis = false;

		System.Collections.Concurrent.ConcurrentQueue<Unity.Collections.NativeArray<byte>> availableFrames = new System.Collections.Concurrent.ConcurrentQueue<Unity.Collections.NativeArray<byte>>();
		System.Collections.Concurrent.ConcurrentQueue<UnityEngine.Rendering.AsyncGPUReadbackRequest> queuedFrames = new System.Collections.Concurrent.ConcurrentQueue<UnityEngine.Rendering.AsyncGPUReadbackRequest>();
		System.Threading.Semaphore availableFramesSem = new System.Threading.Semaphore(0, frameBacklog);
		System.Threading.Semaphore    queuedFramesSem = new System.Threading.Semaphore(0, frameBacklog+1);
		uint skipQueuedSemRelease = 0;

		public void OnEnable() { On.RainWorld.OnModsInit += OnModsInit; }

		private void OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self) {
			UnityEngine.QualitySettings.vSyncCount = 0;
			UnityEngine.Application.targetFrameRate = -1;
			orig(self);
			try {
				System.Net.Sockets.TcpClient ipcClient = new System.Net.Sockets.TcpClient("127.0.0.1", 12345);
				ipc = ipcClient.GetStream();
				//WriteIPC("testing");
				On.ProcessManager.Update += ProcessManager_Update;
				On.RoomCamera.DrawUpdate += RoomCamera_DrawUpdate;
				//On.RWInput.PlayerInput += RWInput_PlayerInput;
				self.StartCoroutine(CaptureFrames());
				(new System.Threading.Thread(ProcessFrames)).Start();
			}
			catch {}
		}

		private void ProcessManager_Update(On.ProcessManager.orig_Update orig, ProcessManager self, float deltaTime) {
			// I would like to change this to 1 along with targetFrameRate, but it is set in a couple of places
			// I don't care *that* much about one dropped/extra frame every once in a while
			orig(self, 1f/40);
		}

		private System.Net.Sockets.NetworkStream ipc;
		private System.Threading.Tasks.Task lastWrite = null;
		private async void WriteIPC(string text) {
			// TODO: same args for read
			Byte[] data = System.Text.Encoding.ASCII.GetBytes(text);
			if(lastWrite != null) await lastWrite;
			// WriteAsync does not start unless awaited (which we don't want to do to let game loop still run)
			lastWrite = System.Threading.Tasks.Task.Run(() => ipc.Write(data, 0, data.Length));
		}

		private void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed) {
			if(!record) return;
			orig(self, timeStacker, timeSpeed);
			recordThis = true;
		}

		bool initFrame = true;
		UnityEngine.Experimental.Rendering.GraphicsFormat frameFormat;
		int frameWidth;
		int frameHeight;
		// TODO: move, take from pool somehow? not sure how to know when one's done
		UnityEngine.RenderTexture frameBuffer = new UnityEngine.RenderTexture(UnityEngine.Screen.currentResolution.width, UnityEngine.Screen.currentResolution.height, 0);
		System.Collections.IEnumerator CaptureFrames() {
			UnityEngine.RenderTexture tempFrameBuffer = new UnityEngine.RenderTexture(UnityEngine.Screen.currentResolution.width, UnityEngine.Screen.currentResolution.height, 0);
			UnityEngine.Vector2 scale  = new UnityEngine.Vector2(1, -1);
			UnityEngine.Vector2 offset = new UnityEngine.Vector2(0, 1);
			// TODO: Maybe Screen.currentResolution or something else instead?
			for(int i = 0; i < frameBacklog; i++) {
				availableFrames.Enqueue(new Unity.Collections.NativeArray<byte>(UnityEngine.Screen.currentResolution.width*UnityEngine.Screen.currentResolution.height*4, Unity.Collections.Allocator.Persistent, Unity.Collections.NativeArrayOptions.UninitializedMemory));
				availableFramesSem.Release();
			}
			while(true) {
				yield return new UnityEngine.WaitForEndOfFrame();
				if(!recordThis) continue;
				recordThis = false;
				if(queuedFrames.Count == frameBacklog) {
					UnityEngine.Rendering.AsyncGPUReadbackRequest request;
					queuedFrames.TryPeek(out request);
					request.WaitForCompletion();
					// the reason for this weirdness - where this normally happens won't if this thread is waiting on availableFramesSem
					// would cause deadlock, WaitForCompletion might be enough to prevent it but not trusting and doing this
					queuedFramesSem.Release();
					skipQueuedSemRelease++;
				}
				UnityEngine.ScreenCapture.CaptureScreenshotIntoRenderTexture(tempFrameBuffer);
				UnityEngine.Graphics.Blit(tempFrameBuffer, frameBuffer, scale, offset);
				if(initFrame) {
					initFrame = false;
					frameFormat = frameBuffer.graphicsFormat;
					frameWidth = frameBuffer.width; frameHeight = frameBuffer.height;
				}
				availableFramesSem.WaitOne();
				Unity.Collections.NativeArray<byte> frame; availableFrames.TryDequeue(out frame);
				//UnityEngine.Rendering.CommandBuffer cb = new UnityEngine.Rendering.CommandBuffer();
				//cb.RequestAsyncReadbackIntoNativeArray(ref frame, frameBuffer, 0, FrameAvailable);
				//UnityEngine.Graphics.ExecuteCommandBuffer(cb);
				queuedFrames.Enqueue(UnityEngine.Rendering.AsyncGPUReadback.RequestIntoNativeArray(ref frame, frameBuffer, 0, FrameAvailable));
			}
		}
		private void FrameAvailable(UnityEngine.Rendering.AsyncGPUReadbackRequest request) {
			if(skipQueuedSemRelease == 0)
				queuedFramesSem.Release();
			else skipQueuedSemRelease--;
		}
		private void ProcessFrames() {
			while(true) {
				queuedFramesSem.WaitOne();
				UnityEngine.Rendering.AsyncGPUReadbackRequest request;
				while(queuedFrames.TryPeek(out request) && request.done) {
					queuedFrames.TryDequeue(out request);
					Unity.Collections.NativeArray<byte> frame = request.GetData<byte>();
					Unity.Collections.NativeArray<byte> nativeData = UnityEngine.ImageConversion.EncodeNativeArrayToJPG(frame, frameFormat, (uint)frameWidth, (uint)frameHeight, quality:100);
					byte[] data = nativeData.ToArray();
					ipc.Write(data, 0, data.Length);
					nativeData.Dispose();
					availableFrames.Enqueue(frame);
					availableFramesSem.Release();
				}
			}
		}

		private Player.InputPackage RWInput_PlayerInput(On.RWInput.orig_PlayerInput orig, RainWorld self, int playerNumber) {
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
			return inputs;
		}
	}
}
