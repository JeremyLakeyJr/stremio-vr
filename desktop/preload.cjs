const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('movieNight',{emit:value=>ipcRenderer.send('movie-night-rtc',value),receive:callback=>ipcRenderer.on('movie-night-command',(_event,value)=>callback(value))});
