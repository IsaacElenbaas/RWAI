#ifndef PATHFIND_H
#define PATHFIND_H
extern int* room;
extern int room_w;
extern int room_h;
extern int room_water_level;
extern int room_zerog;
extern int goal_x;
extern int goal_y;

void pathfind_init();
void pathfind(int x, int y, int dist);
#endif
