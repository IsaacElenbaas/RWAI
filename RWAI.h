#ifndef RWAI_H
#define RWAI_H

#define     room_vision 10 // centered on slugcat
// must match those in RWAI.cs if nonzero
#define creature_vision 10 // centered on slugcat
#define     food_vision  0 // centered on slugcat
#define     item_vision  0 // centered on slugcat
#define    input_memory 10 // multiplied by 5

extern bool died, succeeded, exiting;
extern int* room;
extern int room_w;
extern int room_h;
extern int room_water_level;
extern int room_zerog;
extern int goal_x;
extern int goal_y;
extern int cycle_length;
extern int cycle_elapsed;
extern int block_x;
extern int block_y;
extern float block_pos_x;
extern float block_pos_y;
extern float vel_x;
extern float vel_y;
extern int body_mode;
extern int animation;
extern float air_in_lungs;
extern int object_in_stomach;
extern int hand[2];
extern int hand_type[2];
extern int hand_swallowable[2];
extern int item;
extern int item_type;
extern int food_empty;
extern int creatures[creature_vision*creature_vision];
extern bool food[food_vision*food_vision];
extern int items[item_vision*item_vision];

void pathfind_init();
void pathfind(int x, int y, int dist, int* out_x, int* out_y);

void AI_init();
void AI_exit();
int has_data_get_inputs();
#endif
