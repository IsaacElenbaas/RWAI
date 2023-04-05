# Rain World AI
## About
This repository contains three components which work together to train an AI to play Rain World:
* `RWAI.cs`, `RWAI-*.cs`: A Rain World mod to dump *a lot* of game info through a socket each frame
	* `comm.c`: An intermediary between the mod and neural network
* `pathfind.c`, `movements.c`: An A* pathfinder to provide optimal Slugcat-traversable directions to the chosen room exit
* `AI.cpp`: A neural network using mlpack

It has proven difficult to train the neural network. I am currently poking at my third implementation, and in hindsight the first (which was committed and is in the repository) using Q-Learning never had a chance of working. This will be updated when I get it to even learn to consistently walk in the correct direction in a way that won't destroy future improvement opportunities.
## Demonstration
The Rain World mod also, optionally and in a variable number of threads, encodes and sends each frame over a separate socket (see `RWAI-video.cs`, `RWAI`). This (and rendering entirely) is paused while the stream has a buffer, which it will gain because the mod runs the game at unlimited FPS. It allows regular-speed viewing while training when piped to a video player and I intend to stream it on Twitch.
## License
Please contact me if you wish to fork, contribute to, or have suggestions for this project.

Discord: Kerotoric#2545

Email: isaacelenbaas@gmail.com
