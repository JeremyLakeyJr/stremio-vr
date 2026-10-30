using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace StremioVR {
public sealed class TabletopGames : MonoBehaviour {
 public RoomClient Room; public WorldManager World; public event Action<string> Status;
 public string Promotion="q";
 readonly List<Board> boards=new List<Board>(); readonly Dictionary<Color,Material> materials=new Dictionary<Color,Material>();
 GameObject tables; string layoutKey;
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
  CancelGrab();
  if(tables){tables.SetActive(false);Destroy(tables);}boards.Clear();tables=new GameObject("Tabletop games");tables.transform.SetParent(transform,false);
  foreach(var item in Room.State?["layout"]?["items"] as JArray??new JArray()) {
   string kind=(string)item["kind"];if(kind!="chess"&&kind!="tictactoe")continue;
   MakeBoard(kind,kind=="chess"?8:3,WorldManager.LayoutVector(item["position"]));boards[boards.Count-1].Root.rotation=Quaternion.Euler(WorldManager.LayoutVector(item["rotation"]));
  }
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
 void Message(JObject message) {
  string type=(string)message["type"];if(type!="state"&&type!="disconnected"&&type!="left")return;
  string key=Room.RoomId+":"+(string)message["layout"]?["revision"];
  if(key!=layoutKey){layoutKey=key;Build();}else Refresh();
 }
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
   if(revision==board.Revision && Room.RoomId==board.RoomId)continue;if(grabBoard==board)CancelGrab();board.Revision=revision;board.RoomId=Room.RoomId;board.Selected=-1;
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
  var pieceRoot=new GameObject("Chess piece "+piece+" "+Square(index));pieceRoot.transform.SetParent(board.Pieces,false);
  var target=pieceRoot.AddComponent<TabletopTarget>();target.Owner=this;target.Game="chess";target.Cell=index;
  string name=type=='p'?"Pawn":type=='r'?"Rook":type=='n'?"Knight":type=='b'?"Bishop":type=='q'?"Queen":"King";
  var mesh=Resources.Load<Mesh>("Chess/"+name);if(!mesh){Destroy(pieceRoot);return;}
  pieceRoot.transform.localPosition=p;
  pieceRoot.AddComponent<MeshFilter>().sharedMesh=mesh;pieceRoot.AddComponent<MeshRenderer>().sharedMaterial=Material(color);
  var collider=pieceRoot.AddComponent<BoxCollider>();collider.center=mesh.bounds.center;collider.size=mesh.bounds.size;

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
 Board grabBoard;Transform grabbed;Vector3 originalPosition,grabOffset;Quaternion originalRotation;int grabCell,grabHand;bool leftGripWas,rightGripWas;
 public bool Grabbing=>grabbed;
 void CancelGrab(){if(grabbed){grabbed.localPosition=originalPosition;grabbed.localRotation=originalRotation;}grabbed=null;grabBoard=null;}
 public bool Grip(Transform left,Transform right,bool leftGrip,bool rightGrip,bool allowStart){
  bool consumed=Grabbing;
  if(grabbed){
   Transform hand=grabHand==0?left:right;bool held=grabHand==0?leftGrip:rightGrip;
   if(held){grabbed.position=hand.position+hand.rotation*grabOffset;grabbed.rotation=hand.rotation;}
   else{
    var board=grabBoard;int from=grabCell;Vector3 local=board.Root.InverseTransformPoint(grabbed.position);
    int col=Mathf.FloorToInt((local.x+0.56f)/(1.12f/8)),row=Mathf.FloorToInt((0.56f-local.z)/(1.12f/8));
    var state=Room.State?["games"]?["chess"] as JObject;CancelGrab();
    int to=row*8+col;bool legal=false;
    if(state!=null && col>=0&&col<8&&row>=0&&row<8&&Mathf.Abs(local.y-0.84f)<0.5f)
     foreach(var move in state["legal"] as JArray??new JArray())if((string)move["from"]==Square(from)&&(string)move["to"]==Square(to))legal=true;
    if(legal)Move(state,"chess",new JObject{["from"]=Square(from),["to"]=Square(to),["promotion"]=Promotion});else Status?.Invoke("Piece returned. Drop above a green legal square.");
   }
  }else if(allowStart){
   if(leftGrip&&!leftGripWas)consumed=TryGrab(left,0);
   if(!consumed&&rightGrip&&!rightGripWas)consumed=TryGrab(right,1);
  }
  leftGripWas=leftGrip;rightGripWas=rightGrip;return consumed;
 }
 bool TryGrab(Transform hand,int handIndex){
  TabletopTarget best=null;float distance=.18f;
  foreach(var hit in Physics.OverlapSphere(hand.position,.18f)){
   var target=hit.GetComponent<TabletopTarget>();if(!target||target.Game!="chess"||target.Action!=null||!target.GetComponent<MeshFilter>())continue;
   // Board squares are cubes; only original imported pieces have a Chess piece name.
   if(!target.name.StartsWith("Chess piece ",StringComparison.Ordinal))continue;
   float d=Vector3.Distance(hand.position,hit.ClosestPoint(hand.position));if(d<distance){distance=d;best=target;}
  }
  if(!best)return false;var board=boards.Find(b=>b.Game=="chess");var state=Room.State?["games"]?["chess"] as JObject;
  if(board==null||state==null)return false;int turn=(int)state["turn"];string piece=(string)state["board"]?[best.Cell];
  if((string)state["players"]?[turn]!=Room.PlayerId||string.IsNullOrEmpty(piece)||char.IsUpper(piece[0])!=(turn==0)){Status?.Invoke("Join a seat and pick up your own piece on your turn.");return false;}
  Select("chess",best.Cell);grabBoard=board;grabbed=best.transform;grabCell=best.Cell;grabHand=handIndex;originalPosition=grabbed.localPosition;originalRotation=grabbed.localRotation;grabOffset=Quaternion.Inverse(hand.rotation)*(grabbed.position-hand.position);return true;
 }
 void Move(JObject state,string game,JObject message){message["type"]="game";message["game"]=game;message["action"]="move";message["revision"]=state["revision"].DeepClone();Room.Send(message);}
 public static string Square(int index)=>((char)('a'+index%8)).ToString()+(8-index/8);
 public static int Index(string square)=>(8-(square[1]-'0'))*8+square[0]-'a';
 void OnDestroy(){if(Room)Room.Message-=Message;if(World)World.Changed-=Build;foreach(var material in materials.Values)if(material)Destroy(material);}
 sealed class Board {public string Game,RoomId;public int N,Revision=int.MinValue,Selected=-1;public Transform Root,Pieces;public Renderer[] Cells;public TextMesh Label;}
}
public sealed class TabletopTarget:MonoBehaviour {public TabletopGames Owner;public string Game,Action;public int Cell;public void Activate(){if(string.IsNullOrEmpty(Action))Owner.Select(Game,Cell);else Owner.Command(Game,Action);}}
}
