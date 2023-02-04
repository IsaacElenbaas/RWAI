#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>

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
	while(length = recv(sockfd, buffer, BUFSIZE, 0)) {
		write(1, buffer, length);
	}
	return 0;
}
