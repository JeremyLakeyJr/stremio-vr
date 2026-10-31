// Pin public destinations for every HTTP request and HTTPS CONNECT. Chromium never
// gets a direct route to Docker services, host loopback, or cloud metadata over HTTP.
import http from 'node:http';import net from 'node:net';import {destination} from '../server/media.js';
export function browserProxy(options={}){
 const sockets=new Set();const server=http.createServer(async(req,res)=>{
  try{
   const {url,address}=await destination(req.url,[],options.resolve);
   if(url.protocol!=='http:')throw Error('Use HTTPS CONNECT');
   const headers={...req.headers,host:url.host};delete headers['proxy-authorization'];delete headers['proxy-connection'];
   const out=http.request({hostname:address.address,family:address.family,port:url.port||80,path:url.pathname+url.search,method:req.method,headers},up=>{res.writeHead(up.statusCode,up.headers);up.pipe(res);});
   out.setTimeout(30000,()=>out.destroy());out.on('error',()=>{if(!res.headersSent)res.writeHead(502);res.end();});req.on('aborted',()=>out.destroy());req.pipe(out);
  }catch{res.writeHead(403);res.end('This browser can reach public HTTP(S) sites only.');}
 });
 server.on('connection',s=>{sockets.add(s);s.on('close',()=>sockets.delete(s));});
 server.on('connect',async(req,client,head)=>{
  try{
   if(!/^(?:[a-zA-Z0-9.-]+|\[[a-fA-F0-9:]+\]):\d{1,5}$/.test(req.url))throw Error('Invalid CONNECT');
   const {url,address}=await destination('https://'+req.url,[],options.resolve);
   const upstream=net.connect({host:address.address,port:Number(url.port)||443,family:address.family});sockets.add(upstream);upstream.on('close',()=>sockets.delete(upstream));
   upstream.once('connect',()=>{client.write('HTTP/1.1 200 Connection Established\r\n\r\n');if(head.length)upstream.write(head);client.pipe(upstream);upstream.pipe(client);});
   upstream.on('error',()=>client.destroy());client.on('error',()=>upstream.destroy());client.on('close',()=>upstream.destroy());upstream.setTimeout(120000,()=>upstream.destroy());
  }catch{client.end('HTTP/1.1 403 Forbidden\r\n\r\n');}
 });
 return {server,close:async()=>{for(const s of sockets)s.destroy();await new Promise(r=>server.close(r));}};
}
