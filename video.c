#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>

#define BUFSIZE 64*1024

int main(int argc, char* argv[]) {
	struct sockaddr_in addr;
	addr.sin_family = AF_INET;
	addr.sin_addr.s_addr = inet_addr("127.0.0.1");
	addr.sin_port = htons(8325);
	int sockfd = socket(AF_INET, SOCK_STREAM, 0);
	bind(sockfd, (struct sockaddr*)&addr, sizeof(addr));
	listen(sockfd, 1);
	socklen_t addrlen = sizeof(addr);
	sockfd = accept(sockfd, (struct sockaddr*)&addr, &addrlen);
	char buffer[BUFSIZE];
	int length;
	while((length = recv(sockfd, buffer, BUFSIZE, 0))) {
		write(1, buffer, length);
	}
	return 0;
}
