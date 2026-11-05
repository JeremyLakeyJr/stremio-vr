const vm=require('node:vm');
class SoloRuntime {
 constructor(source,emit){this.source=source;this.emit=emit;this.timers=new Set();}
 start(initial){this.close();this.context=vm.createContext({initial,emit:this.emit,setTimeout:(fn,ms)=>{const id=setTimeout(()=>{this.timers.delete(id);if(this.context)fn();},ms);this.timers.add(id);return id;},clearTimeout:id=>{clearTimeout(id);this.timers.delete(id);}});new vm.Script(this.source+'\nglobalThis.solo=new MovieNightSolo(initial,emit);solo.start();').runInContext(this.context,{timeout:2000});}
 command(command){if(!this.context)return;this.context.command=command;new vm.Script('solo.command(command)').runInContext(this.context,{timeout:2000});}
 close(){for(const id of this.timers)clearTimeout(id);this.timers.clear();this.context=null;}
}
module.exports={SoloRuntime};
