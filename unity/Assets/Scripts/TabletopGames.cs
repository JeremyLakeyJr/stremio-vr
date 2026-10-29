using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace StremioVR {
public sealed class TabletopGames : MonoBehaviour {
 public RoomClient Room; public WorldManager World; public event Action<string> Status;
 public string Promotion="q";
 readonly List<Board> boards=new List<Board>(); readonly Dictionary<Color,Material> materials=new Dictionary<Color,Material>();
 GameObject tables;
 public void Initialize(RoomClient room,WorldManager world) {Room=room;World=world;Room.Message+=Message;World.Changed+=Build;Build();}
 public void Command(string game,string action) {
  if(!Room.Connected||Room.State==null){Status?.Invoke("Create or join a room to play tabletop games");return;}
  Room.Send(new JObject { ["type"]="game",["game"]=game,["action"]=action });
 }
 public void SetPromotion(string value) {Promotion=value;Status?.Invoke("Promotion: "+value.ToUpperInvariant());}
 Material Material(Color color) {if(!materials.TryGetValue(color,out var mat)){mat=new Material(Shader.Find("StremioVR/Avatar"));mat.color=color;mat.enableInstancing=true;materials[color]=mat;}return mat;}
 GameObject Shape(Transform parent,PrimitiveType type,Vector3 position,Vector3 scale,Color color,bool collision=false) {
  var go=GameObject.CreatePrimitive(type);go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=Material(color);
  if(!collision)Destroy(go.GetComponent<Collider>());return go;
 }
 TextMesh Text(Transform parent,string value,Vector3 position,float size,Color color) {
  var go=new GameObject("Label",typeof(TextMesh));go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localRotation=Quaternion.Euler(90,0,0);
  var text=go.GetComponent<TextMesh>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");go.GetComponent<Renderer>().sharedMaterial=text.font.material;text.text=value;text.fontSize=48;text.characterSize=size;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=color;return text;
 }
 void Build() {
  if(tables){tables.SetActive(false);Destroy(tables);}boards.Clear();tables=new GameObject("Tabletop games");tables.transform.SetParent(transform,false);
  MakeBoard("chess",8,World.Data.Spawn+new Vector3(-1.8f,0,1.5f)); MakeBoard("tictactoe",3,World.Data.Spawn+new Vector3(0.3f,0,1.5f));
  Refresh();
 }
 void MakeBoard(string game,int n,Vector3 position) {
  var root=new GameObject(game);root.transform.SetParent(tables.transform,false);root.transform.position=position;
  var board=new Board {Game=game,Root=root.transform,N=n,Cells=new Renderer[n*n],Pieces=new GameObject("Pieces").transform};board.Pieces.SetParent(root.transform,false);boards.Add(board);
  Shape(root.transform,PrimitiveType.Cube,new Vector3(0,0.74f,-0.1f),new Vector3(1.45f,0.1f,1.8f),new Color(0.08f,0.06f,0.12f),true);
  foreach(float x in new[]{-0.6f,0.6f})foreach(float z in new[]{-0.8f,0.6f})Shape(root.transform,PrimitiveType.Cube,new Vector3(x,0.35f,z),new Vector3(0.08f,0.7f,0.08f),Color.gray,true);
  float cell=1.12f/n;
  for(int index=0;index<n*n;index++) {
   var tile=Shape(root.transform,PrimitiveType.Cube,Cell(index,n,0.805f),new Vector3(cell*0.97f,0.025f,cell*0.97f),SquareColor(index,n),true); board.Cells[index]=tile.GetComponent<Renderer>();
   var action=tile.AddComponent<TabletopTarget>();action.Owner=this;action.Game=game;action.Cell=index;
  }
  board.Label=Text(root.transform,game=="chess"?"CHESS":"TIC-TAC-TOE",new Vector3(0,0.81f,0.72f),0.035f,Color.white);
  string[] commands={"join","leave","reset"};for(int i=0;i<3;i++) {
   var button=Shape(root.transform,PrimitiveType.Cube,new Vector3((i-1)*0.43f,0.81f,-0.76f),new Vector3(0.38f,0.04f,0.18f),new Color(0.3f,0.18f,0.65f),true);
   var action=button.AddComponent<TabletopTarget>();action.Owner=this;action.Game=game;action.Action=commands[i];
   Text(root.transform,commands[i].ToUpperInvariant(),new Vector3((i-1)*0.43f,0.837f,-0.76f),0.025f,Color.white);
  }
 }
 static Vector3 Cell(int index,int n,float y) {float cell=1.12f/n;return new Vector3((index%n-(n-1)*0.5f)*cell,y,((n-1)*0.5f-index/n)*cell);}
 static Color SquareColor(int i,int n) => (i/n+i%n)%2==0?new Color(0.67f,0.64f,0.75f):new Color(0.16f,0.12f,0.25f);
 void Message(JObject message) {if((string)message["type"]=="state"||(string)message["type"]=="disconnected")Refresh();}
 public string Description(string game) {
  var state=Room.State?["games"]?[game];if(state==null)return Room.State==null?"Join a room to play":"Update the room server to enable games";
  var players=state["players"] as JArray;string names="";for(int i=0;i<2;i++){if(i>0)names+=" vs ";string id=(string)players[i];names+=id==null?"Open seat":Name(id);}
  string outcome=(string)state["outcome"];string turn=game=="chess"?((int)state["turn"]==0?"White":"Black"):((int)state["turn"]==0?"X":"O");
  return names+"\n"+(!string.IsNullOrEmpty(outcome)?outcome:turn+" to move"+((bool?)state["check"]==true?" — check":""));
 }
 string Name(string id) {foreach(var player in Room.State?["players"] as JArray??new JArray())if((string)player["id"]==id)return (string)player["profile"]?["name"]??"Player";return "Player";}
 void Refresh() {
  foreach(var board in boards) {
   board.Label.text=(board.Game=="chess"?"CHESS":"TIC-TAC-TOE")+"\n"+Description(board.Game);
   var state=Room.State?["games"]?[board.Game] as JObject; int revision=(int?)state?["revision"]??-1;
   if(revision==board.Revision && Room.RoomId==board.RoomId)continue;board.Revision=revision;board.RoomId=Room.RoomId;board.Selected=-1;
   foreach(Transform child in board.Pieces){child.gameObject.SetActive(false);Destroy(child.gameObject);}
   for(int i=0;i<board.Cells.Length;i++)board.Cells[i].sharedMaterial=Material(SquareColor(i,board.N));
   if(!(state?["board"] is JArray cells))continue;
   for(int i=0;i<cells.Count&&i<board.Cells.Length;i++) {string piece=(string)cells[i];if(string.IsNullOrEmpty(piece))continue;
    if(board.Game=="chess")ChessPiece(board,i,piece[0]);else Text(board.Pieces,piece,Cell(i,3,0.83f),0.15f,piece=="X"?new Color(0.1f,0.9f,0.9f):new Color(1,0.6f,0.1f));
   }
  }
 }
 void ChessPiece(Board board,int index,char piece) {
  Color color=char.IsUpper(piece)?new Color(0.95f,0.86f,0.62f):new Color(0.22f,0.31f,0.45f);Vector3 p=Cell(index,8,0.84f);char type=char.ToLowerInvariant(piece);
  Shape(board.Pieces,PrimitiveType.Cylinder,p,new Vector3(0.095f,0.018f,0.095f),color);
  float height=type=='p'?0.07f:type=='k'||type=='q'?0.15f:0.11f;
  Shape(board.Pieces,PrimitiveType.Cylinder,p+Vector3.up*height*0.5f,new Vector3(0.043f,height*0.5f,0.043f),color);
  var top=Shape(board.Pieces,type=='n'||type=='r'?PrimitiveType.Cube:PrimitiveType.Sphere,p+Vector3.up*height,new Vector3(0.065f,0.055f,0.065f),color);
  if(type=='n')top.transform.localRotation=Quaternion.Euler(25,0,0);
  if(type=='k') {Shape(board.Pieces,PrimitiveType.Cube,p+Vector3.up*(height+0.045f),new Vector3(0.018f,0.07f,0.018f),color);Shape(board.Pieces,PrimitiveType.Cube,p+Vector3.up*(height+0.055f),new Vector3(0.055f,0.018f,0.018f),color);}
  Text(board.Pieces,char.ToUpperInvariant(piece).ToString(),p+new Vector3(0,0.006f,-0.045f),0.018f,Color.white);
 }
 public void Select(string game,int index) {
  var board=boards.Find(b=>b.Game==game);var state=Room.State?["games"]?[game] as JObject;if(board==null||state==null){Status?.Invoke("Join a room with the updated game server");return;}
  var players=(JArray)state["players"];int seat=-1;for(int i=0;i<2;i++)if((string)players[i]==Room.PlayerId)seat=i;
  if(seat<0){Status?.Invoke("Select JOIN on the table first");return;}
  if((int)state["turn"]!=seat){Status?.Invoke("Wait for your turn");return;}
  if(game=="tictactoe"){Move(state,game,new JObject{["cell"]=index});return;}
  string piece=(string)state["board"]?[index];
  if(!string.IsNullOrEmpty(piece)&&char.IsUpper(piece[0])==(seat==0)) {
   board.Selected=index;for(int i=0;i<board.Cells.Length;i++)board.Cells[i].sharedMaterial=Material(SquareColor(i,8));board.Cells[index].sharedMaterial=Material(Color.yellow);
   foreach(var legal in state["legal"] as JArray??new JArray())if((string)legal["from"]==Square(index)){int target=Index((string)legal["to"]);board.Cells[target].sharedMaterial=Material(new Color(0.1f,0.65f,0.4f));}
   Status?.Invoke("Selected "+Square(index)+". Green squares are legal moves.");return;
  }
  if(board.Selected>=0)Move(state,game,new JObject{["from"]=Square(board.Selected),["to"]=Square(index),["promotion"]=Promotion});
 }
 void Move(JObject state,string game,JObject message){message["type"]="game";message["game"]=game;message["action"]="move";message["revision"]=state["revision"].DeepClone();Room.Send(message);}
 public static string Square(int index)=>((char)('a'+index%8)).ToString()+(8-index/8);
 public static int Index(string square)=>(8-(square[1]-'0'))*8+square[0]-'a';
 void OnDestroy(){if(Room)Room.Message-=Message;if(World)World.Changed-=Build;foreach(var material in materials.Values)if(material)Destroy(material);}
 sealed class Board {public string Game,RoomId;public int N,Revision=int.MinValue,Selected=-1;public Transform Root,Pieces;public Renderer[] Cells;public TextMesh Label;}
}
public sealed class TabletopTarget:MonoBehaviour {public TabletopGames Owner;public string Game,Action;public int Cell;public void Activate(){if(string.IsNullOrEmpty(Action))Owner.Select(Game,Cell);else Owner.Command(Game,Action);}}
}
