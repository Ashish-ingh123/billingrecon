// Stand-ins for the test of the watchdog (watch.test.sh, ADR 0056): nginx's /health/ready and Seq's /api/events on one
// HTTP port, and a mail server that keeps every message in a file. Node, no package.
//   node watch-test-servers.mjs <http port> <smtp port> <mails file>
// /ok/health/ready answers 200 and /down/health/ready 503; /busy/api/events answers 25 errors and /calm/api/events
// none, each only with the header X-Seq-ApiKey: test-key.
import { createServer as createHttp } from 'node:http';
import { createServer as createTcp } from 'node:net';
import { appendFileSync, writeFileSync } from 'node:fs';

const [httpPort, smtpPort, file] = [Number(process.argv[2]), Number(process.argv[3]), process.argv[4]];
writeFileSync(file, '');

createHttp((request, response) => {
  const url = new URL(request.url ?? '/', 'http://localhost');
  if (url.pathname.endsWith('/health/ready')) {
    response.writeHead(url.pathname.startsWith('/ok/') ? 200 : 503).end();
    return;
  }

  if (url.pathname.endsWith('/api/events')) {
    if (request.headers['x-seq-apikey'] !== 'test-key' || url.searchParams.get('filter') !== "@Level = 'Error'") {
      response.writeHead(401).end();
      return;
    }

    const count = url.pathname.startsWith('/busy/') ? Math.min(25, Number(url.searchParams.get('count') ?? 25)) : 0;
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end(JSON.stringify(Array.from({ length: count }, (_, index) => ({ Id: `event-${String(index)}` }))));
    return;
  }

  response.writeHead(404).end();
}).listen(httpPort, '127.0.0.1');

createTcp((socket) => {
  let buffer = '';
  let data = null;
  socket.write('220 test ESMTP\r\n');
  socket.on('error', () => {});
  socket.on('data', (chunk) => {
    buffer += chunk.toString('latin1');
    for (;;) {
      if (data !== null) {
        const end = buffer.indexOf('\r\n.\r\n');
        if (end < 0) return;
        appendFileSync(file, `${data}${buffer.slice(0, end)}\n=== end of mail ===\n`);
        buffer = buffer.slice(end + 5);
        data = null;
        socket.write('250 kept\r\n');
        continue;
      }

      const line = buffer.indexOf('\r\n');
      if (line < 0) return;
      const command = buffer.slice(0, line).toUpperCase();
      buffer = buffer.slice(line + 2);
      if (command.startsWith('EHLO') || command.startsWith('HELO')) socket.write('250-test\r\n250 8BITMIME\r\n');
      else if (command.startsWith('DATA')) {
        data = '';
        socket.write('354 go on\r\n');
      } else if (command.startsWith('QUIT')) {
        socket.write('221 bye\r\n');
        socket.end();
      } else socket.write('250 ok\r\n');
    }
  });
}).listen(smtpPort, '127.0.0.1', () => console.log('ready'));
