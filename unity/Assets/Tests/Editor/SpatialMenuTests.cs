using NUnit.Framework;
using UnityEngine;
namespace StremioVR.Tests {
public class SpatialMenuTests {
 [TestCase(0f)][TestCase(-15f)]
 public void OffsetControllerHitsPhysicalSurface(float angle){var surface=new GameObject("Menu surface");try{surface.transform.position=new Vector3(-.5f,1.4f,1.6f);surface.transform.rotation=Quaternion.Euler(0,angle,0);var target=surface.transform.TransformPoint(new Vector3(.12f,.08f,0));var origin=new Vector3(.3f,1.1f,.2f);var ray=new Ray(origin,target-origin);Assert.IsTrue(SpatialMenuGeometry.Intersect(surface.transform,ray,out float distance));Assert.Less(Vector3.Distance(ray.GetPoint(distance),target),.0001f);}finally{Object.DestroyImmediate(surface);}}
 [Test] public void SurfacesBehindControllerOrBeyondReachAreRejected(){var surface=new GameObject("Menu surface");try{surface.transform.position=Vector3.back;Assert.IsFalse(SpatialMenuGeometry.Intersect(surface.transform,new Ray(Vector3.zero,Vector3.forward),out _));surface.transform.position=Vector3.forward*5;Assert.IsFalse(SpatialMenuGeometry.Intersect(surface.transform,new Ray(Vector3.zero,Vector3.forward),out _));}finally{Object.DestroyImmediate(surface);}}
}
}
