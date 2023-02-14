#include <algorithm>
#include <cmath>
#include <mlpack.hpp>
#include <mutex>
#include <thread>
#include <type_traits>
extern "C" {
#include "RWAI.h"
}

using namespace mlpack;

#define     room_index 0
#define pathfind_index     room_index+ room_vision*room_vision+2 // vision, underwater, zero-g
#define    world_index pathfind_index+ 2*4+1 // 5, 10, 20, goal, dist to goal
//#define      pos_index    world_index+ 2+creature_vision*creature_vision+food_vision*food_vision+itemVision*itemVision // cycle times, creatures/food/items
#define      pos_index    world_index
#define   player_index      pos_index+ 2+2 // pos, pos on block, velocity
// TODO: broken
//       I hit a magic number or something, changing input_length at all kills things
//       played with it for two hours now, giving up for a bit
//       really want input history though
//#define    input_index   player_index+ 10+25+2+2*(1+4+1)+(1+4) // misc. player info
//#define   input_length    input_index+ 2+5*input_memory // average speed and previous inputs
#define   input_length   player_index+ 10+25+2+2*(1+4+1)+(1+4) // misc. player info

// mlpack breaks with c++20 so abuse mutexes instead of using binary_semaphores
std::mutex has_data;
std::mutex collected_data;
std::mutex has_inputs;
int inputs;

class RWAI {
	public:
		class State {
			public:
				State() : /*input_history(5*input_memory, arma::fill::zeros),*/ data(input_length) {
					average_speed(0);
				}

