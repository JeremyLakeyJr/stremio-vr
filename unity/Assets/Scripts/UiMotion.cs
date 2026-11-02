using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace StremioVR {
public sealed class UiMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {
 public bool Card; Image background; Color resting; bool hovered; float press; Vector3 restingPosition; UnityEngine.UI.Selectable control;
 void Awake() { background=GetComponent<Image>(); if(background) resting=background.color; restingPosition=transform.localPosition; control=GetComponent<UnityEngine.UI.Selectable>(); }
 public void OnPointerEnter(PointerEventData data) { if(control && !control.IsInteractable())return; hovered=true; MenuFeedback.Current?.Play(transform.position); }
 public void OnPointerExit(PointerEventData data) { hovered=false; }
 public void OnPointerClick(PointerEventData data) { if(control && !control.IsInteractable())return; press=0.12f; MenuFeedback.Current?.Play(transform.position,true); }
 void Update() {
  bool reduced=MenuFeedback.Current && MenuFeedback.Current.ReducedMotion;
  press=Mathf.Max(0,press-Time.unscaledDeltaTime); float scale=reduced?1:press>0?0.975f:hovered?(Card?1.035f:1.015f):1;
  transform.localScale=Vector3.Lerp(transform.localScale,Vector3.one*scale,1-Mathf.Exp(-16*Time.unscaledDeltaTime));
  transform.localPosition=Vector3.Lerp(transform.localPosition,restingPosition+Vector3.back*(hovered&&!reduced?4:0),1-Mathf.Exp(-16*Time.unscaledDeltaTime));
  if(background) background.color=Color.Lerp(background.color,hovered?new Color(0.31f,0.23f,0.61f):resting,1-Mathf.Exp(-12*Time.unscaledDeltaTime));
 }
}
public sealed class PageFade : MonoBehaviour {
 CanvasGroup group; float elapsed;
 void Awake() { group=gameObject.AddComponent<CanvasGroup>(); }
 public void Begin() { elapsed=MenuFeedback.Current && MenuFeedback.Current.ReducedMotion?1:0; group.alpha=elapsed>0?1:0; }
 void Update() { elapsed+=Time.unscaledDeltaTime; group.alpha=Mathf.SmoothStep(0,1,elapsed/0.22f); }
}
}
namespace StremioVR {
public sealed class PanelMotion : MonoBehaviour {
 public bool Visible { get; private set; }=true;
 CanvasGroup group; Vector3 scale; float alpha=1;
 void Awake() { group=gameObject.AddComponent<CanvasGroup>(); scale=transform.localScale; }
 public void Show(bool value) { Visible=value; if(value) gameObject.SetActive(true); group.blocksRaycasts=value; group.interactable=value; }
 void Update() { if(MenuFeedback.Current && MenuFeedback.Current.ReducedMotion)alpha=Visible?1:0; alpha=Mathf.MoveTowards(alpha,Visible?1:0,Time.unscaledDeltaTime*6); group.alpha=Mathf.SmoothStep(0,1,alpha); transform.localScale=scale*(0.96f+0.04f*group.alpha); if(!Visible && alpha<=0) gameObject.SetActive(false); }
}
}
