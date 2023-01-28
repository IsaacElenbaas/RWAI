import socket
s = socket.socket()
port = 12345
s.bind(('127.0.0.1', port))
s.listen(5)
c, addr = s.accept()
#print('Got connection from', addr)
while True:
	data = c.recv(1024)
	if not data: break
	print(data.decode(), end='')
c.close()