				// encode the state to a column vector
				const arma::colvec& Encode() const { return data; }
				// dimension of the encoded state
				static const size_t dimension = input_length;
				bool died = false, succeeded = false;
				arma::colvec input_history;
				arma::running_stat<double> average_speed;

/*{{{ State* update()*/
				void update() {
					has_data.lock();
					if(exiting) return;
					this->died = ::died;
					this->succeeded = ::succeeded;

	/*{{{ room*/{
					for(int i = 0; i < room_vision*room_vision; i++) {
						if(block_x-room_vision/2+(i%room_vision) < 0 || block_x-room_vision/2+(i%room_vision) >= room_w)
							data[room_index+i] = 1;
						else
							data[room_index+i] = (room[i] <= 2) ? room[i]/2.0 : -1;
					}
					data[room_index+room_vision*room_vision+0] = block_y < room_water_level;
					data[room_index+room_vision*room_vision+1] = room_zerog;
	}/*}}}*/

	/*{{{ pathfind*/{
					int x, y;
					double mag;
					// TODO: check if in pipe before doing this, don't want to fully pathfind big room if don't have to
					//       might fix itself once back-and-forth between this and game figured out
					pathfind(block_x, block_y,  5, &x, &y);
					data[pathfind_index+0] = x-block_x;
					data[pathfind_index+1] = y-block_y;
					mag = sqrt(pow(data[pathfind_index+0], 2)+pow(data[pathfind_index+1], 2)); data[pathfind_index+0] /= mag; data[pathfind_index+1] /= mag;
					pathfind(block_x, block_y, 10, &x, &y);
					data[pathfind_index+2] = x-block_x;
					data[pathfind_index+3] = y-block_y;
					mag = sqrt(pow(data[pathfind_index+2], 2)+pow(data[pathfind_index+3], 2)); data[pathfind_index+2] /= mag; data[pathfind_index+3] /= mag;
					pathfind(block_x, block_y, 20, &x, &y);
					data[pathfind_index+4] = x-block_x;
					data[pathfind_index+5] = y-block_y;
					mag = sqrt(pow(data[pathfind_index+4], 2)+pow(data[pathfind_index+5], 2)); data[pathfind_index+4] /= mag; data[pathfind_index+5] /= mag;
					data[pathfind_index+6] = goal_x-block_x;
					data[pathfind_index+7] = goal_y-block_y;
					mag = sqrt(pow(data[pathfind_index+6], 2)+pow(data[pathfind_index+7], 2)); data[pathfind_index+6] /= mag; data[pathfind_index+7] /= mag;
					data[pathfind_index+8] = 1-std::min(1.0, mag/40);
					for(int i = pathfind_index; i <= pathfind_index+8; i++) {
						if(!std::isfinite(data[i])) data[i] = 0;
					}
	}/*}}}*/

	/*{{{ world*/{
					//data[world_index+0] = cycle_length/(double)(800*40);
					//data[world_index+1] = cycle_elapsed/(double)cycle_length;
					//for(int i = 0; i < creature_vision*creature_vision; i++) {
					//	data[world_index+2+i] = creatures[i]/2.0;
					//}
					//for(int i = 0; i < food_vision*food_vision; i++) {
					//	data[world_index+2+creature_vision*creature_vision+i] = food[i];
					//}
					//for(int i = 0; i < itemVision*itemVision; i++) {
					//	data[world_index+2+creature_vision*creature_vision+food_vision*food_vision+i] = items[i]/2.0;
					//}
	}/*}}}*/

	/*{{{ pos*/{
					// these are bad occasionally
					data[pos_index+0] = std::max(0.0f, std::min(1.0f, block_pos_x));
					data[pos_index+1] = std::max(0.0f, std::min(1.0f, block_pos_y));
					data[pos_index+2] = copysign(1.0, vel_x)*std::min(1.0f, fabs(vel_x)/100);
					data[pos_index+3] = copysign(1.0, vel_y)*std::min(1.0f, fabs(vel_y)/100);
	}/*}}}*/

	/*{{{ player*/{
					for(int i = 0; i < 10; i++) {
						data[player_index+i] = (i == body_mode) ? 1 : 0;
					}
					for(int i = 0; i < 25; i++) {
						data[player_index+10+i] = (i == animation) ? 1 : 0;
					}
					data[player_index+10+25+0] = air_in_lungs;
					data[player_index+10+25+1] = object_in_stomach;
					for(int i = 0; i < 2; i++) {
						data[player_index+10+25+2+i*(1+4+1)+0] = hand[i];
						data[player_index+10+25+2+i*(1+4+1)+1] = (hand_type[i] & (1 << 0)) ? 1 : 0;
						data[player_index+10+25+2+i*(1+4+1)+2] = (hand_type[i] & (1 << 1)) ? 1 : 0;
						data[player_index+10+25+2+i*(1+4+1)+3] = (hand_type[i] & (1 << 2)) ? 1 : 0;
						data[player_index+10+25+2+i*(1+4+1)+4] = (hand_type[i] & (1 << 3)) ? 1 : 0;
						data[player_index+10+25+2+i*(1+4+1)+5] = hand_swallowable[i];
					}
					data[player_index+10+25+2+2*(1+4+1)+0] = item;
					data[player_index+10+25+2+2*(1+4+1)+1] = (item_type & (1 << 0)) ? 1 : 0;
					data[player_index+10+25+2+2*(1+4+1)+2] = (item_type & (1 << 1)) ? 1 : 0;
					data[player_index+10+25+2+2*(1+4+1)+3] = (item_type & (1 << 2)) ? 1 : 0;
					data[player_index+10+25+2+2*(1+4+1)+4] = (item_type & (1 << 3)) ? 1 : 0;
	}/*}}}*/

	/*{{{ input history*/{
					//data[input_index] = 0; //average_speed.mean();
					//for(int i = 0; i < 5*input_memory; i++) {
					//	data[input_index+1+i] = 0; //input_history[i];
					//}
	}/*}}}*/

	/*{{{ check for bad data*/
					for(int i = 0; i < input_length; i++) {
						if(data[i] < -0.05 || data[i] > 1.05) {
							if(i < pathfind_index) {
								if(data[i] > -1.05 && data[i] < 1.05) continue;
								std::cout << "Bad data in room section";
							}
							else if(i < world_index) {
								if(data[i] > -1.05 && data[i] < 1.05) continue;
								std::cout << "Bad data in pathfind section";
							}
							else if(i < pos_index)
								std::cout << "Bad data in world section";
							else if(i < player_index)
								std::cout << "Bad data in pos section";
							//else if(i < input_index)
							//	std::cout << "Bad data in player section";
							else
								std::cout << "Bad data in input history section";
							std::cout << ": " << data[i] << " at " << i << std::endl;
							throw std::runtime_error("Bad data");
						}
					}
	/*}}}*/

					collected_data.unlock();
				}
/*}}}*/

			private:
				arma::colvec data;
		};

		class Action {
			public:
				// lol not about to put all button combinations in an enum
				//enum actions {
				//	backward,
				//	forward
				//};
				int action;
				// size of the action space
				static const size_t size = 1 << 7;
		};

