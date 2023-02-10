//int* room;
//int room_w;
//int room_h;
//int room_water_level;
//int room_zerog;
//int goal_x;
//int goal_y;
//int possible_x;
//int possible_y;
//float possible_cost;

bool done = false;
// note that movements are "backwards," as pathfinds from goal to player
// this allows efficient reuse if the player ends up in a non-checked-area
// really only affects y as can always move right if can left
// so fall upwards, jump downwards, etc. but need to check conditions as if coming from where going to
for(int movement_index = 0; !done; movement_index++) {
	possible_x = block2->x;
	possible_y = block2->y;
	possible_cost = 1;
	if(possible_y < room_water_level) {
		// TODO: any direction and boosting out of water
		printf("ERR: underwater\n");
		done = true;
	}
	else if(room_zerog) {
		// TODO - don't bother giving directions other than goal?
		printf("ERR: zero-g\n");
		done = true;
	}
	// TODO: train giving no path half? of the time so it learns to move based on end goal direction and terrain faster
	else {
		switch(movement_index) {
			// falling straight down and diagonally
			case 0:
				possible_x +=  0;
				possible_y +=  1;
				break;
			case 1:
				possible_x += -1;
				possible_y +=  1;
				break;
			case 2:
				possible_x +=  1;
				possible_y +=  1;
				break;
			// moving left and right, climbing poles horizontally
			case 3:
				if(possible_y == 0 || room[(possible_y-1)*room_w+possible_x] == 0) continue;
				possible_x += -1;
				possible_y +=  0;
				break;
			case 4:
				if(possible_y == 0 || room[(possible_y-1)*room_w+possible_x] == 0) continue;
				possible_x +=  1;
				possible_y +=  0;
				break;
			// jumping straight up, climbing poles vertically
			case 5:
				// don't allow teleporting through a one-thick floor
				if(possible_y < 1 || room[(possible_y-1)*room_w+possible_x] == 2) break;
				for(int i = 2; i <= 5; i++) {
					if(possible_y < i || room[(possible_y-i)*room_w+possible_x] == 0) continue;
					possible_x +=  0;
					possible_y += -(i-1);
					possible_cost = i-1;
					break;
				}
				break;
			// TODO: jumping diagonally, pouncing?
			// climbing up tunnel
			// TODO: when climbing up tunnel or in a horizontal one raise cost
			// TODO: make falling cost very low
			case 6:
				if(possible_x < 1 || possible_x >= room_w-2) continue;
				if(room[possible_y*room_w+(possible_x-1)] != 2 ||
				   room[possible_y*room_w+(possible_x+1)] != 2
				) continue;
				possible_x +=  0;
				possible_y += -1;
				break;
			// TODO: walljumping
			default:
				done = true;
		}
	}
	if(possible_x < 0 || possible_x >= room_w) continue;
	if(possible_y < 0 || possible_y >= room_h) continue;
	if(possible_x == block2->x && possible_y == block2->y) continue;
	// don't allow moving into solid blocks
	if(room[possible_y*room_w+possible_x] == 2) continue;
//}
