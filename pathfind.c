#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "RWAI.h"

#define MAX(a,b) ((a) > (b) ? (a) : (b))
#define MIN(a,b) ((a) < (b) ? (a) : (b))

typedef struct Block {
	int x;
	int y;
	struct Block* to_goal;
	float dist_to;
	float dist_left;
	float total;
	bool to_explore;
	struct Block* prev;
	struct Block* next;
} Block;

static Block* blocks = NULL;
static int blocks_len = 0;
static Block* explore;

void pathfind_init() {
	if(room_w*room_h > blocks_len) {
		free(blocks);
		blocks_len = room_w*room_h;
		blocks = malloc(blocks_len*sizeof(Block));
	}
	for(int i = 0; i < blocks_len; i++) {
		blocks[i].x = i%room_w;
		blocks[i].y = i/room_w;
		blocks[i].to_goal = NULL;
		blocks[i].to_explore = false;
	}
	Block* goal = &blocks[goal_y*room_w+goal_x];
	goal->to_goal = goal;
	goal->dist_to = 0;
	goal->total = 0;
	explore = goal;
	goal->prev = NULL;
	goal->next = NULL;
}

static void update_dist_left(Block* block, int x, int y) {
	if(y <= block->y) block->dist_left = (block->y-y)+abs(block->x-x);
	else {
		int dx = abs(block->x-x);
		int dy = y-block->y;
		block->dist_left = 0.96*MAX(dx, dy)+0.40*MIN(dx, dy);
	}
}

static void update_total(Block* block) {
	block->total = block->dist_to+block->dist_left;
}

// TODO: make dist by cost rounding up not steps
void pathfind(int x, int y, int dist, int* out_x, int* out_y) {
	if(x < 0 || x >= room_w || y < 0 || y >= room_h) {
		*out_x = 0;
		*out_y = 0;
		return;
	}
	Block* block = &blocks[y*room_w+x];
	if(block->to_goal == NULL) {
		if(explore == NULL) {
			if(dist == 0) return;
			*out_x = 0;
			*out_y = 0;
			return;
		}
		// update totals of seen Blocks
		for(int i = 0; i < blocks_len; i++) {
			if(blocks[i].to_goal == NULL) continue;
			update_dist_left(&blocks[i], x, y);
			update_total(&blocks[i]);
		}

/*{{{ re-sort explore on those totals*/
		Block* block2 = explore;
		Block* block3 = explore->next;
		while(block3 != NULL) {
			if(block3->total < block2->total) {
				Block* block4 = block2;
				while(block4->prev != NULL && block3->total < block4->prev->total) {
					block4 = block4->prev;
				}
				if(block3->next != NULL) block3->next->prev = block2;
				                               block2->next = block3->next;
				block3->prev = block4->prev;
				block3->next = block4;
				if(block3->prev != NULL) block3->prev->next = block3;
				block3->next->prev = block3;
				if(explore == block4) explore = block3;
			}
			else block2 = block3;
			block3 = block2->next;
		}
/*}}}*/

		while(block->to_goal == NULL) {
			if(explore == NULL) {
				if(dist == 0) return;
				*out_x = 0;
				*out_y = 0;
				return;
			}
			Block* block2 = explore;
			explore = explore->next;
			block2->to_explore = false;
			if(explore != NULL) explore->prev = NULL;
			int possible_x;
			int possible_y;
			float possible_cost;
			// don't worry I hate this too
			#include "movements.c"
				Block* block3 = &blocks[possible_y*room_w+possible_x];
				if(block3->to_goal == NULL || block2->dist_to+possible_cost < block3->dist_to) {
					block3->to_goal = block2;
					block3->dist_to = block2->dist_to+possible_cost;
					update_dist_left(block3, x, y);
					update_total(block3);
					if(explore != block3) {
						if(block3->to_explore) {
							if(block3->prev != NULL) block3->prev->next = block3->next;
							if(block3->next != NULL) block3->next->prev = block3->prev;
						}
						if(explore == NULL || block3->total < explore->total) {
							block3->prev = NULL;
							block3->next = explore;
							if(block3->next != NULL) block3->next->prev = block3;
							explore = block3;
						}
						else {
							Block* block4 = explore;
							while(block4->next != NULL && block4->next->total < block3->total) {
								block4 = block4->next;
							}
							block3->prev = block4;
							block3->next = block4->next;
							block3->prev->next = block3;
							if(block3->next != NULL) block3->next->prev = block3;
						}
						block3->to_explore = true;
					}
				}
			} // sorry about this
		}
	}
	/*int* path = malloc((room_w*room_h)*sizeof(int));
	memcpy(path, room, (room_w*room_h)*sizeof(int));
	for(Block* block2 = block; block2->to_goal != block2; block2 = block2->to_goal) {
		path[block2->y*room_w+block2->x] = -1;
	}
	for(int j = room_h-1; j >= 0; j--) {
		for(int i = 0; i < room_w; i++) {
			if((i == x && j == y) || (blocks[j*room_w+i].to_goal == &blocks[j*room_w+i]))
				printf("X");
			else if(path[j*room_w+i] == -1)
				printf("-");
			else
				printf("%d", path[j*room_w+i]);
		} printf("\n");
	} printf("\n"); //*/
	if(dist == 0) return;
	for(int i = 0; i < dist; i++) { block = block->to_goal; }
	*out_x = block->x;
	*out_y = block->y;
}
