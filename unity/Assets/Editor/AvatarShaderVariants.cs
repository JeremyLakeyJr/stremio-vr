using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
namespace StremioVR.Editor {
// Runtime VRM Standard materials use one unshadowed directional light and probes.
// They are never lightmapped and do not cast shadows or use deferred rendering.
public sealed class AvatarShaderVariants:IPreprocessShaders {
 public int callbackOrder=>0;
 static readonly string[] Unused={"LIGHTMAP_ON","DIRLIGHTMAP_COMBINED","DYNAMICLIGHTMAP_ON","SHADOWS_SCREEN","SHADOWS_CUBE","SHADOWS_DEPTH","SHADOWS_SHADOWMASK","LIGHTMAP_SHADOW_MIXING","VERTEXLIGHT_ON","FOG_LINEAR","FOG_EXP","FOG_EXP2"};
 public void OnProcessShader(Shader shader,ShaderSnippetData snippet,IList<ShaderCompilerData> variants){if(shader.name!="Standard")return;if(snippet.passType!=PassType.ForwardBase){variants.Clear();return;}for(int i=variants.Count-1;i>=0;i--)foreach(string name in Unused)if(variants[i].shaderKeywordSet.IsEnabled(new ShaderKeyword(shader,name))){variants.RemoveAt(i);break;}}
}
}
