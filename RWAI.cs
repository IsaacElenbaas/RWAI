using System;

[module: System.Security.UnverifiableCode]
[assembly: System.Security.Permissions.SecurityPermission(System.Security.Permissions.SecurityAction.RequestMinimum, SkipVerification = true)]

namespace RWAI {
	[BepInEx.BepInPlugin("isaacelenbaas.rwai", "RWAI", "1.0.0")]

	public class RWAI : BepInEx.BaseUnityPlugin {
		const int frameBacklog = 20;
		const int frameProcessorThreads = 3;
		const float fpsMult = 1;

		private bool record = true;
		// just in case
		private bool recordThis = false;

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
				ProcessFramesInit();
				for(int i = 0; i < frameProcessorThreads; i++) {
					(new System.Threading.Thread(ProcessFrames)).Start();
				}
			}
			catch {}
		}

		private System.Net.Sockets.NetworkStream ipc;
		private System.Threading.Tasks.Task lastWrite = null;
		private async void WriteIPC(string text) {
			// TODO: same args for read
			Byte[] data = System.Text.Encoding.ASCII.GetBytes(text);
			if(lastWrite != null) await lastWrite;
			// WriteAsync does not start unless awaited (which we don't want to do to let game loop still run)
			lastWrite = System.Threading.Tasks.Task.Run(() => ipc.Write(data, 0, data.Length));
			ipc.Write(data, 0, data.Length);
		}

/*{{{ recording*/
		private void ProcessManager_Update(On.ProcessManager.orig_Update orig, ProcessManager self, float deltaTime) {
			// I would like to change this to 1 along with targetFrameRate, but it is set in a couple of places
			// I don't care *that* much about one dropped/extra frame every once in a while
			orig(self, 1f/20/fpsMult);
		}

		private void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed) {
			if(!record) return;
			orig(self, timeStacker, timeSpeed);
			recordThis = true;
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
			// TODO: Maybe Screen.currentResolution or something else instead?
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
					ipc.Write(data, 0, data.Length);
					nativeData.Dispose();
				}
				else threadMut.ReleaseMutex();
			}
		}
	/*}}}*/
/*}}}*/

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
