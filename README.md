# Rain World AI
## About
This repository contains three components which work together to train an AI to play Rain World:
* `RWAI.cs`, `RWAI-*.cs`: A Rain World mod to dump *a lot* of game info through a socket each frame
	* `comm.c`: An intermediary between the mod and neural network
* `pathfind.c`, `movements.c`: An A* pathfinder to provide optimal Slugcat-traversable directions to the chosen room exit
* `AI.cpp`: A neural network using mlpack
## Demonstration
The Rain World mod also, optionally and in a variable number of threads, encodes and sends each frame over a separate socket (see `RWAI-video.cs`, `RWAI`). This (and rendering entirely) is paused while the stream has a buffer, which it will gain because the mod runs the game at unlimited FPS. It allows regular-speed viewing while training when piped to a video player and I intend to stream it on Twitch.

Though that is working, Rain World currently will not launch on my desktop. I am training this intermittently on my laptop and will add a video soon if I have not fixed that and gotten a stream up by then.
## License
Please contact me if you wish to fork, contribute to, or have suggestions for this project.

Discord: Kerotoric#2545

Email: isaacelenbaas@gmail.com