		// get next state and reward based on current state and action
		double Sample(const State& state, const Action& action, State& next_state) {
			steps_performed++;
			inputs = action.action;
			has_inputs.unlock();
			next_state.update();
			//for(int i = 5; i < 5*input_memory; i++) {
			//	next_state.input_history[i] = state.input_history[i-5];
			//}
			//next_state.input_history[0] = (((action.action & (1 << 0)) != 0) ? 1 : 0)-(((action.action & (1 << 1)) != 0) ? 1 : 0);
			//next_state.input_history[1] = (((action.action & (1 << 2)) != 0) ? 1 : 0)-(((action.action & (1 << 3)) != 0) ? 1 : 0);
			//next_state.input_history[2] = ((action.action & (1 << 4)) != 0) ? 1 : 0;
			//next_state.input_history[3] = ((action.action & (1 << 5)) != 0) ? 1 : 0;
			//next_state.input_history[4] = ((action.action & (1 << 6)) != 0) ? 1 : 0;
			if(exiting) return 0;

			if(exiting || next_state.died || (max_steps != 0 && steps_performed >= max_steps))
				return -1000*(abs(goal_x-block_x)+abs(goal_y-block_y));
			if(state.succeeded)
				return 1000+9000*(1-steps_performed/(60.0*40));
			float speed  = std::min(1.0f, fabs(vel_x)/100);
			      speed += std::min(1.0f, fabs(vel_y)/100);
			//next_state.average_speed(speed/2);
			//// give a more current average speed
			//if(steps_performed%10 == 0) {
			//	double last = next_state.average_speed.mean();
			//	next_state.average_speed.reset();
			//	for(int i = 0; i < 5; i++) { next_state.average_speed(last); }
			//}
			// don't constantly reward for getting closer because that screws with pathfinding
			// could reward for following given path but if it can find its own better ways I would prefer it did that
			return speed;
		}
		double Sample(const State& state, const Action& action) {
			State next_state;
			return Sample(state, action, next_state);
		}

		// initial state for a new episode
		State InitialSample() {
			steps_performed = 0;
			return State();
		}

		bool IsTerminal(const State& state) const {
			if(max_steps != 0 && steps_performed >= max_steps) return true;
			if(state.died || state.succeeded || exiting) return true;
			return false;
		}

		size_t StepsPerformed() const { return steps_performed; }
		// getter and setter
		size_t MaxSteps() const { return max_steps; }
		size_t& MaxSteps() { return max_steps; }

	private:
		size_t max_steps = 0;
		size_t steps_performed = 0;
};

/*{{{ void AI_thread()*/
static void AI_thread() {

	/*{{{ model*/
	// see https://github.com/mlpack/mlpack/issues/2849
	FFN<MeanSquaredError, GaussianInitialization> network(MeanSquaredError(), GaussianInitialization(0, 0.001));
	// maybe batch normalization instead of dropout but not updated to 4
	// https://github.com/mlpack/examples/blob/master/mnist_batch_norm/mnist_batch_norm.cpp
	// well, dropout is causing errors so neither :(
	network.Add<Linear>(input_length);

	network.Add<LeakyReLU>();
	//network.Add<Dropout>(0.5);
	network.Add<Linear>((1 << 7)+(int)(2.0/3*input_length));

	network.Add<LeakyReLU>();
	//network.Add<Dropout>(0.5);
	network.Add<Linear>((1 << 7)+(int)(2.0/3*input_length));

	network.Add<LeakyReLU>();
	network.Add<Linear>(1 << 7);
	SimpleDQN<> model(network);
	/*}}}*/

	// initial epsilon values, interval of epsilon decrease, epsilon min
	GreedyPolicy<RWAI> policy(1.0, 1000, 0.1, 0.99);
	// batch size returned to train on when using replay, number to store
	// TODO: larger batch size means larger amount of work at once on GPU, should be faster but isn't - experiment
	RandomReplay<RWAI> replay_method(100, 10000);

	TrainingConfig config;
	config.StepSize() = 0.01;
	config.Discount() = 0.9; // prioritize long-term rewards https://en.wikipedia.org/wiki/Q-learning#Discount_factor
	config.ExplorationSteps() = 2*40; // only start learning after exploring this many steps
	config.DoubleQLearning() = false;
	config.StepLimit() = 0;

	QLearning<RWAI, decltype(model), ens::AdamUpdate, decltype(policy)> agent(config, model, policy, replay_method);
	data::Load("model.bin", "model", model.Parameters());
	arma::running_stat<double> average_return;
	for(int i = 1; ; i = (i+1)%60) {
		average_return(agent.Episode());
		if(i == 0) data::Save("model.bin", "model", model.Parameters());
		if(exiting) break;
		std::cout << "Average return: " << average_return.mean() << std::endl;
	}
}
/*}}}*/

static std::thread* train_thread;
void AI_init() {
	has_data.lock();
	has_inputs.lock();
	train_thread = new std::thread(AI_thread);
}
void AI_exit() {
	has_data.unlock();
	train_thread->join();
}
int has_data_get_inputs() {
	has_inputs.lock();
	int this_inputs = inputs;
	has_data.unlock();
	collected_data.lock();
	return this_inputs;
}
