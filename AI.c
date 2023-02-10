#include <arpa/inet.h>
#include <netinet/in.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/socket.h>
#include <unistd.h>
#include "pathfind.h"

// all messages must be able to fit in a buffer
#define BUFSIZE 64*1024

// Center info around slugcat position
// give player velocity (capped and normalized? log scale?)
// use -1 to 1 where applicable, 0 means not impacting anything
//
// treat pipe paths and pipe entrances as air
// poles as their own input
// air is 1, slope is 0.5, solid is 0
// give whether can see space
// give position on block 0-1
// with N rocks or whatever, randomize between inputs
// give underwater (by room water height), 0g flags
// for each enemy or bodyChunk or whatever I give it give whether there is line-of-sight to it
// slowly reward it at all times for having items in hand
//   https://github.com/Dark-Gran/KarmaAppetite_SpearPull/blob/master/SpearSkills/patch_Player.cs
// see https://github.com/casheww/RW-Bioengineering/tree/master/SmallEel for in-game pathfinding implementation
// see https://github.com/casheww/RW-SlugBrain
// give distance to desired pipe? (normalize or no?)
// I'm thinking do some small amount of pathfinding and give points along path in increments, along with a flag of whether to consider that (so can disable to just hunt)
//   maybe give direction, whether to follow it, and magnitude?
//   give next *two*, if close to first or between it and next advance
//   time trying to get to that point along it and give same thing for a safer path
//
// when a creature is damaged by another creature (its Violence method is called), it sets killTag to the AbstractCreature that caused the violence. killTagCounter is set to max(killTagCounter, 200). Every Update call, if killTagCounter > 0the killTagCounter is reduced by 1, and when killTagCounter < 1, killTag is cleared (set to null). 200 updates is 5 seconds
// the SetKillTag method that sets the killTag to the killer and sets the killTagCounter to the larger value of itself or 200, is also called by explosion.update, flarebomb.update, and a few others
//
// give percent full, reward for eating, can be a lot as it will max out for the cycle?
int* room = NULL;
int room_w;
int room_h;
int room_water_level;
int room_zerog;
int goal_x;
int goal_y;

int main(int argc, char* argv[]) {
	struct sockaddr_in addr;
	addr.sin_family = AF_INET;
	addr.sin_addr.s_addr = inet_addr("127.0.0.1");
	addr.sin_port = htons(8319);
	int sockfd = socket(AF_INET, SOCK_STREAM, 0);
	bind(sockfd, (struct sockaddr*)&addr, sizeof(addr));
	listen(sockfd, 1);
	socklen_t addrlen = sizeof(addr);
	sockfd = accept(sockfd, (struct sockaddr*)&addr, &addrlen);
	char buffer[BUFSIZE];
	int length;
	size_t from_last = 0;

/*{{{ AI variables*/
	int room_length = 0;
	char* room_name = malloc((20+1)*sizeof(char));
	char* last_room_name = malloc((20+1)*sizeof(char));
	room_name[0] = '\0';
	int cycle_length;
	int cycle_elapsed;
	int block_x;
	int block_y;
	float block_pos_x;
	float block_pos_y;
	float vel_x;
	float vel_y;
	int body_mode;
	int animation;
	float air_in_lungs;
	int object_in_stomach;
	int hand[2];
	int hand_type[2];
	int hand_swallowable[2];
	int item;
	int item_type;
	int food_empty;
/*}}}*/

	while((length = recv(sockfd, buffer+from_last*sizeof(char), BUFSIZE-from_last-1, 0))) {
		buffer[from_last+length] = '\0';
		char* i = buffer;
		char* j;
		while((j = strchr(i, '\n'))) {
			// does not include newline
			//write(1, i, (int)((j-i)/sizeof(char)));
			i += sizeof(char);
			switch(*(i-sizeof(char))) {
				case 'R':
					char* temp = last_room_name;
					last_room_name = room_name;
					room_name = temp;
					int room_name_length;
					sscanf(i, "%20[^|]%n|%dx%d|%d|%d|%n",
						room_name,
						&room_name_length,
						&room_w,
						&room_h,
						&room_water_level,
						&room_zerog,
					&length)*sizeof(char); i += length*sizeof(char);
					room_name[room_name_length] = '\0';
					if(strcmp(room_name, last_room_name) != 0) {
						if(room_w*room_h > room_length) {
							free(room);
							room_length = room_w*room_h;
							room = malloc(room_length*sizeof(int));
						}
						for(int j = 0; j < room_length; j++) {
							sscanf(i+j*sizeof(char), "%1d", &room[j]);
						}
					}
					/*for(int j = room_h-1; j >= 0; j--) {
						for(int k = 0; k < room_w; k++) {
							printf("%d", room[j*room_w+k]);
						} printf("\n");
					} printf("\n"); //*/
					break;
				case 'S':
					if(strcmp(room_name, last_room_name) != 0) {
						int exit_x;
						int exit_y;
						sscanf(i, "%d,%d|%d,%d",
							&exit_x,
							&exit_y,
							&goal_x,
							&goal_y
						)*sizeof(char);
						pathfind_init();
						// TODO: make this return bool, if not reachable send back to reroll
						//       or maybe if not reachable check total reachable nodes and if low then reroll (teleported into box)
						pathfind(exit_x, exit_y, 0);
					}
					break;
				case 'T':
					sscanf(i, "%d,%d",
						&cycle_length,
						&cycle_elapsed
					)*sizeof(char);
					break;
				case 'P':
					sscanf(i, "%d,%d|%f,%f|%f,%f|%n",
						&block_x,
						&block_y,
						&block_pos_x,
						&block_pos_y,
						&vel_x,
						&vel_y,
					&length)*sizeof(char); i += length*sizeof(char);
					sscanf(i, "%d|%d|%f|%d|%n",
						&body_mode,
						&animation,
						&air_in_lungs,
						&object_in_stomach,
					&length)*sizeof(char); i += length*sizeof(char);
					for(int k = 0; k < 2; k++) {
						sscanf(i, "%d,%d,%d|%n",
							&hand[k],
							&hand_type[k],
							&hand_swallowable[k],
						&length)*sizeof(char); i += length*sizeof(char);
					}
					sscanf(i, "%d,%d|%d%n",
						&item,
						&item_type,
						&food_empty,
					&length)*sizeof(char); i += length*sizeof(char);
					break;
				case 'C':
					if(*i == '-') break;
					i += sizeof(char);
					// TODO
					break;
				case 'F':
					if(*i == '-') break;
					i += sizeof(char);
					// TODO
					break;
				case 'I':
					if(*i == '-') break;
					i += sizeof(char);
					// TODO
					break;
				case '-':
					pathfind(block_x, block_y, 1);
					break;
				case 'D':
					write(1, i, (int)((j-i)/sizeof(char))+1);
					break;
			}
			i = j+sizeof(char);
		}
		if(*i != '\0') {
			from_last = (int)((buffer+(BUFSIZE-1)*sizeof(char)-i)/sizeof(char));
			memcpy(buffer, i, from_last*sizeof(char));
		}
		else from_last = 0;
	}
	return 0;
}
