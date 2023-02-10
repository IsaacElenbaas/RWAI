CC=gcc
INC_PATH=-I.
LIB_PATH=
LIBS=
CFLAGS=-Wall -Wextra -O3 $(INC_PATH)

.PHONY: all
all: mod AI video

.PHONY: mod
mod:
	msbuild ./*.csproj /t:Build /p:Configuration=Release /p:Platform=AnyCPU

AI: pathfind.h AI.c pathfind.o
	$(CC) $(CFLAGS) -o $@ $^ $(LIB_PATH) $(LIBS)

pathfind.o: pathfind.h pathfind.c movements.c

video: video.c
	$(CC) $(CFLAGS) -o $@ $^ $(LIB_PATH) $(LIBS)

.PHONY: clean
clean:
	rm -rf Mod/plugins/*.dll obj AI video *.o
