using UnityEngine;
using UnityEngine.UI;
namespace StremioVR {
public static class MenuSurface {
 static Sprite rounded;
 public static void Style(Image image){
  if(!rounded){const int size=64;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);texture.name="Menu rounded corners";texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;var pixels=new Color[size*size];for(int y=0;y<size;y++)for(int x=0;x<size;x++){float dx=Mathf.Max(12.5f-x,x-50.5f),dy=Mathf.Max(12.5f-y,y-50.5f);float distance=new Vector2(Mathf.Max(0,dx),Mathf.Max(0,dy)).magnitude;pixels[y*size+x]=new Color(1,1,1,Mathf.Clamp01(12.5f-distance));}texture.SetPixels(pixels);texture.Apply(false,true);rounded=Sprite.Create(texture,new Rect(0,0,size,size),Vector2.one*.5f,100,0,SpriteMeshType.FullRect,new Vector4(14,14,14,14));rounded.name="Menu rounded surface";}
  image.sprite=rounded;image.type=Image.Type.Sliced;image.pixelsPerUnitMultiplier=1;
 }
}
}
