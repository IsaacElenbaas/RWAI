CC=gcc
INC_PATH=-I.
LIB_PATH=
LIBS=
CFLAGS=-Wall -Wextra -O3 $(INC_PATH)

.PHONY: all
all: mod AI video

.PHONY: mod
mod: Mod/plugins/RWAI.dll

Mod/plugins/RWAI.dll: RWAI.csproj Mod/modinfo.json RWAI.cs RWAI-data.cs RWAI-helper.cs RWAI-video.cs
	msbuild ./*.csproj /t:Build /p:Configuration=Release /p:Platform=AnyCPU

AI: comm.o AI.o pathfind.o
	g++ $(CFLAGS) -o $@ $^ $(LIB_PATH) $(LIBS) -lm -lnvblas -L/opt/cuda/lib64 -larmadillo -fopenmp
comm.o: RWAI.h comm.c pathfind.o
AI.o: RWAI.h AI.cpp
	g++ $(CFLAGS) -c -o $@ AI.cpp $(LIB_PATH) $(LIBS)
pathfind.o: RWAI.h pathfind.c movements.c

video: video.c
	$(CC) $(CFLAGS) -o $@ $^ $(LIB_PATH) $(LIBS)

.PHONY: clean
clean:
	rm -rf Mod/plugins/*.dll obj AI video *.o
